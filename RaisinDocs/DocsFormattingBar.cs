using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
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

    // Bullet list: three horizontal lines with dots (arc starts at top of circle, so y = line_center - radius)
    private static readonly Geometry IconBullet = Geometry.Parse(
        "M3,2 A1,1,0,1,1,2.99,2 Z M5,2 H14 V4 H5 Z " +
        "M3,6.5 A1,1,0,1,1,2.99,6.5 Z M5,6.5 H14 V8.5 H5 Z " +
        "M3,11 A1,1,0,1,1,2.99,11 Z M5,11 H14 V13 H5 Z");

    // Ordered list: lines match bullet icon, digits centered on each row
    private static readonly Geometry IconOrderedList = Geometry.Parse(
        "M1.5,2 L3,1 V4.5 H2 V2.5 Z M1,4.5 H4 V5 H1 Z " +
        "M5,2 H14 V4 H5 Z " +
        "M1,5.5 H4 V6.3 L2,8.7 H4 V9.5 H1 V8.7 L3,6.3 H1 Z " +
        "M5,6.5 H14 V8.5 H5 Z " +
        "M1,10 H4 V10.8 H1 Z M3,10.8 H4 V11.6 H3 Z M1,11.6 H4 V12.4 H1 Z M3,12.4 H4 V13.2 H3 Z M1,13.2 H4 V14 H1 Z " +
        "M5,11 H14 V13 H5 Z");

    // Task list: lines match bullet icon, 4x4 checkboxes centered on each row
    private static readonly Geometry IconTaskList = Geometry.Parse(
        "M0.5,1 H4.5 V5 H0.5 Z M1,3 L2.2,4.3 L4,1.5 L3.3,2 L2.2,3.3 L1.8,2.5 Z " +
        "M5,2 H14 V4 H5 Z " +
        "M0.5,5.5 H4.5 V9.5 H0.5 Z M1.5,6.5 H3.5 V8.5 H1.5 Z " +
        "M5,6.5 H14 V8.5 H5 Z " +
        "M0.5,10 H4.5 V14 H0.5 Z M1,12 L2.2,13.3 L4,10.5 L3.3,11 L2.2,12.3 L1.8,11.5 Z " +
        "M5,11 H14 V13 H5 Z");

    // Link: chain link icon
    private static readonly Geometry IconLink = Geometry.Parse(
        "M6.5,8.5 A3,3,0,0,1,6.5,3.5 L8.5,1.5 A3,3,0,0,1,13.5,6.5 L12,8 " +
        "M9.5,7.5 A3,3,0,0,1,9.5,12.5 L7.5,14.5 A3,3,0,0,1,2.5,9.5 L4,8");

    // Blockquote: opening quote mark
    private static readonly Geometry IconQuote = Geometry.Parse(
        "M2,9 C2,5.5 4,3 7,2 L7,3.5 C5,4.5 4.2,6 4,7.5 L6.5,7.5 V12 H2 Z " +
        "M9,9 C9,5.5 11,3 14,2 L14,3.5 C12,4.5 11.2,6 11,7.5 L13.5,7.5 V12 H9 Z");

    // Minimap off: rectangle frame only
    private static readonly Geometry IconMinimap = Geometry.Parse(
        "M1,1 H15 V15 H1 Z M2,2 H14 V14 H2 Z");

    // Minimap off: document text lines (rendered at reduced opacity)
    private static readonly Geometry IconMinimapLines = Geometry.Parse(
        "M3,3.5 H13 V4.5 H3 Z M3,6 H11 V7 H3 Z M3,8.5 H12 V9.5 H3 Z M3,11 H9 V12 H3 Z");

    // Minimap on: frame with filled right column
    private static readonly Geometry IconMinimapOn = Geometry.Parse(
        "M1,1 H15 V15 H1 Z M2,2 H10.5 V14 H2 Z");

    // Minimap on: shorter text lines (rendered at reduced opacity)
    private static readonly Geometry IconMinimapOnLines = Geometry.Parse(
        "M3,3.5 H9.5 V4.5 H3 Z M3,6 H8 V7 H3 Z M3,8.5 H9 V9.5 H3 Z M3,11 H7 V12 H3 Z");

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

    private static readonly Geometry IconTable = Geometry.Parse(
        "M1,1 H15 V13 H1 Z M1,7 H15 M8,1 V13");

    private static readonly Geometry IconReflow = Geometry.Parse(
        "M1,3 H7 M1,7 H11 M1,11 H5 M10,4 L13,7 L10,10");

    private static readonly Geometry IconHardBreaks = Geometry.Parse(
        "M1,4 H10 M1,10 H10 M12,1 V7 M10.5,2.5 L12,1 L13.5,2.5");

    private static readonly Geometry IconToc = Geometry.Parse(
        "M1,2 H5 M7,2 H15 M1,5.5 H5 M7,5.5 H13 M1,9 H5 M7,9 H14 M1,12.5 H5 M7,12.5 H11");

    static DocsFormattingBar()
    {
        IconOff.Freeze();
        IconInline.Freeze();
        IconOnHover.Freeze();
        IconSource.Freeze();
        IconVisual.Freeze();
        IconBullet.Freeze();
        IconOrderedList.Freeze();
        IconTaskList.Freeze();
        IconLink.Freeze();
        IconQuote.Freeze();
        IconDropdownArrow.Freeze();
        IconMinimap.Freeze();
        IconMinimapLines.Freeze();
        IconMinimapOn.Freeze();
        IconMinimapOnLines.Freeze();
        IconSun.Freeze();
        IconMoon.Freeze();
        IconCrescent.Freeze();
        CrescentBrush.Freeze();
        IconTable.Freeze();
        IconReflow.Freeze();
        IconHardBreaks.Freeze();
        IconToc.Freeze();

        DefaultStyleKeyProperty.OverrideMetadata(typeof(DocsFormattingBar),
            new FrameworkPropertyMetadata(typeof(DocsFormattingBar)));
    }

    public static readonly DependencyProperty CanvasProperty =
        DependencyProperty.Register(nameof(Canvas), typeof(DocsCanvas), typeof(DocsFormattingBar),
            new PropertyMetadata(null, OnCanvasChanged));

    public static readonly DependencyProperty IsCollapsedProperty =
        DependencyProperty.Register(nameof(IsCollapsed), typeof(bool), typeof(DocsFormattingBar),
            new FrameworkPropertyMetadata(false));

    public bool IsCollapsed
    {
        get => (bool)GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

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
    private ToggleButton? _orderedListButton;
    private ToggleButton? _taskListButton;
    private ToggleButton? _quoteButton;
    private ToggleButton? _themeButton;
    private ToggleButton? _editModeButton;
    private Path? _editModeIcon;
    private Path? _themeIcon;
    private Path? _bulletIcon;
    private Path? _orderedListIcon;
    private Path? _taskListIcon;
    private Path? _quoteIcon;
    private Path? _linkIcon;
    private Button? _imagePreviewButton;
    private Button? _imagePreviewArrow;
    private Border? _imagePreviewBorder;
    private Path? _imagePreviewIcon;
    private ToggleButton? _tocButton;
    private Path? _tocIcon;
    private ToggleButton? _minimapButton;
    private Path? _minimapIcon;
    private Path? _minimapLines;
    private Button? _linkButton;
    private Button? _insertTableButton;
    private Button? _colorTextButton;
    private Button? _reflowButton;
    private ReformatActions _lastReformatActions;
    private TextBlock? _reflowTooltipText;
    private Button? _hardBreaksButton;
    private Border? _colorBar;
    private string _lastColorName = "red";
    private OverflowPanel? _overflowPanel;
    private Button? _moreButton;
    private Button? _collapseButton;
    private Dictionary<UIElement, OverflowEntry>? _overflowMap;
    private List<(ButtonBase Button, UIElement OverflowChild, Action Action)>? _navigableButtons;
    private int _keyboardFocusIndex = -1;
    private bool _isKeyboardActive;

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
        _orderedListButton = WireToggle("PART_OrderedList", () => Canvas?.ToggleOrderedList());
        _orderedListIcon = GetTemplateChild("PART_OrderedListIcon") as Path;
        if (_orderedListIcon != null) _orderedListIcon.Data = IconOrderedList;
        _taskListButton = WireToggle("PART_TaskList", () => Canvas?.ToggleTaskList());
        _taskListIcon = GetTemplateChild("PART_TaskListIcon") as Path;
        if (_taskListIcon != null) _taskListIcon.Data = IconTaskList;
        _quoteButton = WireToggle("PART_Quote", () => Canvas?.ToggleBlockquote());
        _quoteIcon = GetTemplateChild("PART_QuoteIcon") as Path;
        if (_quoteIcon != null) _quoteIcon.Data = IconQuote;

        _linkButton = GetTemplateChild("PART_Link") as Button;
        _linkIcon = GetTemplateChild("PART_LinkIcon") as Path;
        if (_linkIcon != null) _linkIcon.Data = IconLink;
        if (_linkButton != null)
        {
            _linkButton.Click += (_, _) =>
            {
                Canvas?.InsertLink();
                Canvas?.Focus();
            };
        }

        _insertTableButton = GetTemplateChild("PART_InsertTable") as Button;
        if (_insertTableButton != null)
        {
            _insertTableButton.Click += (_, _) =>
            {
                Canvas?.InsertTable(3, 2);
                Canvas?.Focus();
            };
        }

        _colorTextButton = GetTemplateChild("PART_ColorText") as Button;
        _colorBar = GetTemplateChild("PART_ColorBar") as Border;
        if (_colorTextButton != null)
        {
            _colorTextButton.Click += (_, _) => ShowColorMenu();
        }

        _reflowButton = GetTemplateChild("PART_Reflow") as Button;
        if (_reflowButton != null)
        {
            _reflowButton.Click += (_, _) =>
            {
                Canvas?.Reflow();
                Canvas?.Focus();
            };
            _reflowTooltipText = new TextBlock();
            _reflowButton.ToolTip = _reflowTooltipText;
            _reflowButton.ToolTipOpening += (_, _) =>
            {
                UpdateReflowTooltipText();
                var window = Window.GetWindow(this);
                if (window != null)
                {
                    window.PreviewKeyDown += OnReflowTooltipKeyChange;
                    window.PreviewKeyUp += OnReflowTooltipKeyChange;
                }
            };
            _reflowButton.ToolTipClosing += (_, _) =>
            {
                var window = Window.GetWindow(this);
                if (window != null)
                {
                    window.PreviewKeyDown -= OnReflowTooltipKeyChange;
                    window.PreviewKeyUp -= OnReflowTooltipKeyChange;
                }
            };
        }

        _hardBreaksButton = GetTemplateChild("PART_HardBreaks") as Button;
        if (_hardBreaksButton != null)
        {
            _hardBreaksButton.Click += (_, _) =>
            {
                Canvas?.ConvertToHardBreaks();
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

        _tocButton = GetTemplateChild("PART_Toc") as ToggleButton;
        _tocIcon = GetTemplateChild("PART_TocIcon") as Path;
        if (_tocIcon != null) _tocIcon.Data = IconToc;
        if (_tocButton != null)
        {
            _tocButton.Click += (_, _) =>
            {
                Canvas?.ToggleToc();
                Canvas?.Focus();
                UpdateTocButton();
            };
        }
        UpdateTocButton();

        _minimapButton = GetTemplateChild("PART_Minimap") as ToggleButton;
        _minimapIcon = GetTemplateChild("PART_MinimapIcon") as Path;
        _minimapLines = GetTemplateChild("PART_MinimapLines") as Path;
        if (_minimapIcon != null) _minimapIcon.Data = IconMinimap;
        if (_minimapLines != null) _minimapLines.Data = IconMinimapLines;
        if (_minimapButton != null)
        {
            _minimapButton.Click += (_, _) =>
            {
                Canvas?.ToggleMinimap();
                Canvas?.Focus();
                UpdateMinimapButton();
            };
        }
        UpdateMinimapButton();
        UpdateButtonStates();

        _overflowPanel = GetTemplateChild("PART_OverflowPanel") as OverflowPanel;
        _moreButton = GetTemplateChild("PART_Overflow") as Button;
        if (_moreButton != null)
            _moreButton.Click += (_, _) => ShowOverflowMenu();

        _collapseButton = GetTemplateChild("PART_Collapse") as Button;
        if (_collapseButton != null)
            _collapseButton.Click += (_, _) => IsCollapsed = true;

        var collapsedStrip = GetTemplateChild("PART_CollapsedStrip") as Button;
        if (collapsedStrip != null)
            collapsedStrip.Click += (_, _) => IsCollapsed = false;

        BuildOverflowMap();
        BuildNavigableButtons();
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
            oldCanvas.FormattingBar = null;
        }
        if (e.NewValue is DocsCanvas newCanvas)
        {
            newCanvas.FormattingChanged += bar.OnFormattingChanged;
            newCanvas.ThemeChanged += bar.OnThemeChanged;
            newCanvas.EditModeChanged += bar.OnEditModeChanged;
            newCanvas.FormattingBar = bar;
        }
        bar.UpdateButtonStates();
        bar.UpdateThemeButton();
        bar.UpdateEditModeButton();
        bar.UpdateImagePreviewButton();
        bar.UpdateTocButton();
        bar.UpdateMinimapButton();
    }

    private void OnFormattingChanged(object? sender, EventArgs e) => UpdateButtonStates();
    private void OnThemeChanged(object? sender, EventArgs e) => UpdateThemeButton();
    private void OnEditModeChanged(object? sender, EventArgs e) => UpdateEditModeButton();

    private void UpdateThemeButton()
    {
        if (_themeButton == null || Canvas == null) return;
        var theme = Canvas.Theme;
        SetCheckedSilent(_themeButton, false);
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

    internal void UpdateTocButton()
    {
        if (_tocButton == null || Canvas == null) return;
        SetCheckedSilent(_tocButton, Canvas.IsTocVisible);
    }

    internal void UpdateMinimapButton()
    {
        if (_minimapButton == null || Canvas == null) return;
        var visible = Canvas.IsMinimapVisible;
        SetCheckedSilent(_minimapButton, visible);
        if (_minimapIcon != null)
            _minimapIcon.Data = visible ? IconMinimapOn : IconMinimap;
        if (_minimapLines != null)
            _minimapLines.Data = visible ? IconMinimapOnLines : IconMinimapLines;
    }

    private void UpdateButtonStates()
    {
        var canvas = Canvas;
        if (canvas == null) return;

        var kind = canvas.CurrentBlockKind;
        bool inCodeBlock = kind is BlockKind.FencedCodeLine or BlockKind.IndentedCodeLine;

        SetCheckedSilent(_boldButton, canvas.SelectionIsBold);
        SetCheckedSilent(_italicButton, canvas.SelectionIsItalic);
        SetCheckedSilent(_strikethroughButton, canvas.SelectionIsStrikethrough);
        SetCheckedSilent(_codeButton, canvas.SelectionIsCode);
        SetCheckedSilent(_codeBlockButton, inCodeBlock);

        SetCheckedSilent(_h1Button, kind == BlockKind.Heading1);
        SetCheckedSilent(_h2Button, kind == BlockKind.Heading2);
        SetCheckedSilent(_h3Button, kind == BlockKind.Heading3);
        SetCheckedSilent(_bulletButton, kind == BlockKind.UnorderedListItem);
        SetCheckedSilent(_orderedListButton, kind == BlockKind.OrderedListItem);
        SetCheckedSilent(_taskListButton, kind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked);
        SetCheckedSilent(_quoteButton, kind == BlockKind.Blockquote);

        if (_boldButton != null) _boldButton.IsEnabled = !inCodeBlock;
        if (_italicButton != null) _italicButton.IsEnabled = !inCodeBlock;
        if (_strikethroughButton != null) _strikethroughButton.IsEnabled = !inCodeBlock;
        if (_codeButton != null) _codeButton.IsEnabled = !inCodeBlock;
        if (_h1Button != null) _h1Button.IsEnabled = !inCodeBlock;
        if (_h2Button != null) _h2Button.IsEnabled = !inCodeBlock;
        if (_h3Button != null) _h3Button.IsEnabled = !inCodeBlock;
        if (_bulletButton != null) _bulletButton.IsEnabled = !inCodeBlock;
        if (_orderedListButton != null) _orderedListButton.IsEnabled = !inCodeBlock;
        if (_taskListButton != null) _taskListButton.IsEnabled = !inCodeBlock;
        if (_quoteButton != null) _quoteButton.IsEnabled = !inCodeBlock;
        if (_linkButton != null) _linkButton.IsEnabled = !inCodeBlock;
        if (_insertTableButton != null) _insertTableButton.IsEnabled = !inCodeBlock;
        if (_colorTextButton != null) _colorTextButton.IsEnabled = !inCodeBlock;

        if (_reflowButton != null)
        {
            _lastReformatActions = canvas.GetReformatActions();
            _reflowButton.IsEnabled = _lastReformatActions != ReformatActions.None;
            UpdateReflowTooltipText();
        }
        if (_hardBreaksButton != null)
            _hardBreaksButton.IsEnabled = canvas.CanConvertToHardBreaks;
    }

    private void OnReflowTooltipKeyChange(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift)
            UpdateReflowTooltipText();
    }

    private void UpdateReflowTooltipText()
    {
        if (_reflowTooltipText == null) return;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        BuildReformatTooltip(_reflowTooltipText, _lastReformatActions, shift);
    }

    private static readonly (ReformatActions Flag, string Label)[] _reformatLabels =
    [
        (ReformatActions.ConvertBoxTable, "Convert box-drawing table"),
        (ReformatActions.MergeParagraphs, "Merge paragraphs"),
        (ReformatActions.CollapseBlankLines, "Collapse blank lines"),
        (ReformatActions.TrimWhitespace, "Trim whitespace"),
        (ReformatActions.RenumberOrderedList, "Renumber ordered list"),
    ];

    private static void BuildReformatTooltip(TextBlock tb, ReformatActions actions, bool shift)
    {
        tb.Inlines.Clear();
        tb.Inlines.Add(new Run("Reformat selection") { FontWeight = FontWeights.SemiBold });

        if (shift)
        {
            foreach (var (_, label) in _reformatLabels)
                tb.Inlines.Add(new Run("\n• " + label));
        }
        else
        {
            if (actions != ReformatActions.None)
            {
                foreach (var (flag, label) in _reformatLabels)
                {
                    if (actions.HasFlag(flag))
                        tb.Inlines.Add(new Run("\n• " + label));
                }
            }
            tb.Inlines.Add(new Run("\n\nHold Shift to see all operations") { FontStyle = FontStyles.Italic });
        }
    }

    private static void SetCheckedSilent(ToggleButton? btn, bool value)
    {
        if (btn != null && btn.IsChecked != value)
            btn.IsChecked = value;
    }

    private static readonly (string Name, Color Color)[] ColorPalette =
    [
        ("red", Color.FromRgb(255, 0, 0)),
        ("blue", Color.FromRgb(0, 0, 255)),
        ("green", Color.FromRgb(0, 128, 0)),
        ("orange", Color.FromRgb(255, 165, 0)),
        ("purple", Color.FromRgb(128, 0, 128)),
        ("crimson", Color.FromRgb(220, 20, 60)),
        ("dodgerblue", Color.FromRgb(30, 144, 255)),
        ("goldenrod", Color.FromRgb(218, 165, 32)),
        ("teal", Color.FromRgb(0, 128, 128)),
        ("coral", Color.FromRgb(255, 127, 80)),
        ("darkviolet", Color.FromRgb(148, 0, 211)),
        ("forestgreen", Color.FromRgb(34, 139, 34)),
    ];

    private void ShowColorMenu(FrameworkElement? target = null)
    {
        if (Canvas == null) return;
        target ??= _colorTextButton;
        if (target == null) return;

        var menu = new ContextMenu();

        foreach (var (name, color) in ColorPalette)
        {
            var swatch = new Border
            {
                Width = 14, Height = 14,
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 6, 0),
            };
            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(swatch);
            panel.Children.Add(label);

            var item = new MenuItem { Header = panel };
            var capturedName = name;
            var capturedColor = color;
            item.Click += (_, _) =>
            {
                _lastColorName = capturedName;
                if (_colorBar != null)
                    _colorBar.Background = new SolidColorBrush(capturedColor);
                Canvas?.InsertFgColor(capturedName);
                Canvas?.Focus();
            };
            menu.Items.Add(item);
        }

        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ShowOverflowMenu()
    {
        if (_overflowPanel == null || !_overflowPanel.HasOverflow || _overflowMap == null) return;

        var menu = new ContextMenu();

        foreach (UIElement child in _overflowPanel.Children)
        {
            if (!_overflowPanel.IsOverflowed(child) || OverflowPanel.GetIsOverflowButton(child))
                continue;

            if (child.DesiredSize.Width < 15)
            {
                if (menu.Items.Count > 0 && menu.Items[menu.Items.Count - 1] is not Separator)
                    menu.Items.Add(new Separator());
                continue;
            }

            if (!_overflowMap.TryGetValue(child, out var entry)) continue;

            string label = (child as FrameworkElement)?.ToolTip as string ?? entry.Label;
            var item = new MenuItem
            {
                Header = label,
                IsChecked = entry.IsChecked?.Invoke() ?? false,
                IsEnabled = child.IsEnabled,
                Icon = entry.Icon?.Invoke(),
            };
            item.Click += (_, _) => entry.Handler();
            menu.Items.Add(item);
        }

        while (menu.Items.Count > 0 && menu.Items[menu.Items.Count - 1] is Separator)
            menu.Items.RemoveAt(menu.Items.Count - 1);

        if (menu.Items.Count == 0) return;

        menu.PlacementTarget = _moreButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void BuildOverflowMap()
    {
        _overflowMap = new();
        var mono = new FontFamily("Cascadia Mono,Consolas");

        void Map(UIElement? el, string label, Action handler, Func<bool>? isChecked = null, Func<object>? icon = null)
        {
            if (el != null) _overflowMap[el] = new OverflowEntry(label, handler, isChecked, icon);
        }

        Map(_boldButton, "Bold (Ctrl+B)",
            () => { Canvas?.ToggleBold(); Canvas?.Focus(); },
            () => Canvas?.SelectionIsBold ?? false,
            () => new TextBlock { Text = "B", FontWeight = FontWeights.Bold });
        Map(_italicButton, "Italic (Ctrl+I)",
            () => { Canvas?.ToggleItalic(); Canvas?.Focus(); },
            () => Canvas?.SelectionIsItalic ?? false,
            () => new TextBlock { Text = "I", FontStyle = FontStyles.Italic });
        Map(_strikethroughButton, "Strikethrough",
            () => { Canvas?.ToggleStrikethrough(); Canvas?.Focus(); },
            () => Canvas?.SelectionIsStrikethrough ?? false,
            () => new TextBlock { Text = "S", TextDecorations = TextDecorations.Strikethrough });
        Map(_codeButton, "Code",
            () => { Canvas?.ToggleCodeSpan(); Canvas?.Focus(); },
            () => Canvas?.SelectionIsCode ?? false,
            () => new TextBlock { Text = "</>", FontSize = 11, FontFamily = mono });
        Map(_codeBlockButton, "Code block",
            () => { Canvas?.ToggleFencedCode(); Canvas?.Focus(); },
            () => Canvas?.CurrentBlockKind == BlockKind.FencedCodeLine,
            () => new TextBlock { Text = "```", FontSize = 11, FontFamily = mono });
        Map(_h1Button, "Heading 1",
            () => { Canvas?.ToggleHeading(1); Canvas?.Focus(); },
            () => Canvas?.CurrentBlockKind == BlockKind.Heading1,
            () => new TextBlock { Text = "H1", FontWeight = FontWeights.Bold, FontSize = 13 });
        Map(_h2Button, "Heading 2",
            () => { Canvas?.ToggleHeading(2); Canvas?.Focus(); },
            () => Canvas?.CurrentBlockKind == BlockKind.Heading2,
            () => new TextBlock { Text = "H2", FontWeight = FontWeights.Bold, FontSize = 12 });
        Map(_h3Button, "Heading 3",
            () => { Canvas?.ToggleHeading(3); Canvas?.Focus(); },
            () => Canvas?.CurrentBlockKind == BlockKind.Heading3,
            () => new TextBlock { Text = "H3", FontWeight = FontWeights.Bold, FontSize = 11 });
        Map(_bulletButton, "Bullet list",
            () => { Canvas?.ToggleBulletList(); Canvas?.Focus(); },
            () => Canvas?.CurrentBlockKind == BlockKind.UnorderedListItem,
            () => MakePathIcon(IconBullet));
        Map(_orderedListButton, "Ordered list",
            () => { Canvas?.ToggleOrderedList(); Canvas?.Focus(); },
            () => Canvas?.CurrentBlockKind == BlockKind.OrderedListItem,
            () => MakePathIcon(IconOrderedList));
        Map(_taskListButton, "Task list",
            () => { Canvas?.ToggleTaskList(); Canvas?.Focus(); },
            () => Canvas?.CurrentBlockKind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked,
            () => MakePathIcon(IconTaskList));
        Map(_quoteButton, "Blockquote",
            () => { Canvas?.ToggleBlockquote(); Canvas?.Focus(); },
            () => Canvas?.CurrentBlockKind == BlockKind.Blockquote,
            () => MakePathIcon(IconQuote));
        Map(_linkButton, "Link (Ctrl+K)",
            () => { Canvas?.InsertLink(); Canvas?.Focus(); },
            icon: () => MakePathIcon(IconLink, true));
        Map(_insertTableButton, "Insert table",
            () => { Canvas?.InsertTable(3, 2); Canvas?.Focus(); },
            icon: () => MakePathIcon(IconTable, true));
        Map(_colorTextButton, "Text color",
            () => ShowColorMenu(_moreButton),
            icon: () =>
            {
                var grid = new Grid { Width = 16, Height = 16 };
                grid.Children.Add(new TextBlock
                {
                    Text = "A", FontWeight = FontWeights.Bold, FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -1, 0, 0),
                });
                grid.Children.Add(new Border
                {
                    Height = 3, VerticalAlignment = VerticalAlignment.Bottom,
                    Background = _colorBar?.Background ?? Brushes.Red, CornerRadius = new CornerRadius(1),
                });
                return grid;
            });
        Map(_reflowButton, "Reformat selection",
            () => { Canvas?.Reflow(); Canvas?.Focus(); },
            icon: () => MakePathIcon(IconReflow, true));
        Map(_hardBreaksButton, "Convert to hard breaks",
            () => { Canvas?.ConvertToHardBreaks(); Canvas?.Focus(); },
            icon: () => MakePathIcon(IconHardBreaks, true));
        Map(_editModeButton, "Edit mode (Ctrl+M)",
            () => { Canvas?.ToggleEditMode(); Canvas?.Focus(); UpdateEditModeButton(); },
            () => Canvas?.CurrentEditMode == DocsCanvas.EditMode.Visual,
            () => MakePathIcon(Canvas?.CurrentEditMode == DocsCanvas.EditMode.Visual ? IconVisual : IconSource));
        Map(_imagePreviewBorder, "Image preview",
            () => { Canvas?.CycleImagePreview(); Canvas?.Focus(); UpdateImagePreviewButton(); },
            icon: () => MakePathIcon(Canvas?.CurrentImagePreview switch
            {
                DocsCanvas.ImagePreviewMode.Inline => IconInline,
                DocsCanvas.ImagePreviewMode.OnHover => IconOnHover,
                _ => IconOff,
            }));
        Map(_themeButton, "Theme",
            () => { Canvas?.ToggleTheme(); Canvas?.Focus(); UpdateThemeButton(); },
            icon: () =>
            {
                var theme = Canvas?.Theme ?? DocsCanvas.EditorTheme.Light;
                if (theme == DocsCanvas.EditorTheme.DarkBlue)
                    return new Path { Width = 14, Height = 14, Stretch = Stretch.Uniform, Data = IconCrescent, Fill = CrescentBrush };
                return MakePathIcon(theme == DocsCanvas.EditorTheme.Dark ? IconMoon : IconSun);
            });
        Map(_tocButton, "Contents",
            () => { Canvas?.ToggleToc(); Canvas?.Focus(); UpdateTocButton(); },
            () => Canvas?.IsTocVisible ?? false,
            () => MakePathIcon(IconToc));
        Map(_minimapButton, "Minimap",
            () => { Canvas?.ToggleMinimap(); Canvas?.Focus(); UpdateMinimapButton(); },
            () => Canvas?.IsMinimapVisible ?? false,
            () => MakePathIcon(IconMinimap));
    }

    private void BuildNavigableButtons()
    {
        _navigableButtons = [];

        void Add(ButtonBase? btn, Action action, UIElement? overflowChild = null)
        {
            if (btn != null)
                _navigableButtons.Add((btn, overflowChild ?? btn, action));
        }

        Add(_boldButton, () => { Canvas?.ToggleBold(); UpdateButtonStates(); });
        Add(_italicButton, () => { Canvas?.ToggleItalic(); UpdateButtonStates(); });
        Add(_strikethroughButton, () => { Canvas?.ToggleStrikethrough(); UpdateButtonStates(); });
        Add(_codeButton, () => { Canvas?.ToggleCodeSpan(); UpdateButtonStates(); });
        Add(_codeBlockButton, () => { Canvas?.ToggleFencedCode(); UpdateButtonStates(); });
        Add(_h1Button, () => { Canvas?.ToggleHeading(1); UpdateButtonStates(); });
        Add(_h2Button, () => { Canvas?.ToggleHeading(2); UpdateButtonStates(); });
        Add(_h3Button, () => { Canvas?.ToggleHeading(3); UpdateButtonStates(); });
        Add(_bulletButton, () => { Canvas?.ToggleBulletList(); UpdateButtonStates(); });
        Add(_orderedListButton, () => { Canvas?.ToggleOrderedList(); UpdateButtonStates(); });
        Add(_taskListButton, () => { Canvas?.ToggleTaskList(); UpdateButtonStates(); });
        Add(_quoteButton, () => { Canvas?.ToggleBlockquote(); UpdateButtonStates(); });
        Add(_linkButton, () => Canvas?.InsertLink());
        Add(_insertTableButton, () => Canvas?.InsertTable(3, 2));
        Add(_colorTextButton, () => ShowColorMenu());
        Add(_reflowButton, () => { Canvas?.Reflow(); UpdateButtonStates(); });
        Add(_hardBreaksButton, () => { Canvas?.ConvertToHardBreaks(); UpdateButtonStates(); });
        Add(_editModeButton, () => { Canvas?.ToggleEditMode(); UpdateEditModeButton(); });
        Add(_imagePreviewButton, () => { Canvas?.CycleImagePreview(); UpdateImagePreviewButton(); }, _imagePreviewBorder);
        Add(_imagePreviewArrow, () => ShowImagePreviewMenu(), _imagePreviewBorder);
        Add(_themeButton, () => { Canvas?.ToggleTheme(); UpdateThemeButton(); });
        Add(_tocButton, () => { Canvas?.ToggleToc(); UpdateTocButton(); });
        Add(_minimapButton, () => { Canvas?.ToggleMinimap(); UpdateMinimapButton(); });
        Add(_moreButton, () => ShowOverflowMenu());
        Add(_collapseButton, () => IsCollapsed = true);
    }

    internal bool ActivateKeyboardNavigation()
    {
        if (_navigableButtons == null || _navigableButtons.Count == 0) return false;
        if (IsCollapsed || Visibility != Visibility.Visible) return false;

        int idx = FindNextAccessible(-1, forward: true);
        if (idx < 0) return false;

        _isKeyboardActive = true;
        FocusButton(idx);
        return true;
    }

    internal void DeactivateKeyboardNavigation()
    {
        if (!_isKeyboardActive) return;

        if (_keyboardFocusIndex >= 0 && _navigableButtons != null
            && _keyboardFocusIndex < _navigableButtons.Count)
            _navigableButtons[_keyboardFocusIndex].Button.Focusable = false;
        _keyboardFocusIndex = -1;
        _isKeyboardActive = false;
    }

    private void FocusButton(int index)
    {
        if (_navigableButtons == null) return;

        if (_keyboardFocusIndex >= 0 && _keyboardFocusIndex < _navigableButtons.Count)
            _navigableButtons[_keyboardFocusIndex].Button.Focusable = false;

        _keyboardFocusIndex = index;
        var btn = _navigableButtons[index].Button;
        btn.Focusable = true;
        btn.Focus();
    }

    private int FindNextAccessible(int from, bool forward, bool wrap = false)
    {
        if (_navigableButtons == null) return -1;
        int count = _navigableButtons.Count;
        int step = forward ? 1 : -1;
        int i = from + step;

        while (i >= 0 && i < count)
        {
            if (IsButtonAccessible(i)) return i;
            i += step;
        }

        if (!wrap) return -1;

        i = forward ? 0 : count - 1;
        int limit = from < 0 ? 0 : from;
        while (forward ? i < limit : i > limit)
        {
            if (IsButtonAccessible(i)) return i;
            i += step;
        }

        return -1;
    }

    private bool IsButtonAccessible(int index)
    {
        if (_navigableButtons == null) return false;
        var (btn, overflowChild, _) = _navigableButtons[index];
        return btn.IsEnabled && !(_overflowPanel?.IsOverflowed(overflowChild) ?? false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || !_isKeyboardActive || _navigableButtons == null) return;

        switch (e.Key)
        {
            case Key.Left:
            {
                int next = FindNextAccessible(_keyboardFocusIndex, forward: false, wrap: true);
                if (next >= 0) FocusButton(next);
                e.Handled = true;
                break;
            }

            case Key.Right:
            {
                int next = FindNextAccessible(_keyboardFocusIndex, forward: true, wrap: true);
                if (next >= 0) FocusButton(next);
                e.Handled = true;
                break;
            }

            case Key.Home:
            {
                int first = FindNextAccessible(-1, forward: true);
                if (first >= 0) FocusButton(first);
                e.Handled = true;
                break;
            }

            case Key.End:
            {
                int last = FindNextAccessible(_navigableButtons.Count, forward: false);
                if (last >= 0) FocusButton(last);
                e.Handled = true;
                break;
            }

            case Key.Enter:
            case Key.Space:
            {
                if (_keyboardFocusIndex >= 0 && _keyboardFocusIndex < _navigableButtons.Count)
                    _navigableButtons[_keyboardFocusIndex].Action();
                e.Handled = true;
                break;
            }

            case Key.Tab:
            {
                bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                int next = FindNextAccessible(_keyboardFocusIndex, forward: !shift);
                if (next >= 0)
                    FocusButton(next);
                else
                {
                    DeactivateKeyboardNavigation();
                    Canvas?.Focus();
                }
                e.Handled = true;
                break;
            }

            case Key.Escape:
            case Key.LeftAlt:
            case Key.RightAlt:
                DeactivateKeyboardNavigation();
                Canvas?.Focus();
                e.Handled = true;
                break;

            case Key.Z:
            case Key.Y:
                if (Keyboard.Modifiers == ModifierKeys.Control && Canvas != null)
                {
                    if (e.Key == Key.Z) Canvas.PerformUndo();
                    else Canvas.PerformRedo();
                    e.Handled = true;
                }
                break;
        }
    }

    private static Path MakePathIcon(Geometry data, bool stroke = false)
    {
        var path = new Path { Width = 14, Height = 14, Stretch = Stretch.Uniform, Data = data };
        var binding = new Binding("Foreground")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(MenuItem), 1),
        };
        if (stroke)
        {
            path.SetBinding(Shape.StrokeProperty, binding);
            path.StrokeThickness = 1.5;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
            path.StrokeLineJoin = PenLineJoin.Round;
        }
        else
        {
            path.SetBinding(Shape.FillProperty, binding);
        }
        return path;
    }

    private sealed record OverflowEntry(string Label, Action Handler, Func<bool>? IsChecked = null, Func<object>? Icon = null);
}
