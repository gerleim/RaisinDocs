using System.Windows;

namespace RaisinDocs;

internal static class ClipboardHelper
{
    internal static bool SetText(string text, IDocsLogger? logger = null)
    {
        return RetryHelper.Execute(
            () => Clipboard.SetText(text),
            onRetry: (ex, attempt) =>
                logger?.Log(DocsLogLevel.Warning, $"Clipboard.SetText failed (attempt {attempt}): {ex.Message}"));
    }

    internal static string? GetText(IDocsLogger? logger = null)
    {
        return RetryHelper.Execute(
            () => Clipboard.GetText(),
            onRetry: (ex, attempt) =>
                logger?.Log(DocsLogLevel.Warning, $"Clipboard.GetText failed (attempt {attempt}): {ex.Message}"));
    }

    internal static string? GetHtml(IDocsLogger? logger = null)
    {
        return RetryHelper.Execute(
            () => (Clipboard.ContainsData(DataFormats.Html)
                ? Clipboard.GetData(DataFormats.Html) as string
                : null)!,
            onRetry: (ex, attempt) =>
                logger?.Log(DocsLogLevel.Warning, $"Clipboard.GetHtml failed (attempt {attempt}): {ex.Message}"));
    }
}
