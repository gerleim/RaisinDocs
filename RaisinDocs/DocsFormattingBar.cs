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

    // Bullet list: three horizontal lines with dots
    private static readonly Geometry IconBullet = Geometry.Parse(
        "M2,3 A1.5,1.5,0,1,1,1.99,3 Z M5.5,2 H14 V4 H5.5 Z " +
        "M2,7.5 A1.5,1.5,0,1,1,1.99,7.5 Z M5.5,6.5 H14 V8.5 H5.5 Z " +
        "M2,12 A1.5,1.5,0,1,1,1.99,12 Z M5.5,11 H14 V13 H5.5 Z");

    // Task list: three lines with checkboxes
    private static readonly Geometry IconTaskList = Geometry.Parse(
        "M1,2 H4 V5 H1 Z M2,3.8 L2.8,4.6 L4,2.4 " +
        "M5.5,2.5 H14 V4.5 H5.5 Z " +
        "M1,6.5 H4 V9.5 H1 Z " +
        "M5.5,7 H14 V9 H5.5 Z " +
        "M1,11 H4 V14 H1 Z M2,12.8 L2.8,13.6 L4,11.4 " +
        "M5.5,11.5 H14 V13.5 H5.5 Z");

    // Blockquote: opening quote mark
    private static readonly Geometry IconQuote = Geometry.Parse(
        "M2,9 C2,5.5 4,3 7,2 L7,3.5 C5,4.5 4.2,6 4,7.5 L6.5,7.5 V12 H2 Z " +
        "M9,9 C9,5.5 11,3 14,2 L14,3.5 C12,4.5 11.2,6 11,7.5 L13.5,7.5 V12 H9 Z");

    // Dropdown arrow: small chevron down
    private static readonly Geometry IconDropdownArrow = Geometry.Parse(
        "M3,5 L8,10 L13,5");

    // Sun icon for light theme (filled shapes only — no stroke)
    private static readonly Geometry IconSun = Geometry.Parse(
        "M8,3.5 A4,4,0,1,1,7.99,3.5 Z " +
        "M7.2,0 H8.8 V2.2 H7.2 Z M7.2,13.8 H8.8 V16 H7.2 Z " +
        "M0,7.2 V8.8 H2.2 V7.2 Z M13.8,7.2 V8.8 H16 V7.2 Z " +
        "M1.6,1.2 L2.8,1.2 L4,3.6 L2.8,4 Z " +
        "M13.2,1.2 L14.4,1.2 L13.2,4 L12,3.6 Z " +
        "M1.6,14.8 L2.8,12 L4,12.4 L2.8,14.8 Z " +
        "M12,12.4 L13.2,12 L14.4,14.8 L13.2,14.8 Z");

    // Full moon with craters (EvenOdd cuts holes) — asymmetric layout to avoid smiley face
    private static readonly Geometry IconMoon = Geometry.Parse(
        "M8,1 A6,6,0,1,1,7.99,1 Z " +
        "M6.5,4 A1.4,1.4,0,1,1,6.49,4 Z " +
        "M10.5,8 A1,1,0,1,1,10.49,8 Z " +
        "M7,10.5 A0.7,0.7,0,1,1,6.99,10.5 Z " +
        "M9,5.5 A0.6,0.6,0,1,1,8.99,5.5 Z");

    // Crescent moon (C-shape) icon for dark-blue theme — outer circle (7,7) r=6, inner circle (10,7) r=5
    private static readonly Geometry IconCrescent = Geometry.Parse(
        "M10.3,2 A6,6,0,1,0,10.3,12 A5,5,0,0,1,10.3,2 Z");

    private static readonly Brush CrescentBrush = new SolidColorBrush(Color.FromRgb(100, 149, 237));

    static DocsFormattingBar()
    {
        IconOff.Freeze();
        IconInline.Freeze();
        IconOnHover.Freeze();
        IconSource.Freeze();
        IconVisual.Freeze();
        IconBullet.Freeze();
        IconTaskList.Freeze();
        IconQuote.Freeze();
        IconDropdownArrow.Freeze();
        IconSun.Freeze();
        IconMoon.Freeze();
        IconCrescent.Freeze();
        CrescentBrush.Freeze();

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
    private ToggleButton? _taskListButton;
    private ToggleButton? _quoteButton;
    private ToggleButton? _themeButton;
    private ToggleButton? _editModeButton;
    private Path? _editModeIcon;
    private Path? _themeIcon;
    private Path? _bulletIcon;
    private Path? _taskListIcon;
    private Path? _quoteIcon;
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
        _bulletIcon = GetTemplateChild("PART_BulletIcon") as Path;
        if (_bulletIcon != null) _bulletIcon.Data = IconBullet;
        _taskListButton = WireToggle("PART_TaskList", () => Canvas?.ToggleTaskList());
        _taskListIcon = GetTemplateChild("PART_TaskListIcon") as Path;
        if (_taskListIcon != null) _taskListIcon.Data = IconTaskList;
        _quoteButton = WireToggle("PART_Quote", () => Canvas?.ToggleBlockquote());
        _quoteIcon = GetTemplateChild("PART_QuoteIcon") as Path;
        if (_quoteIcon != null) _quoteIcon.Data = IconQuote;

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
        _themeIcon = GetTemplateChild("PART_ThemeIcon") as Path;
        if (_themeIcon != null && _themeIcon.Data == null) _themeIcon.Data = IconSun;
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
        var theme = Canvas.Theme;
        SetCheckedSilent(_themeButton, theme != DocsCanvas.EditorTheme.Light);
        if (_themeIcon != null)
        {
            _themeIcon.Data = theme switch
            {
                DocsCanvas.EditorTheme.Dark => IconMoon,
                DocsCanvas.EditorTheme.DarkBlue => IconCrescent,
                _ => IconSun,
            };
            if (theme == DocsCanvas.EditorTheme.DarkBlue)
                _themeIcon.SetValue(Shape.FillProperty, CrescentBrush);
            else
                _themeIcon.ClearValue(Shape.FillProperty);
        }
        _themeButton.ToolTip = theme switch
        {
            DocsCanvas.EditorTheme.Light => "Switch to dark theme",
            DocsCanvas.EditorTheme.Dark => "Switch to dark blue theme",
            _ => "Switch to light theme",
        };
    }

    private void UpdateEditModeButton()
    {
        if (_editModeButton == null || Canvas == null) return;
        bool isVisual = Canvas.CurrentEditMode == DocsCanvas.EditMode.Visual;
        SetCheckedSilent(_editModeButton, isVisual);
        _editModeButton.ToolTip = isVisual ? "Visual mode (Ctrl+M)" : "Source mode (Ctrl+M)";
        if (_editModeIcon != null)
            _editModeIcon.Data = isVisual ? IconVisual : IconSource;
        if (_imagePreviewBorder != null)
            _imagePreviewBorder.IsEnabled = !isVisual;
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
        SetCheckedSilent(_taskListButton, kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked);
        SetCheckedSilent(_quoteButton, kind == BlockKind.Blockquote);
    }

    private static void SetCheckedSilent(ToggleButton? btn, bool value)
    {
        if (btn != null && btn.IsChecked != value)
            btn.IsChecked = value;
    }
}
