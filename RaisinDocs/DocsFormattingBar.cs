using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace RaisinDocs;

public class DocsFormattingBar : Control
{
    public static readonly DependencyProperty CanvasProperty =
        DependencyProperty.Register(nameof(Canvas), typeof(DocsCanvas), typeof(DocsFormattingBar),
            new PropertyMetadata(null, OnCanvasChanged));

    public DocsCanvas? Canvas
    {
        get => (DocsCanvas?)GetValue(CanvasProperty);
        set => SetValue(CanvasProperty, value);
    }

    static DocsFormattingBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DocsFormattingBar),
            new FrameworkPropertyMetadata(typeof(DocsFormattingBar)));
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
            oldCanvas.FormattingChanged -= bar.OnFormattingChanged;
        if (e.NewValue is DocsCanvas newCanvas)
            newCanvas.FormattingChanged += bar.OnFormattingChanged;
        bar.UpdateButtonStates();
    }

    private void OnFormattingChanged(object? sender, EventArgs e) => UpdateButtonStates();

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
