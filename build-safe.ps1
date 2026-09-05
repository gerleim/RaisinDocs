#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Safe build wrapper that ensures all child processes are cleaned up.

.DESCRIPTION
    Runs dotnet build/test with guaranteed cleanup of child processes.
    Prevents stray dotnet/MSBuild/VBCSCompiler processes from accumulating.
#>

# CmdletBinding so that a parameter name this script does not have is an error rather than
# nothing. Without it PowerShell quietly drops unrecognised named arguments into $args, and
# "-ExtraArgs '-c Release'" - the inner function's parameter name, not this one - built Debug and
# reported success. That is the same silent-miss as the $Args bug the inner function documents.
[CmdletBinding()]
param(
    [ValidateSet('build', 'test', 'clean')]
    [string]$Command = 'build',

    [string]$Project = 'RaisinDocs.slnx',

    # Passed straight through to dotnet, so give it one element per argument:
    #   .\build-safe.ps1 -Command build -AdditionalArgs '-c','Release'
    [string[]]$AdditionalArgs = @()
)

$ErrorActionPreference = "Stop"

function Invoke-SafeBuild {
    param(
        [string]$Command,
        [string]$Project,
        # Not $Args. That is an automatic variable, and PowerShell binds nothing to a parameter
        # of that name without saying so - -Args @('-c','Release') arrived as an empty array, so
        # every "Release" build through this wrapper silently built Debug.
        [string[]]$ExtraArgs
    )

    # The full command, so a switch that failed to arrive is visible rather than assumed. That is
    # how the bug above went unnoticed: the banner said what it was asked to do, not what it ran.
    Write-Host "Starting build: dotnet $Command $Project $($ExtraArgs -join ' ')" -ForegroundColor Cyan

    # Get current dotnet processes before build
    $beforeCount = @(Get-Process dotnet -ErrorAction SilentlyContinue).Count

    try {
        # Build argument list
        $argumentList = @($Command, $Project)
        if ($ExtraArgs.Count -gt 0) {
            $argumentList += $ExtraArgs
        }

        # Start dotnet in a job so we can monitor it
        $job = Start-Process -FilePath "dotnet" `
            -ArgumentList $argumentList `
            -NoNewWindow `
            -PassThru `
            -WorkingDirectory (Get-Location)

        $mainPid = $job.Id
        Write-Host "Build process started (PID: $mainPid)" -ForegroundColor Gray

        # Wait for main process with timeout
        $timeout = [System.TimeSpan]::FromMinutes(30)
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

        while (!$job.HasExited -and $stopwatch.Elapsed -lt $timeout) {
            Start-Sleep -Milliseconds 500
        }

        if (!$job.HasExited) {
            Write-Host "⚠ Build timeout after 30 minutes, terminating..." -ForegroundColor Yellow
            Stop-Process -Id $mainPid -Force -ErrorAction SilentlyContinue
            throw "Build timeout"
        }

        $exitCode = $job.ExitCode
        $stopwatch.Stop()

        Write-Host "Build process exited (exit code: $exitCode, elapsed: $([Math]::Round($stopwatch.Elapsed.TotalSeconds))s)" -ForegroundColor Gray

        # Now clean up any orphaned child processes
        Write-Host "Cleaning up child processes..." -ForegroundColor Yellow

        $orphaned = @()
        $orphaned += @(Get-Process dotnet -ErrorAction SilentlyContinue)
        $orphaned += @(Get-Process VBCSCompiler -ErrorAction SilentlyContinue)
        $orphaned += @(Get-Process MSBuild -ErrorAction SilentlyContinue)
        $orphaned = $orphaned | Where-Object { $_ -ne $null }

        if ($orphaned.Count -gt 0) {
            Write-Host "  Found $($orphaned.Count) orphaned processes, terminating..."
            $orphaned | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
            Write-Host "  ✓ Orphaned processes cleaned up"
        } else {
            Write-Host "  ✓ No orphaned processes"
        }

        return $exitCode

    } catch {
        Write-Host "❌ Build failed: $_" -ForegroundColor Red

        # Force cleanup on error
        Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Get-Process VBCSCompiler -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Get-Process MSBuild -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

        throw $_
    }
}

# === MAIN ===

Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Safe Build Wrapper" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

try {
    if ($Command -eq 'clean') {
        Write-Host "Cleaning build artifacts..." -ForegroundColor Yellow
        Get-ChildItem -Path . -Recurse -Directory -Name "obj", "bin" -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item -Path $_ -Recurse -Force -ErrorAction SilentlyContinue }
        Write-Host "✓ Clean complete" -ForegroundColor Green
    } else {
        $exitCode = Invoke-SafeBuild -Command $Command -Project $Project -ExtraArgs $AdditionalArgs

        if ($exitCode -eq 0) {
            Write-Host ""
            Write-Host "✓ Build succeeded!" -ForegroundColor Green
        } else {
            Write-Host ""
            Write-Host "❌ Build failed with exit code $exitCode" -ForegroundColor Red
            exit $exitCode
        }
    }

    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green

} catch {
    Write-Host "❌ Error: $_" -ForegroundColor Red
    exit 1
}
