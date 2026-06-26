using System.Windows;
using System.Windows.Controls;

namespace RaisinDocs;

public partial class DocsEditor : UserControl
{
    public static readonly DependencyProperty ShowToolbarProperty =
        DependencyProperty.Register(nameof(ShowToolbar), typeof(bool), typeof(DocsEditor),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(nameof(Theme), typeof(DocsCanvas.EditorTheme), typeof(DocsEditor),
            new FrameworkPropertyMetadata(DocsCanvas.EditorTheme.Light, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private static readonly DependencyPropertyKey IsDirtyPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsDirty), typeof(bool), typeof(DocsEditor),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsDirtyProperty = IsDirtyPropertyKey.DependencyProperty;

    public static readonly DependencyProperty DocumentBasePathProperty =
        DependencyProperty.Register(nameof(DocumentBasePath), typeof(string), typeof(DocsEditor),
            new PropertyMetadata(null, OnDocumentBasePathChanged));

    public event EventHandler? ContentChanged;
    public event EventHandler? IsDirtyChanged;
    public event EventHandler? ThemeChanged;
    public event EventHandler? EditModeChanged;
    public event EventHandler? FormattingChanged;

    public DocsEditor()
    {
        InitializeComponent();

        PART_Canvas.ContentChanged += (_, e) => ContentChanged?.Invoke(this, e);
        PART_Canvas.ThemeChanged += (_, e) => ThemeChanged?.Invoke(this, e);
        PART_Canvas.EditModeChanged += (_, e) => EditModeChanged?.Invoke(this, e);
        PART_Canvas.FormattingChanged += (_, e) => FormattingChanged?.Invoke(this, e);
        PART_Canvas.IsDirtyChanged += (_, _) =>
        {
            SetValue(IsDirtyPropertyKey, PART_Canvas.IsDirty);
            IsDirtyChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public bool ShowToolbar
    {
        get => (bool)GetValue(ShowToolbarProperty);
        set => SetValue(ShowToolbarProperty, value);
    }

    public DocsCanvas.EditorTheme Theme
    {
        get => (DocsCanvas.EditorTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public bool IsDirty => (bool)GetValue(IsDirtyProperty);

    public string? DocumentBasePath
    {
        get => (string?)GetValue(DocumentBasePathProperty);
        set => SetValue(DocumentBasePathProperty, value);
    }

    public DocsCanvas Canvas => PART_Canvas;

    public string GetText() => PART_Canvas.GetText();

    public void SetText(string text) => PART_Canvas.SetText(text);

    public void MarkClean() => PART_Canvas.MarkClean();

    private static void OnDocumentBasePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (DocsEditor)d;
        editor.PART_Canvas.DocumentBasePath = (string?)e.NewValue;
    }
}
