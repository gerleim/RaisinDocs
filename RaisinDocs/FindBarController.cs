using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RaisinDocs;

internal class FindBarController
{
    private readonly DocsCanvas _canvas;
    private readonly Border _border;
    private TextBox _searchBox = null!;
    private TextBox _replaceBox = null!;
    private TextBlock _matchInfo = null!;
    private StackPanel _replaceRow = null!;
    private bool _caseSensitive;
    private ToggleButton _caseToggle = null!;
    private readonly DispatcherTimer _debounce;

    public Border Element => _border;
    public bool IsOpen => _border.Visibility == Visibility.Visible;

    public FindBarController(DocsCanvas canvas)
    {
        _canvas = canvas;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _canvas.ExecuteSearch(_searchBox.Text, _caseSensitive);
        };
        _border = Build();
        _border.Visibility = Visibility.Collapsed;
    }

    public void Open(bool showReplace, string? initialText)
    {
        _replaceRow.Visibility = showReplace ? Visibility.Visible : Visibility.Collapsed;
        _border.Visibility = Visibility.Visible;

        if (initialText != null)
            _searchBox.Text = initialText;

        _searchBox.Focus();
        _searchBox.SelectAll();
    }

    public void Close()
    {
        _border.Visibility = Visibility.Collapsed;
        _debounce.Stop();
        _canvas.Focus();
    }

    public void UpdateMatchInfo(int currentIndex, int totalCount)
    {
        if (totalCount == 0)
            _matchInfo.Text = "No results";
        else
            _matchInfo.Text = $"{currentIndex + 1} of {totalCount}";
    }

    public void ApplyTheme(Brush background, Brush foreground, Brush syntax, Brush codeBg)
    {
        _border.Background = background;
        _border.BorderBrush = syntax;

        _matchInfo.Foreground = foreground;

        ApplyTextBoxTheme(_searchBox, foreground, codeBg, syntax);
        ApplyTextBoxTheme(_replaceBox, foreground, codeBg, syntax);

        foreach (var btn in FindButtons(_border))
        {
            btn.Foreground = foreground;
            btn.Background = Brushes.Transparent;
            btn.BorderBrush = Brushes.Transparent;
        }

        _caseToggle.Foreground = foreground;
        _caseToggle.Background = _caseSensitive ? syntax : Brushes.Transparent;
        _caseToggle.BorderBrush = syntax;
    }

    private static void ApplyTextBoxTheme(TextBox tb, Brush fg, Brush bg, Brush border)
    {
        tb.Foreground = fg;
        tb.Background = bg;
        tb.BorderBrush = border;
        tb.CaretBrush = fg;
    }

    private static IEnumerable<ButtonBase> FindButtons(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ButtonBase btn)
                yield return btn;
            foreach (var nested in FindButtons(child))
                yield return nested;
        }
    }

    private Border Build()
    {
        _searchBox = CreatePlainTextBox(200);
        _replaceBox = CreatePlainTextBox(200);

        _searchBox.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            _debounce.Start();
        };

        _matchInfo = new TextBlock
        {
            Text = "No results",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Margin = new Thickness(6, 0, 2, 0),
            MinWidth = 60,
        };

        var prevBtn = CreateIconButton(CreateUpArrowPath(), "Previous match (Shift+F3)");
        prevBtn.Click += (_, _) => _canvas.NavigateMatch(-1);

        var nextBtn = CreateIconButton(CreateDownArrowPath(), "Next match (F3)");
        nextBtn.Click += (_, _) => _canvas.NavigateMatch(1);

        _caseToggle = new ToggleButton
        {
            Content = new TextBlock
            {
                Text = "Aa",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
            },
            Width = 26,
            Height = 24,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1),
            ToolTip = "Match case",
            Template = CreateButtonTemplate(),
        };
        _caseToggle.Click += (_, _) =>
        {
            _caseSensitive = _caseToggle.IsChecked == true;
            _caseToggle.Background = _caseSensitive ? _caseToggle.BorderBrush : Brushes.Transparent;
            _canvas.ExecuteSearch(_searchBox.Text, _caseSensitive);
        };

        var closeBtn = CreateIconButton(CreateClosePath(), "Close (Escape)");
        closeBtn.Click += (_, _) => _canvas.CloseFind();

        var findRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _searchBox, _matchInfo, prevBtn, nextBtn, _caseToggle, closeBtn },
        };

        var replaceBtn = CreateTextButton("Replace");
        replaceBtn.Click += (_, _) => _canvas.ReplaceCurrent(_replaceBox.Text);

        var replaceAllBtn = CreateTextButton("All");
        replaceAllBtn.Click += (_, _) => _canvas.ReplaceAll(_replaceBox.Text);

        _replaceRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Visibility = Visibility.Collapsed,
            Children = { _replaceBox, replaceBtn, replaceAllBtn },
        };

        var outerPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children = { findRow, _replaceRow },
        };

        void HandleKey(object? sender, KeyEventArgs e)
        {
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            if (e.Key == Key.Escape)
            {
                _canvas.CloseFind();
                e.Handled = true;
            }
            else if (e.Key == Key.F && ctrl)
            {
                _replaceRow.Visibility = Visibility.Collapsed;
                _searchBox.Focus();
                _searchBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.H && ctrl)
            {
                _replaceRow.Visibility = Visibility.Visible;
                _searchBox.Focus();
                _searchBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.F3)
            {
                _canvas.NavigateMatch(shift ? -1 : 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && sender == _searchBox)
            {
                _canvas.NavigateMatch(shift ? -1 : 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && sender == _replaceBox)
            {
                _canvas.ReplaceCurrent(_replaceBox.Text);
                e.Handled = true;
            }
        }
        _searchBox.KeyDown += HandleKey;
        _replaceBox.KeyDown += HandleKey;

        var border = new Border
        {
            Child = outerPanel,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 4, 6, 4),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 20, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 2,
                Opacity = 0.3,
            },
        };

        return border;
    }

    private static Button CreateIconButton(Path icon, string tooltip)
    {
        var btn = new Button
        {
            Content = icon,
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(1, 0, 1, 0),
            BorderThickness = new Thickness(0),
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
            Template = CreateButtonTemplate(),
        };
        return btn;
    }

    private static Button CreateTextButton(string text)
    {
        var btn = new Button
        {
            Content = new TextBlock { Text = text, FontSize = 12 },
            Height = 24,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(4, 0, 0, 0),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Template = CreateButtonTemplate(),
        };
        return btn;
    }

    private static ControlTemplate CreateButtonTemplate()
    {
        var borderFactory = new FrameworkElementFactory(typeof(Border), "Bd");
        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(contentPresenter);

        return new ControlTemplate(typeof(ButtonBase)) { VisualTree = borderFactory };
    }

    private static Path CreateUpArrowPath()
    {
        return new Path
        {
            Data = Geometry.Parse("M 0,6 L 5,0 L 10,6"),
            StrokeThickness = 1.5,
            Stroke = Brushes.Gray,
            Width = 10,
            Height = 7,
            Stretch = Stretch.Uniform,
        };
    }

    private static Path CreateDownArrowPath()
    {
        return new Path
        {
            Data = Geometry.Parse("M 0,0 L 5,6 L 10,0"),
            StrokeThickness = 1.5,
            Stroke = Brushes.Gray,
            Width = 10,
            Height = 7,
            Stretch = Stretch.Uniform,
        };
    }

    private static Path CreateClosePath()
    {
        return new Path
        {
            Data = Geometry.Parse("M 0,0 L 8,8 M 8,0 L 0,8"),
            StrokeThickness = 1.5,
            Stroke = Brushes.Gray,
            Width = 8,
            Height = 8,
            Stretch = Stretch.Uniform,
        };
    }

    private static TextBox CreatePlainTextBox(double minWidth)
    {
        var tb = new TextBox
        {
            MinWidth = minWidth,
            Padding = new Thickness(3, 1, 3, 1),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12,
        };

        var factory = new FrameworkElementFactory(typeof(Border), "Bd");
        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        factory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        factory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

        var contentHost = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        factory.AppendChild(contentHost);

        var template = new ControlTemplate(typeof(TextBox)) { VisualTree = factory };
        tb.Template = template;

        return tb;
    }
}
