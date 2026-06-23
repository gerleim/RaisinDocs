using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RaisinDocs;

public class DocsFormattingBar : Control
{
    // Image frame with diagonal slash
    private static readonly Geometry IconOff = Geometry.Parse(
        "M1,1 H15 V13 H1 Z M2,2 H14 V12 H2 Z M1,13 L15,1");

    // Image frame with mountain landscape and sun
    private static readonly Geometry IconInline = Geometry.Parse(
        "M1,1 H15 V13 H1 Z M2,2 H14 V12 H2 Z " +
        "M11.5,4 A1.5,1.5,0,1,1,11.49,4 " +
        "M2,12 L5.5,6.5 L7.5,9 L10,5.5 L14,12 Z");

    // Image frame with eye symbol
    private static readonly Geometry IconOnHover = Geometry.Parse(
        "M1,1 H15 V13 H1 Z M2,2 H14 V12 H2 Z " +
        "M3,7 C5,3.5 11,3.5 13,7 C11,10.5 5,10.5 3,7 Z " +
        "M8,5.5 A1.5,1.5,0,1,1,7.99,5.5");

    // Source mode: code brackets </>
    private static readonly Geometry IconSource = Geometry.Parse(
        "M5,2 L1,7 L5,12 M11,2 L15,7 L11,12 M9,1 L7,13");

    // Visual mode: eye
    private static readonly Geometry IconVisual = Geometry.Parse(
        "M1,7 C3.5,2 12.5,2 15,7 C12.5,12 3.5,12 1,7 Z " +
        "M8,5 A2,2,0,1,1,7.99,5");

    static DocsFormattingBar()
    {
        IconOff.Freeze();
        IconInline.Freeze();
        IconOnHover.Freeze();
        IconSource.Freeze();
        IconVisual.Freeze();

        DefaultStyleKeyProperty.OverrideMetadata(typeof(DocsFormattingBar),
            new FrameworkPropertyMetadata(typeof(DocsFormattingBar)));
    }

    public static readonly DependencyProperty CanvasProperty =
        DependencyProperty.Register(nameof(Canvas), typeof(DocsCanvas), typeof(DocsFormattingBar),
            new PropertyMetadata(null, OnCanvasChanged));

    public DocsCanvas? Canvas
    {
        get => (DocsCanvas?)GetValue(CanvasProperty);
        set => SetValue(CanvasProperty, value);
    }

    private ToggleButton? _boldButton;
    private ToggleButton? _italicButton;
    private ToggleButton? _strikethroughButton;
    private ToggleButton? _codeButton;
    private ToggleButton? _codeBlockButton;
    private ToggleButton? _h1Button;
    private ToggleButton? _h2Button;
    private ToggleButton? _h3Button;
    private ToggleButton? _bulletButton;
    private ToggleButton? _quoteButton;
    private ToggleButton? _themeButton;
    private ToggleButton? _editModeButton;
    private Path? _editModeIcon;
    private Button? _imagePreviewButton;
    private Button? _imagePreviewArrow;
    private Border? _imagePreviewBorder;
    private Path? _imagePreviewIcon;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _boldButton = WireToggle("PART_Bold", () => Canvas?.ToggleBold());
        _italicButton = WireToggle("PART_Italic", () => Canvas?.ToggleItalic());
        _strikethroughButton = WireToggle("PART_Strikethrough", () => Canvas?.ToggleStrikethrough());
        _codeButton = WireToggle("PART_Code", () => Canvas?.ToggleCodeSpan());
        _codeBlockButton = WireToggle("PART_CodeBlock", () => Canvas?.ToggleFencedCode());
        _h1Button = WireToggle("PART_H1", () => Canvas?.ToggleHeading(1));
        _h2Button = WireToggle("PART_H2", () => Canvas?.ToggleHeading(2));
        _h3Button = WireToggle("PART_H3", () => Canvas?.ToggleHeading(3));
        _bulletButton = WireToggle("PART_Bullet", () => Canvas?.ToggleBulletList());
        _quoteButton = WireToggle("PART_Quote", () => Canvas?.ToggleBlockquote());

        var insertTableButton = GetTemplateChild("PART_InsertTable") as Button;
        if (insertTableButton != null)
        {
            insertTableButton.Click += (_, _) =>
            {
                Canvas?.InsertTable(3, 2);
                Canvas?.Focus();
            };
        }

        _editModeButton = GetTemplateChild("PART_EditMode") as ToggleButton;
        _editModeIcon = GetTemplateChild("PART_EditModeIcon") as Path;
        if (_editModeButton != null)
        {
            _editModeButton.Click += (_, _) =>
            {
                Canvas?.ToggleEditMode();
                Canvas?.Focus();
                UpdateEditModeButton();
            };
        }
        UpdateEditModeButton();

        _imagePreviewBorder = GetTemplateChild("PART_ImagePreviewBorder") as Border;
        _imagePreviewButton = GetTemplateChild("PART_ImagePreview") as Button;
        _imagePreviewArrow = GetTemplateChild("PART_ImagePreviewArrow") as Button;
        _imagePreviewIcon = GetTemplateChild("PART_ImagePreviewIcon") as Path;
        if (_imagePreviewButton != null)
        {
            _imagePreviewButton.Click += (_, _) =>
            {
                Canvas?.CycleImagePreview();
                Canvas?.Focus();
                UpdateImagePreviewButton();
            };
        }
        if (_imagePreviewArrow != null)
        {
            _imagePreviewArrow.Click += (_, _) => ShowImagePreviewMenu();
        }
        UpdateImagePreviewButton();

