using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RaisinDocs;

public partial class DocsCanvas
{
    private bool _spellCheckEnabled;
    private SpellCheckService? _spellCheckService;
    private readonly HashSet<int> _dirtySpellBlocks = new();
    private DispatcherTimer? _spellCheckTimer;
    private List<IReadOnlyList<SpellingError>?>? _blockSpellingErrors;
    private Pen? _spellErrorPen;

    public bool SpellCheckEnabled => _spellCheckEnabled;

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

    private void EnsureSpellCheckInitialized()
    {
        if (_spellCheckService is not null) return;

        _spellCheckService = new SpellCheckService();
        _spellCheckService.LoadEmbeddedDictionary();

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

        for (int i = 0; i < _doc.BlockCount; i++)
            _dirtySpellBlocks.Add(i);

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
            _blockSpellingErrors = new List<IReadOnlyList<SpellingError>?>(
                Enumerable.Repeat<IReadOnlyList<SpellingError>?>(null, _doc.BlockCount));
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

    internal SpellCheckService? TestSpellCheckService => _spellCheckService;
    internal IReadOnlyList<SpellingError>? TestGetSpellingErrors(int blockIndex)
        => _blockSpellingErrors is not null && blockIndex < _blockSpellingErrors.Count
            ? _blockSpellingErrors[blockIndex]
            : null;
}
