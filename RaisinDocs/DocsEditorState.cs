namespace RaisinDocs;

public class DocsEditorState
{
    public DocsCanvas.EditorTheme Theme { get; set; } = DocsCanvas.EditorTheme.Light;
    public DocsCanvas.EditMode EditMode { get; set; } = DocsCanvas.EditMode.Source;
    public DocsCanvas.ImagePreviewMode ImagePreview { get; set; } = DocsCanvas.ImagePreviewMode.Off;
    public DocsCanvas.SoftBreakMode SoftBreak { get; set; } = DocsCanvas.SoftBreakMode.Relaxed;
    public DocsCanvas.HardBreakStyle HardBreak { get; set; } = DocsCanvas.HardBreakStyle.Backslash;
}
