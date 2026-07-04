using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace RaisinDocs;

internal class LinkPopupController
{
    private readonly Document _doc;
    private readonly DocsCanvas _canvas;

    private Popup? _popup;
    private TextBox? _text;
    private TextBox? _url;
    private TextBox? _label;
    private TextBlock? _labelHeader;
    private int _block;
    private int _start;
    private int _currentLength;
    private string _originalText = "";
    private bool _updating;
    private bool _cancelling;
    private bool _readOnly;

    public bool IsOpen => _popup is { IsOpen: true };

    public LinkPopupController(Document doc, DocsCanvas canvas)
    {
        _doc = doc;
        _canvas = canvas;
    }

    public void Show(InlineLink? existingLink, string? selectedText, int selStart, int selEnd)
    {
        if (_popup == null)
            Build();

        _block = _doc.CursorBlock;

        bool isRef = existingLink?.RefLabel != null;
        _readOnly = isRef;

        _label!.Visibility = isRef ? Visibility.Visible : Visibility.Collapsed;
        _labelHeader!.Visibility = isRef ? Visibility.Visible : Visibility.Collapsed;
        _text!.IsReadOnly = isRef;
        _url!.IsReadOnly = isRef;

        _doc.BeginUndoGroup();
        _updating = true;

        if (existingLink != null)
        {
            var link = existingLink.Value;
            _start = link.Start;
            _currentLength = link.Length;
            _originalText = _doc.GetBlockText(_block).Substring(link.Start, link.Length);
            _text!.Text = link.Text;
            _url!.Text = link.Url;
            _label!.Text = isRef ? link.RefLabel : "";
        }
        else
        {
            if (selectedText != null)
            {
                _start = selStart;
                _currentLength = selEnd - selStart;
                _originalText = selectedText;
                _doc.AnchorBlock = _doc.CursorBlock;
                _doc.AnchorOffset = _doc.CursorOffset;
                _text!.Text = selectedText;
            }
            else
            {
                _start = _doc.CursorOffset;
                _currentLength = 0;
                _originalText = "";
                _text!.Text = "";
            }
            _url!.Text = "";
            _label!.Text = "";
        }

        _updating = false;

        _popup!.PlacementTarget = _canvas;
        _popup.Placement = PlacementMode.Relative;

        if (isRef)
            _text!.Focus();
        else if (existingLink != null)
            _url!.Focus();
        else if (!string.IsNullOrEmpty(_text!.Text))
            _url!.Focus();
        else
            _text!.Focus();

        if (!isRef)
            _url!.SelectAll();
    }

    public void SetPopupPosition(double x, double y)
    {
        if (_popup == null) return;
        _popup.HorizontalOffset = x;
        _popup.VerticalOffset = y;
        _popup.IsOpen = true;
    }

    public void Close()
    {
        if (_popup is { IsOpen: true })
            _popup.IsOpen = false;
    }

    public void Cancel()
    {
        if (_popup is not { IsOpen: true }) return;

        if (!_readOnly)
        {
            _doc.RemoveTextAt(_block, _start, _currentLength);
            if (_originalText.Length > 0)
                _doc.InsertTextAt(_block, _start, _originalText);
            _doc.CursorBlock = _block;
            _doc.CursorOffset = _start + _originalText.Length;
            _doc.AnchorBlock = _doc.CursorBlock;
            _doc.AnchorOffset = _doc.CursorOffset;
        }
        _doc.SealUndoGroup();
        _cancelling = true;
        _popup.IsOpen = false;
    }

    public void ApplyTheme(Brush background, Brush foreground, Brush syntax, Brush codeBackground)
    {
        if (_popup?.Child is not Border border) return;

        border.Background = background;
        border.BorderBrush = syntax;

        foreach (var child in ((StackPanel)border.Child).Children)
        {
            if (child is TextBox tb)
            {
                tb.Background = codeBackground;
                tb.Foreground = foreground;
                tb.BorderBrush = syntax;
                tb.CaretBrush = foreground;
            }
            else if (child is TextBlock lbl)
            {
                lbl.Foreground = foreground;
            }
        }
    }

    private void OnContentChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updating || _readOnly || _popup is not { IsOpen: true }) return;

        string text = _text!.Text.Trim();
        string url = _url!.Text.Trim();

        string newContent;
        if (!string.IsNullOrEmpty(url))
        {
            if (string.IsNullOrEmpty(text)) text = url;
            newContent = $"[{text}]({url})";
        }
        else
        {
            newContent = text;
        }

        _doc.RemoveTextAt(_block, _start, _currentLength);
        if (newContent.Length > 0)
            _doc.InsertTextAt(_block, _start, newContent);
        _currentLength = newContent.Length;

        _canvas.InvalidateLayout();
    }

    private void Build()
    {
        _text = CreatePlainTextBox(160);
        _url = CreatePlainTextBox(200);
        _label = CreatePlainTextBox(80);
        _label.IsReadOnly = true;

        _text.TextChanged += OnContentChanged;
        _url.TextChanged += OnContentChanged;

        void HandleKey(object? s, KeyEventArgs e)
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control) { Cancel(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
            else if (_readOnly) { if (e.Key == Key.Enter) { Close(); e.Handled = true; } }
            else if (e.Key == Key.Enter && s == _text) { _url!.Focus(); _url.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Enter && s == _url) { Close(); e.Handled = true; }
        }
        _text.KeyDown += HandleKey;
        _url.KeyDown += HandleKey;
        _label.KeyDown += HandleKey;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 4, 6, 4),
        };
        panel.Children.Add(new TextBlock { Text = "Text", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        panel.Children.Add(_text);
        _labelHeader = new TextBlock { Text = "Ref", Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        panel.Children.Add(_labelHeader);
        panel.Children.Add(_label);
        panel.Children.Add(new TextBlock { Text = "URL", Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        panel.Children.Add(_url);

        var border = new Border
        {
            Child = panel,
            BorderThickness = new Thickness(1),
        };

        _popup = new Popup
        {
            Child = border,
            StaysOpen = false,
        };
        _popup.Closed += (_, _) =>
        {
            if (!_cancelling)
            {
                _doc.CursorBlock = _block;
                _doc.CursorOffset = _start + _currentLength;
                _doc.AnchorBlock = _doc.CursorBlock;
                _doc.AnchorOffset = _doc.CursorOffset;
                _doc.SealUndoGroup();
            }
            _cancelling = false;
            _updating = false;
            _canvas.Focus();
            _canvas.InvalidateLayout();
            _canvas.EnsureCursorVisible();
            _canvas.RaiseFormattingChanged();
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