        _themeButton = GetTemplateChild("PART_Theme") as ToggleButton;
        if (_themeButton != null)
        {
            _themeButton.Click += (_, _) =>
            {
                Canvas?.ToggleTheme();
                Canvas?.Focus();
                UpdateThemeButton();
            };
        }
        UpdateThemeButton();
    }

    private ToggleButton? WireToggle(string partName, Action action)
    {
        var btn = GetTemplateChild(partName) as ToggleButton;
        if (btn != null)
        {
            btn.Click += (_, _) =>
            {
                action();
                Canvas?.Focus();
                UpdateButtonStates();
            };
        }
        return btn;
    }

    private static void OnCanvasChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (DocsFormattingBar)d;
        if (e.OldValue is DocsCanvas oldCanvas)
        {
            oldCanvas.FormattingChanged -= bar.OnFormattingChanged;
            oldCanvas.ThemeChanged -= bar.OnThemeChanged;
            oldCanvas.EditModeChanged -= bar.OnEditModeChanged;
        }
        if (e.NewValue is DocsCanvas newCanvas)
        {
            newCanvas.FormattingChanged += bar.OnFormattingChanged;
            newCanvas.ThemeChanged += bar.OnThemeChanged;
            newCanvas.EditModeChanged += bar.OnEditModeChanged;
        }
        bar.UpdateButtonStates();
        bar.UpdateThemeButton();
        bar.UpdateEditModeButton();
    }

    private void OnFormattingChanged(object? sender, EventArgs e) => UpdateButtonStates();
    private void OnThemeChanged(object? sender, EventArgs e) => UpdateThemeButton();
    private void OnEditModeChanged(object? sender, EventArgs e) => UpdateEditModeButton();

    private void UpdateThemeButton()
    {
        if (_themeButton == null || Canvas == null) return;
        bool isDark = Canvas.Theme == DocsCanvas.EditorTheme.Dark;
        SetCheckedSilent(_themeButton, isDark);
        if (_themeButton.Content is System.Windows.Controls.TextBlock tb)
            tb.Text = isDark ? "☾" : "☀";
        _themeButton.ToolTip = isDark ? "Switch to light theme" : "Switch to dark theme";
    }

    private void UpdateEditModeButton()
    {
        if (_editModeButton == null || Canvas == null) return;
        bool isVisual = Canvas.CurrentEditMode == DocsCanvas.EditMode.Visual;
        SetCheckedSilent(_editModeButton, isVisual);
        _editModeButton.ToolTip = isVisual ? "Visual mode (Ctrl+M)" : "Source mode (Ctrl+M)";
        if (_editModeIcon != null)
            _editModeIcon.Data = isVisual ? IconVisual : IconSource;
    }

    private void UpdateImagePreviewButton()
    {
        if (_imagePreviewBorder == null || Canvas == null) return;
        var mode = Canvas.CurrentImagePreview;
        _imagePreviewBorder.ToolTip = mode switch
        {
            DocsCanvas.ImagePreviewMode.Inline => "Image Preview: Inline",
            DocsCanvas.ImagePreviewMode.OnHover => "Image Preview: On Hover",
            _ => "Image Preview: Off",
        };
        if (_imagePreviewIcon != null)
        {
            _imagePreviewIcon.Data = mode switch
            {
                DocsCanvas.ImagePreviewMode.Inline => IconInline,
                DocsCanvas.ImagePreviewMode.OnHover => IconOnHover,
                _ => IconOff,
            };
        }
    }

    private void ShowImagePreviewMenu()
    {
        if (Canvas == null || _imagePreviewBorder == null) return;
        var current = Canvas.CurrentImagePreview;
        var menu = new ContextMenu();

        foreach (var mode in new[] {
            DocsCanvas.ImagePreviewMode.Off,
            DocsCanvas.ImagePreviewMode.Inline,
            DocsCanvas.ImagePreviewMode.OnHover })
        {
            string label = mode switch
            {
                DocsCanvas.ImagePreviewMode.Inline => "Inline",
                DocsCanvas.ImagePreviewMode.OnHover => "On Hover",
                _ => "Off",
            };
            var item = new MenuItem { Header = label, IsChecked = mode == current };
            var capturedMode = mode;
            item.Click += (_, _) =>
            {
                Canvas?.SetImagePreview(capturedMode);
                Canvas?.Focus();
                UpdateImagePreviewButton();
            };
            menu.Items.Add(item);
        }

        menu.PlacementTarget = _imagePreviewBorder;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void UpdateButtonStates()
    {
        var canvas = Canvas;
        if (canvas == null) return;

        SetCheckedSilent(_boldButton, canvas.SelectionIsBold);
        SetCheckedSilent(_italicButton, canvas.SelectionIsItalic);
        SetCheckedSilent(_strikethroughButton, canvas.SelectionIsStrikethrough);
        SetCheckedSilent(_codeButton, canvas.SelectionIsCode);
        SetCheckedSilent(_codeBlockButton, canvas.CurrentBlockKind == BlockKind.FencedCodeLine);

        var kind = canvas.CurrentBlockKind;
        SetCheckedSilent(_h1Button, kind == BlockKind.Heading1);
        SetCheckedSilent(_h2Button, kind == BlockKind.Heading2);
        SetCheckedSilent(_h3Button, kind == BlockKind.Heading3);
        SetCheckedSilent(_bulletButton, kind == BlockKind.UnorderedListItem);
        SetCheckedSilent(_quoteButton, kind == BlockKind.Blockquote);
    }

    private static void SetCheckedSilent(ToggleButton? btn, bool value)
    {
        if (btn != null && btn.IsChecked != value)
            btn.IsChecked = value;
    }
}
