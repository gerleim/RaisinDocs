using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RaisinDocs;

internal sealed class SpellCheckController
{
    private readonly ICanvasOperations _canvas;
    private readonly IImageServices _images;
    private readonly IDocumentServices _doc;
    private readonly IRenderingServices _rendering;
    private readonly ILayoutDataServices _layout;
    private readonly IParsedContentServices _content;
    private readonly INavigationServices _nav;
    private readonly IVisualModeServices _visualMode;
    private readonly IScrollServices _scroll;
    private readonly ILoggingServices _logging;

    private bool _spellCheckEnabled;
    private SpellCheckService? _spellCheckService;
    private string? _projectFolder;
    private readonly HashSet<int> _dirtySpellBlocks = new();
    private DispatcherTimer? _spellCheckTimer;
    private List<IReadOnlyList<SpellingError>?>? _blockSpellingErrors;
    private Pen? _spellErrorPen;

    public bool SpellCheckEnabled => _spellCheckEnabled;
    public string? ProjectFolder => _projectFolder;

    public SpellCheckController(ICanvasOperations canvas, IImageServices images, IDocumentServices doc,
        IRenderingServices rendering, ILayoutDataServices layout, IParsedContentServices content,
        INavigationServices nav, IVisualModeServices visualMode, IScrollServices scroll,
        ILoggingServices logging)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _rendering = rendering ?? throw new ArgumentNullException(nameof(rendering));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));
        _visualMode = visualMode ?? throw new ArgumentNullException(nameof(visualMode));
        _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
        _logging = logging ?? throw new ArgumentNullException(nameof(logging));
    }

    public void SetSpellCheckEnabled(bool enabled)
    {
        if (_spellCheckEnabled == enabled) return;
        _spellCheckEnabled = enabled;

        if (enabled)
        {
            EnsureSpellCheckInitialized();
            RecheckAllBlocks();
        }
        else
        {
            _blockSpellingErrors = null;
            _spellCheckTimer?.Stop();
        }

        _rendering.InvalidateVisual();
    }

    public void Cleanup()
    {
        if (_spellCheckTimer != null)
        {
            _spellCheckTimer.Stop();
            _spellCheckTimer.Tick -= SpellCheckTimerTick;
            _spellCheckTimer = null;
        }
        _spellCheckService = null;
        _blockSpellingErrors = null;
    }

    internal void OnDocumentBasePathChanged()
    {
        if (_spellCheckService is null) return;
        ResolveAndLoadProjectDictionary();
        if (_spellCheckEnabled)
            RecheckAllBlocks();
    }

    public void SetProjectFolder(string folder)
    {
        RaisinDocsPaths.SetProjectFolder(folder);
        _projectFolder = folder;
        if (_spellCheckService is not null)
        {
            _spellCheckService.LoadProjectDictionary(RaisinDocsPaths.GetProjectDictionaryPath(folder));
            if (_spellCheckEnabled)
            {
                RecheckAllBlocks();
                _rendering.InvalidateVisual();
            }
        }
    }

    private void ResolveAndLoadProjectDictionary()
    {
        if (_images.DocumentBasePath is not null)
        {
            var root = RaisinDocsPaths.FindProjectRoot(_images.DocumentBasePath);
            _projectFolder = root ?? _images.DocumentBasePath;
        }
        else
        {
            _projectFolder = null;
        }
        var dictPath = _projectFolder is not null
            ? RaisinDocsPaths.GetProjectDictionaryPath(_projectFolder) : null;
        _spellCheckService!.LoadProjectDictionary(dictPath);
    }

    private void EnsureSpellCheckInitialized()
    {
        if (_spellCheckService is not null) return;

        // Read at the moment of failure rather than captured now, since a host sets the canvas's
        // logger after constructing it. Warning, because nothing is lost: the word is already in
        // memory and the next successful save writes it.
        _spellCheckService = new SpellCheckService
        {
            SaveFailed = message => _logging.Logger?.Log(DocsLogLevel.Warning, message),
        };
        _spellCheckService.LoadEmbeddedDictionary();
        ResolveAndLoadProjectDictionary();

        _spellErrorPen = new Pen(Brushes.Red, 0.75);
        _spellErrorPen.Freeze();

        _spellCheckTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, _canvas.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _spellCheckTimer.Tick += SpellCheckTimerTick;
    }

    internal void OnContentChanged()
    {
        if (!_spellCheckEnabled || _spellCheckService is null) return;

        int from = Math.Min(_doc.Document.AnchorBlock, _doc.Document.CursorBlock);
        int to = Math.Max(_doc.Document.AnchorBlock, _doc.Document.CursorBlock);
        for (int i = from; i <= to; i++)
            _dirtySpellBlocks.Add(i);

        _spellCheckTimer?.Stop();
        _spellCheckTimer?.Start();
    }

    private void SpellCheckTimerTick(object? sender, EventArgs e)
    {
        _spellCheckTimer!.Stop();
        if (!_spellCheckEnabled || _spellCheckService is null) return;

        _layout.ComputeLayout();

        if (_blockSpellingErrors is null || _blockSpellingErrors.Count != _doc.Document.BlockCount)
        {
            RecheckAllBlocks();
            _rendering.InvalidateVisual();
            return;
        }

        foreach (var blockIdx in _dirtySpellBlocks)
        {
            if (blockIdx >= _doc.Document.BlockCount) continue;
            RecheckBlock(blockIdx);
        }

        _dirtySpellBlocks.Clear();
        _rendering.InvalidateVisual();
    }

    private void RecheckBlock(int blockIndex)
    {
        if (_content.ParsedBlocks is null || blockIndex >= _content.ParsedBlocks.Count) return;

        var text = _doc.GetBlockText(blockIndex);
        var parsed = _content.ParsedBlocks[blockIndex];
        var words = MarkdownParser.ExtractCheckableWords(text, parsed);
        var errors = new List<SpellingError>();

        foreach (var (offset, word) in words)
        {
            if (!_spellCheckService!.Check(word))
                errors.Add(new SpellingError(offset, word.Length, word));
        }

        while (_blockSpellingErrors!.Count <= blockIndex)
            _blockSpellingErrors.Add(null);

        _blockSpellingErrors[blockIndex] = errors.Count > 0 ? errors : null;
    }

    private void RecheckAllBlocks()
    {
        if (_spellCheckService is null) return;

        _layout.ComputeLayout();

        _blockSpellingErrors = new List<IReadOnlyList<SpellingError>?>(
            Enumerable.Repeat<IReadOnlyList<SpellingError>?>(null, _doc.Document.BlockCount));

        for (int i = 0; i < _doc.Document.BlockCount; i++)
            RecheckBlock(i);

        _dirtySpellBlocks.Clear();
    }

    internal void DrawSpellingErrors(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        if (_blockSpellingErrors is null || _spellErrorPen is null) return;

        for (int i = 0; i < _layout.VisualLines.Count; i++)
        {
            var vl = _layout.VisualLines[i];
            double lineH = _layout.GetEffectiveLineHeight(vl);
            double lineY = _layout.LineYPositions[i];
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            if (vl.Group != null)
            {
                DrawSpellingErrorsOnJoinedLine(dc, i, vl, lineY, lineH, effectiveScroll);
                continue;
            }

            if (vl.BlockIndex >= _blockSpellingErrors.Count) continue;
            var errors = _blockSpellingErrors[vl.BlockIndex];
            if (errors is null) continue;

            int vlEnd = vl.StartOffset + vl.Length;

            foreach (var err in errors)
            {
                int errEnd = err.StartOffset + err.Length;
                if (err.StartOffset >= vlEnd || errEnd <= vl.StartOffset) continue;

                int hlStart = Math.Max(err.StartOffset, vl.StartOffset);
                int hlEnd = Math.Min(errEnd, vlEnd);

                // Under the line of text each piece is on: the bottom of a wrapped cell's own line,
                // not the bottom of the whole row.
                _spans.Clear();
                _nav.GetRangeSpans(i, hlStart, hlEnd, _spans);
                foreach (var span in _spans)
                {
                    if (span.X2 - span.X1 <= 0) continue;
                    double baselineY = lineY - effectiveScroll + span.Bottom - 2;
                    DrawSquigglyLine(dc, DocsCanvas._padding + span.X1, DocsCanvas._padding + span.X2, baselineY);
                }
            }
        }
    }

    private readonly List<DocsCanvas.LineSpan> _spans = new();

    private void DrawSpellingErrorsOnJoinedLine(DrawingContext dc, int vlIndex, DocsCanvas.VisualLine vl,
        double lineY, double lineH, double effectiveScroll)
    {
        var group = vl.Group!;

        foreach (var seg in group.Segments)
        {
            if (seg.BlockIndex >= _blockSpellingErrors!.Count) continue;
            var errors = _blockSpellingErrors[seg.BlockIndex];
            if (errors is null) continue;

            foreach (var err in errors)
            {
                int startJoined = group.SourceToJoined(seg.BlockIndex, err.StartOffset);
                int endJoined = group.SourceToJoined(seg.BlockIndex, err.StartOffset + err.Length);
                if (startJoined < 0 || endJoined < 0) continue;

                int vlStart = vl.StartOffset;
                int vlEnd = vl.StartOffset + vl.Length;
                if (vlEnd <= startJoined || vlStart >= endJoined) continue;

                int hlStart = Math.Max(vlStart, startJoined);
                int hlEnd = Math.Min(vlEnd, endJoined);

                double x1 = _nav.XInVisualLine(vlIndex, hlStart);
                double x2 = _nav.XInVisualLine(vlIndex, hlEnd);

                double w = x2 - x1;
                if (w > 0)
                {
                    double baselineY = lineY - effectiveScroll + lineH - 2;
                    DrawSquigglyLine(dc, DocsCanvas._padding + x1, DocsCanvas._padding + x2, baselineY);
                }
            }
        }
    }

    private void DrawSquigglyLine(DrawingContext dc, double x1, double x2, double y)
    {
        const double waveHeight = 1.5;
        const double waveLength = 3.0;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x1, y), false, false);
            double x = x1;
            bool up = true;
            while (x < x2)
            {
                x = Math.Min(x + waveLength, x2);
                ctx.LineTo(new Point(x, y + (up ? -waveHeight : waveHeight)), true, false);
                up = !up;
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, _spellErrorPen, geometry);
    }

    private SpellingError? FindSpellingErrorAt(int blockIndex, int charOffset)
    {
        if (_blockSpellingErrors is null || blockIndex >= _blockSpellingErrors.Count) return null;
        var errors = _blockSpellingErrors[blockIndex];
        if (errors is null) return null;

        foreach (var err in errors)
        {
            if (charOffset >= err.StartOffset && charOffset < err.StartOffset + err.Length)
                return err;
        }
        return null;
    }

    internal bool AddSpellCheckMenuItems(ContextMenu menu, Point position)
    {
        if (_spellCheckService is null || _blockSpellingErrors is null) return false;

        _nav.HitTestToPosition(position, out int blockIndex, out int charOffset);
        var error = FindSpellingErrorAt(blockIndex, charOffset);
        if (error is null) return false;

        var err = error.Value;
        var suggestions = _spellCheckService.Suggest(err.Word);

        if (suggestions.Count > 0)
        {
            foreach (var suggestion in suggestions)
            {
                var item = new MenuItem { Header = suggestion, FontWeight = FontWeights.Bold };
                _canvas.StyleMenuItem(item);
                var capturedSuggestion = suggestion;
                var capturedBlock = blockIndex;
                var capturedErr = err;
                item.Click += (_, _) =>
                {
                    ReplaceWord(capturedBlock, capturedErr.StartOffset, capturedErr.Length, capturedSuggestion);
                    _canvas.FocusCanvas();
                };
                menu.Items.Add(item);
            }
        }
        else
        {
            var noSuggestions = new MenuItem { Header = "(no suggestions)", IsEnabled = false };
            _canvas.StyleMenuItem(noSuggestions);
            menu.Items.Add(noSuggestions);
        }

        menu.Items.Add(new Separator());

        var ignoreItem = new MenuItem { Header = "Ignore All" };
        _canvas.StyleMenuItem(ignoreItem);
        var wordToIgnore = err.Word;
        ignoreItem.Click += (_, _) =>
        {
            _spellCheckService.IgnoreAll(wordToIgnore);
            RecheckAllBlocks();
            _rendering.InvalidateVisual();
            _canvas.FocusCanvas();
        };
        menu.Items.Add(ignoreItem);

        var addItem = new MenuItem { Header = "Add to Dictionary" };
        _canvas.StyleMenuItem(addItem);
        var wordToAdd = err.Word;
        addItem.Click += (_, _) =>
        {
            _spellCheckService.AddToUserDictionary(wordToAdd);
            RecheckAllBlocks();
            _rendering.InvalidateVisual();
            _canvas.FocusCanvas();
        };
        menu.Items.Add(addItem);

        var addProjectItem = new MenuItem { Header = "Add to Project Dictionary" };
        _canvas.StyleMenuItem(addProjectItem);
        var wordForProject = err.Word;
        addProjectItem.Click += (_, _) =>
        {
            _spellCheckService.AddToProjectDictionary(wordForProject);
            RecheckAllBlocks();
            _rendering.InvalidateVisual();
            _canvas.FocusCanvas();
        };
        menu.Items.Add(addProjectItem);

        return true;
    }

    private void ReplaceWord(int blockIndex, int offset, int length, string replacement)
    {
        _doc.Document.BeginUndoGroup();
        _doc.Document.RemoveTextAt(blockIndex, offset, length);
        _doc.Document.InsertTextAt(blockIndex, offset, replacement);
        _doc.Document.CursorBlock = blockIndex;
        _doc.Document.CursorOffset = offset + replacement.Length;
        _doc.Document.AnchorBlock = blockIndex;
        _doc.Document.AnchorOffset = offset + replacement.Length;
        _doc.Document.SealUndoGroup();
        _layout.InvalidateLayout();
        _scroll.EnsureCursorVisible();
    }

    public static string? UserDictionaryPath => RaisinDocsPaths.GetUserDictionaryPath();
    public string? ProjectDictionaryPath => _projectFolder is not null
        ? RaisinDocsPaths.GetProjectDictionaryPath(_projectFolder) : null;

    internal SpellCheckService? TestSpellCheckService => _spellCheckService;
    internal IReadOnlyList<SpellingError>? TestGetSpellingErrors(int blockIndex)
        => _blockSpellingErrors is not null && blockIndex < _blockSpellingErrors.Count
            ? _blockSpellingErrors[blockIndex]
            : null;
}
