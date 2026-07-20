using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RaisinDocs;

public partial class DocsCanvas
{
    private bool _spellCheckEnabled;
    private SpellCheckService? _spellCheckService;
    private string? _projectFolder;
    private readonly HashSet<int> _dirtySpellBlocks = new();
    private DispatcherTimer? _spellCheckTimer;
    private List<IReadOnlyList<SpellingError>?>? _blockSpellingErrors;
    private Pen? _spellErrorPen;

    public bool SpellCheckEnabled => _spellCheckEnabled;
    public string? ProjectFolder => _projectFolder;

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

        InvalidateVisual();
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
        ProjectRootFinder.SetProjectFolder(folder);
        _projectFolder = folder;
        if (_spellCheckService is not null)
        {
            _spellCheckService.LoadProjectDictionary(folder);
            if (_spellCheckEnabled)
            {
                RecheckAllBlocks();
                InvalidateVisual();
            }
        }
    }

    private void ResolveAndLoadProjectDictionary()
    {
        if (DocumentBasePath is not null)
        {
            var root = ProjectRootFinder.FindProjectRoot(DocumentBasePath);
            _projectFolder = root ?? DocumentBasePath;
        }
        else
        {
            _projectFolder = null;
        }
        _spellCheckService!.LoadProjectDictionary(_projectFolder);
    }

    private void EnsureSpellCheckInitialized()
    {
        if (_spellCheckService is not null) return;

        _spellCheckService = new SpellCheckService();
        _spellCheckService.LoadEmbeddedDictionary();
        ResolveAndLoadProjectDictionary();

        _spellErrorPen = new Pen(Brushes.Red, 0.75);
        _spellErrorPen.Freeze();

        _spellCheckTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _spellCheckTimer.Tick += SpellCheckTimerTick;
    }

    private void OnContentChangedForSpellCheck()
    {
        if (!_spellCheckEnabled || _spellCheckService is null) return;

        _dirtySpellBlocks.Add(_doc.CursorBlock);

        _spellCheckTimer?.Stop();
        _spellCheckTimer?.Start();
    }

    private void SpellCheckTimerTick(object? sender, EventArgs e)
    {
        _spellCheckTimer!.Stop();
        if (!_spellCheckEnabled || _spellCheckService is null) return;

        ComputeLayout();

        if (_blockSpellingErrors is null || _blockSpellingErrors.Count != _doc.BlockCount)
        {
            RecheckAllBlocks();
            InvalidateVisual();
            return;
        }

        foreach (var blockIdx in _dirtySpellBlocks)
        {
            if (blockIdx >= _doc.BlockCount) continue;
            RecheckBlock(blockIdx);
        }

        _dirtySpellBlocks.Clear();
        InvalidateVisual();
    }

    private void RecheckBlock(int blockIndex)
    {
        if (_parsedBlocks is null || blockIndex >= _parsedBlocks.Count) return;

        var text = _doc.GetBlockText(blockIndex);
        var parsed = _parsedBlocks[blockIndex];
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

        ComputeLayout();

        _blockSpellingErrors = new List<IReadOnlyList<SpellingError>?>(
            Enumerable.Repeat<IReadOnlyList<SpellingError>?>(null, _doc.BlockCount));

        for (int i = 0; i < _doc.BlockCount; i++)
            RecheckBlock(i);

        _dirtySpellBlocks.Clear();
    }

    private void DrawSpellingErrors(DrawingContext dc, double effectiveScroll,
        double viewTop, double viewBottom)
    {
        if (_blockSpellingErrors is null || _spellErrorPen is null) return;

        for (int i = 0; i < _visualLines.Count; i++)
        {
            var vl = _visualLines[i];
            double lineH = GetEffectiveLineHeight(vl);
            double lineY = _lineYPositions[i];
            if (lineY + lineH < viewTop) continue;
            if (lineY > viewBottom) break;

            if (vl.Group != null)
            {
                DrawSpellingErrorsOnJoinedLine(dc, vl, lineY, lineH, effectiveScroll);
                continue;
            }

            if (vl.BlockIndex >= _blockSpellingErrors.Count) continue;
            var errors = _blockSpellingErrors[vl.BlockIndex];
            if (errors is null) continue;

            string blockText = _doc.GetBlockText(vl.BlockIndex);
            var parsed = _parsedBlocks![vl.BlockIndex];
            var map = IsVisual ? _visualMaps?[vl.BlockIndex] : null;
            int vlEnd = vl.StartOffset + vl.Length;

            foreach (var err in errors)
            {
                int errEnd = err.StartOffset + err.Length;
                if (err.StartOffset >= vlEnd || errEnd <= vl.StartOffset) continue;

                int hlStart = Math.Max(err.StartOffset, vl.StartOffset);
                int hlEnd = Math.Min(errEnd, vlEnd);

                double x1, x2;
                if (IsVisual && parsed.Table != null && parsed.TableRow != null)
                {
                    if (_tableColumnWidths.TryGetValue(parsed.Table, out var colWidths))
                    {
                        x1 = CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlStart);
                        x2 = CursorXInTableRow(vl.BlockIndex, parsed, colWidths, hlEnd);
                    }
                    else continue;
                }
                else
                {
                    x1 = MeasureRangeWidth(blockText, vl.StartOffset, hlStart - vl.StartOffset,
                        parsed.Runs, parsed.Kind, map);
                    x2 = MeasureRangeWidth(blockText, vl.StartOffset, hlEnd - vl.StartOffset,
                        parsed.Runs, parsed.Kind, map);

                    if (map?.ReplacementPrefix != null && vl.StartOffset == 0)
                    {
                        double prefixW = _measure.MeasureReplacementPrefix(
                            map.ReplacementPrefix!, map.PrefixMeasureKind);
                        x1 += prefixW;
                        x2 += prefixW;
                    }
                }

                double w = x2 - x1;
                if (w > 0)
                {
                    double baselineY = lineY - effectiveScroll + lineH - 2;
                    DrawSquigglyLine(dc, _padding + x1, _padding + x2, baselineY);
                }
            }
        }
    }

    private void DrawSpellingErrorsOnJoinedLine(DrawingContext dc, VisualLine vl,
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

                double x1 = MeasureJoinedRange(group, vlStart, hlStart - vlStart);
                double x2 = MeasureJoinedRange(group, vlStart, hlEnd - vlStart);

                double w = x2 - x1;
                if (w > 0)
                {
                    double baselineY = lineY - effectiveScroll + lineH - 2;
                    DrawSquigglyLine(dc, _padding + x1, _padding + x2, baselineY);
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

    private bool AddSpellCheckMenuItems(ContextMenu menu, Point position)
    {
        if (_spellCheckService is null || _blockSpellingErrors is null) return false;

        HitTestToPosition(position, out int blockIndex, out int charOffset);
        var error = FindSpellingErrorAt(blockIndex, charOffset);
        if (error is null) return false;

        var err = error.Value;
        var suggestions = _spellCheckService.Suggest(err.Word);

        if (suggestions.Count > 0)
        {
            foreach (var suggestion in suggestions)
            {
                var item = new MenuItem { Header = suggestion, FontWeight = FontWeights.Bold };
                ApplyMenuItemStyle(item);
                var capturedSuggestion = suggestion;
                var capturedBlock = blockIndex;
                var capturedErr = err;
                item.Click += (_, _) =>
                {
                    ReplaceWord(capturedBlock, capturedErr.StartOffset, capturedErr.Length, capturedSuggestion);
                    Focus();
                };
                menu.Items.Add(item);
            }
        }
        else
        {
            var noSuggestions = new MenuItem { Header = "(no suggestions)", IsEnabled = false };
            ApplyMenuItemStyle(noSuggestions);
            menu.Items.Add(noSuggestions);
        }

        menu.Items.Add(new Separator());

        var ignoreItem = new MenuItem { Header = "Ignore All" };
        ApplyMenuItemStyle(ignoreItem);
        var wordToIgnore = err.Word;
        ignoreItem.Click += (_, _) =>
        {
            _spellCheckService.IgnoreAll(wordToIgnore);
            RecheckAllBlocks();
            InvalidateVisual();
            Focus();
        };
        menu.Items.Add(ignoreItem);

        var addItem = new MenuItem { Header = "Add to Dictionary" };
        ApplyMenuItemStyle(addItem);
        var wordToAdd = err.Word;
        addItem.Click += (_, _) =>
        {
            _spellCheckService.AddToUserDictionary(wordToAdd);
            RecheckAllBlocks();
            InvalidateVisual();
            Focus();
        };
        menu.Items.Add(addItem);

        var addProjectItem = new MenuItem { Header = "Add to Project Dictionary" };
        ApplyMenuItemStyle(addProjectItem);
        var wordForProject = err.Word;
        addProjectItem.Click += (_, _) =>
        {
            _spellCheckService.AddToProjectDictionary(wordForProject);
            RecheckAllBlocks();
            InvalidateVisual();
            Focus();
        };
        menu.Items.Add(addProjectItem);

        return true;
    }

    private void ReplaceWord(int blockIndex, int offset, int length, string replacement)
    {
        _doc.BeginUndoGroup();
        _doc.RemoveTextAt(blockIndex, offset, length);
        _doc.InsertTextAt(blockIndex, offset, replacement);
        _doc.CursorBlock = blockIndex;
        _doc.CursorOffset = offset + replacement.Length;
        _doc.AnchorBlock = blockIndex;
        _doc.AnchorOffset = offset + replacement.Length;
        _doc.SealUndoGroup();
        InvalidateLayout();
        EnsureCursorVisible();
    }

    internal SpellCheckService? TestSpellCheckService => _spellCheckService;
    internal IReadOnlyList<SpellingError>? TestGetSpellingErrors(int blockIndex)
        => _blockSpellingErrors is not null && blockIndex < _blockSpellingErrors.Count
            ? _blockSpellingErrors[blockIndex]
            : null;
}
