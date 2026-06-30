using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

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

    public static readonly DependencyProperty ShowMinimapProperty =
        DependencyProperty.Register(nameof(ShowMinimap), typeof(bool), typeof(DocsEditor),
            new PropertyMetadata(false));

    public event EventHandler? ContentChanged;
    public event EventHandler? IsDirtyChanged;
    public event EventHandler? ThemeChanged;
    public event EventHandler? EditModeChanged;
    public event EventHandler? FormattingChanged;

    private bool _updatingScrollBar;

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

        PART_Canvas.Minimap = PART_Minimap;
        PART_Minimap.Canvas = PART_Canvas;
        PART_Minimap.ScrollRequested += offset => PART_Canvas.SetScrollOffsetDirect(offset);
        PART_Minimap.SmoothScrollRequested += offset => PART_Canvas.SmoothScrollTo(offset);

        PART_Canvas.ScrollStateChanged += UpdateScrollBar;

        SizeChanged += (_, _) => UpdateMinimapWidth();
    }

    private void UpdateMinimapWidth()
    {
        double w = Math.Clamp(ActualWidth * 0.10, 60, 200);
        PART_Minimap.Width = w;
    }

    private void UpdateScrollBar()
    {
        _updatingScrollBar = true;
        double maxScroll = Math.Max(0, PART_Canvas.TotalContentHeight - PART_Canvas.ActualHeight);
        if (maxScroll > 0)
        {
            PART_ScrollBar.Maximum = maxScroll;
            PART_ScrollBar.ViewportSize = PART_Canvas.ActualHeight;
            PART_ScrollBar.LargeChange = Math.Max(1, PART_Canvas.ActualHeight - 20);
            PART_ScrollBar.SmallChange = 20;
            PART_ScrollBar.Value = PART_Canvas.ScrollOffset;
            PART_ScrollBar.Visibility = Visibility.Visible;
        }
        else
        {
            PART_ScrollBar.Visibility = Visibility.Collapsed;
        }
        _updatingScrollBar = false;
    }

    private void OnScrollBarValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingScrollBar) return;

        bool isThumbDrag = PART_ScrollBar.Track?.Thumb?.IsDragging == true;
        if (!isThumbDrag)
            PART_Canvas.SmoothScrollTo(e.NewValue);
        else
            PART_Canvas.SetScrollOffsetDirect(e.NewValue);
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

    public bool ShowMinimap
    {
        get => (bool)GetValue(ShowMinimapProperty);
        set => SetValue(ShowMinimapProperty, value);
    }

    public DocsCanvas Canvas => PART_Canvas;

    public string GetText() => PART_Canvas.GetText();

    public void SetText(string text) => PART_Canvas.SetText(text);

    public void MarkClean() => PART_Canvas.MarkClean();

    public DocsEditorState GetState() => new()
    {
        Theme = PART_Canvas.Theme,
        EditMode = PART_Canvas.CurrentEditMode,
        ImagePreview = PART_Canvas.CurrentImagePreview,
        SoftBreak = PART_Canvas.CurrentSoftBreak,
        HardBreak = PART_Canvas.CurrentHardBreak,
        ShowMinimap = ShowMinimap,
    };

    public void ApplyState(DocsEditorState state)
    {
        Theme = state.Theme;
        PART_Canvas.SetEditMode(state.EditMode);
        PART_Canvas.SetImagePreview(state.ImagePreview);
        PART_Canvas.SetSoftBreak(state.SoftBreak);
        PART_Canvas.SetHardBreak(state.HardBreak);
        ShowMinimap = state.ShowMinimap;
    }

    private static void OnDocumentBasePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (DocsEditor)d;
        editor.PART_Canvas.DocumentBasePath = (string?)e.NewValue;
    }
}
