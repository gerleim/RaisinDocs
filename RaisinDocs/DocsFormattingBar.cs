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

    // Minimap: small rectangle with horizontal lines (document overview)
    private static readonly Geometry IconMinimap = Geometry.Parse(
        "M1,1 H15 V15 H1 Z M2,2 H14 V14 H2 Z M3,4 H13 M3,6.5 H11 M3,9 H12 M3,11.5 H9");

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
        IconOrderedList.Freeze();
        IconTaskList.Freeze();
        IconLink.Freeze();
        IconQuote.Freeze();
        IconDropdownArrow.Freeze();
        IconMinimap.Freeze();
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
    private ToggleButton? _minimapButton;
    private Path? _minimapIcon;
    private Button? _linkButton;
    private Button? _insertTableButton;
    private Button? _colorTextButton;
    private Button? _reflowButton;
    private Button? _hardBreaksButton;
    private Border? _colorBar;
    private string _lastColorName = "red";
    private OverflowPanel? _overflowPanel;
    private Button? _moreButton;
    private Dictionary<UIElement, OverflowEntry>? _overflowMap;

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

        _minimapButton = GetTemplateChild("PART_Minimap") as ToggleButton;
        _minimapIcon = GetTemplateChild("PART_MinimapIcon") as Path;
        if (_minimapIcon != null) _minimapIcon.Data = IconMinimap;
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

        BuildOverflowMap();
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
        bar.UpdateImagePreviewButton();
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

    private void UpdateMinimapButton()
    {
        if (_minimapButton == null || Canvas == null) return;
        SetCheckedSilent(_minimapButton, Canvas.IsMinimapVisible);
    }

    private void UpdateButtonStates()
    {
        var canvas = Canvas;
        if (canvas == null) return;

        var kind = canvas.CurrentBlockKind;
        bool inCodeBlock = kind == BlockKind.FencedCodeLine;

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
            _reflowButton.IsEnabled = canvas.CanReformat;
        if (_hardBreaksButton != null)
            _hardBreaksButton.IsEnabled = canvas.CanConvertToHardBreaks;
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

        void Map(UIElement? el, string label, Action handler, Func<bool>? isChecked = null)
        {
            if (el != null) _overflowMap[el] = new OverflowEntry(label, handler, isChecked);
        }

        Map(_boldButton, "Bold (Ctrl+B)",
            () => { Canvas?.ToggleBold(); Canvas?.Focus(); UpdateButtonStates(); },
            () => Canvas?.SelectionIsBold ?? false);
        Map(_italicButton, "Italic (Ctrl+I)",
            () => { Canvas?.ToggleItalic(); Canvas?.Focus(); UpdateButtonStates(); },
            () => Canvas?.SelectionIsItalic ?? false);
        Map(_strikethroughButton, "Strikethrough",
            () => { Canvas?.ToggleStrikethrough(); Canvas?.Focus(); UpdateButtonStates(); },
            () => Canvas?.SelectionIsStrikethrough ?? false);
        Map(_codeButton, "Code",
            () => { Canvas?.ToggleCodeSpan(); Canvas?.Focus(); UpdateButtonStates(); },
            () => Canvas?.SelectionIsCode ?? false);
        Map(_codeBlockButton, "Code block",
            () => { Canvas?.ToggleFencedCode(); Canvas?.Focus(); UpdateButtonStates(); },
            () => Canvas?.CurrentBlockKind == BlockKind.FencedCodeLine);
        Map(_h1Button, "Heading 1",
            () => { Canvas?.ToggleHeading(1); Canvas?.Focus(); UpdateButtonStates(); },
            () => Canvas?.CurrentBlockKind == BlockKind.Heading1);
        Map(_h2Button, "Heading 2",
            () => { Canvas?.ToggleHeading(2); Canvas?.Focus(); UpdateButtonStates(); },
            () => Canvas?.CurrentBlockKind == BlockKind.Heading2);
        Map(_h3Button, "Heading 3",
            () => { Canvas?.ToggleHeading(3); Canvas?.Focus(); UpdateButtonStates(); },
            () => Canvas?.CurrentBlockKind == BlockKind.Heading3);
        Map(_bulletButton, "Bullet list",
            () => { Canvas?.ToggleBulletList(); Canvas?.Focus(); UpdateButtonStates(); },
            () => Canvas?.CurrentBlockKind == BlockKind.UnorderedListItem);
        Map(_orderedListButton, "Ordered list",
            () => { Canvas?.ToggleOrderedList(); Canvas?.Focus(); UpdateButtonStates(); },
            () => Canvas?.CurrentBlockKind == BlockKind.OrderedListItem);
        Map(_taskListButton, "Task list",
            () => { Canvas?.ToggleTaskList(); Canvas?.Focus(); UpdateButtonStates(); },
            () => Canvas?.CurrentBlockKind is BlockKind.TaskListItemUnchecked or BlockKind.TaskListItemChecked);
        Map(_quoteButton, "Blockquote",
            () => { Canvas?.ToggleBlockquote(); Canvas?.Focus(); UpdateButtonStates(); },
            () => Canvas?.CurrentBlockKind == BlockKind.Blockquote);
        Map(_linkButton, "Link (Ctrl+K)",
            () => { Canvas?.InsertLink(); Canvas?.Focus(); });
        Map(_insertTableButton, "Insert table",
            () => { Canvas?.InsertTable(3, 2); Canvas?.Focus(); });
        Map(_colorTextButton, "Text color",
            () => ShowColorMenu(_moreButton));
        Map(_reflowButton, "Reformat selection",
            () => { Canvas?.Reflow(); Canvas?.Focus(); });
        Map(_hardBreaksButton, "Convert to hard breaks",
            () => { Canvas?.ConvertToHardBreaks(); Canvas?.Focus(); });
        Map(_editModeButton, "Edit mode (Ctrl+M)",
            () => { Canvas?.ToggleEditMode(); Canvas?.Focus(); UpdateEditModeButton(); },
            () => Canvas?.CurrentEditMode == DocsCanvas.EditMode.Visual);
        Map(_imagePreviewBorder, "Image preview",
            () => { Canvas?.CycleImagePreview(); Canvas?.Focus(); UpdateImagePreviewButton(); });
        Map(_themeButton, "Theme",
            () => { Canvas?.ToggleTheme(); Canvas?.Focus(); UpdateThemeButton(); });
        Map(_minimapButton, "Minimap",
            () => { Canvas?.ToggleMinimap(); Canvas?.Focus(); UpdateMinimapButton(); },
            () => Canvas?.IsMinimapVisible ?? false);
    }

    private sealed record OverflowEntry(string Label, Action Handler, Func<bool>? IsChecked = null);
}
