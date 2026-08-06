#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Safe build wrapper that ensures all child processes are cleaned up.

.DESCRIPTION
    Runs dotnet build/test with guaranteed cleanup of child processes.
    Prevents stray dotnet/MSBuild/VBCSCompiler processes from accumulating.
#>

param(
    [ValidateSet('build', 'test', 'clean')]
    [string]$Command = 'build',

    [string]$Project = 'RaisinDocs.slnx',

    [string[]]$AdditionalArgs = @()
)

$ErrorActionPreference = "Stop"

function Invoke-SafeBuild {
    param(
        [string]$Command,
        [string]$Project,
        [string[]]$Args
    )

    Write-Host "Starting build: dotnet $Command $Project" -ForegroundColor Cyan

    # Get current dotnet processes before build
    $beforeCount = @(Get-Process dotnet -ErrorAction SilentlyContinue).Count

    try {
        # Build argument list
        $argumentList = @($Command, $Project)
        if ($Args.Count -gt 0) {
            $argumentList += $Args
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
        $exitCode = Invoke-SafeBuild -Command $Command -Project $Project -Args $AdditionalArgs

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
