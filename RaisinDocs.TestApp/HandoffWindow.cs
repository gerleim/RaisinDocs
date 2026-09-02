using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using FactoryType = Vortice.Direct2D1.FactoryType;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;
using Color = System.Windows.Media.Color;

namespace RaisinDocs.TestApp;

/// <summary>
/// C3 of design/Scroll Frame Pacing.md: the handoff, on a real gesture.
/// </summary>
/// <remarks>
/// The static comparison showed the two renderers can agree on one screenful at offset zero.
/// That is necessary and not sufficient. A gesture hands over at whatever offset the scroll
/// happens to be at, so the agreement has to hold at every sub-line position, and the swap
/// itself has to happen without a blank or repeated frame.
///
/// This window does both halves:
///
///   - On startup it sweeps the scroll offset a pixel at a time across a line and differences
///     the two renderers at each, which is the objective half. A seam that only closes at
///     offset zero would show up here as a diff that rises and falls with the offset.
///   - Then it stays open and scrolls for real: the wheel starts a gesture, the presenter takes
///     over for the duration, and WPF gets it back when the coast ends. F9 disables the
///     handoff so the same gesture can be compared against plain WPF scrolling, which is the
///     subjective half and the one that actually decides it.
///
/// Order matters at both ends, because the child HWND is always on top of WPF content:
/// taking over means drawing a frame at the current offset and only then showing the surface;
/// handing back means moving WPF and letting it paint before the surface is hidden. Getting
/// either backwards shows one frame of the wrong thing.
/// </remarks>
public sealed class HandoffWindow : Window
{
    private const double FontSize = 16;
    private const string FontFamily = "Segoe UI";
    private const double LineHeight = 22;
    private const double PaddingX = 8;

    private static readonly Color BackColor = Color.FromRgb(0x1E, 0x1E, 0x1E);
    private static readonly Color TextColor = Color.FromRgb(0xDC, 0xDC, 0xDC);

    private static readonly string OutDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaisinDocs", "seam");

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RaisinDocs", "handoff.log");

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object LogLock = new();

    /// <summary>
    /// Appends a timestamped line. Three rounds of guessing at what causes a flash and a
    /// freeze have cost more than instrumenting would have; this is the instrument.
    /// </summary>
    internal static void Log(string message)
    {
        try
        {
            lock (LogLock)
                File.AppendAllText(LogPath,
                    $"{Clock.Elapsed.TotalSeconds,9:F3}  {message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }

    private readonly string[] _lines;
    private readonly WpfTextPanel _wpf;
    private readonly PresenterSurface _d2d;
    private readonly TextBlock _readout;

    /// <summary>Whether the presenter takes over at all. F9 toggles it, for the A/B.</summary>
    private bool _handoffEnabled = true;

    /// <summary>Scroll position while WPF owns it. The presenter owns it during a gesture.</summary>
    private double _offset;
    private double _velocity;
    private bool _presenting;
    private bool _handingBack;

    private const double WheelDamping = 10.0;
    private const double PixelsPerNotch = 120.0;

    /// <summary>Whether to run the offset sweep before handing the window over.</summary>
    private readonly bool _sweep;

    /// <summary>Drives its own gesture and watches its own pixels. See BackgroundTestAsync.</summary>
    private readonly bool _bgTest;

    /// <summary>
    /// The control for the background test: sample for the same duration but never engage the
    /// presenter. If the background brightens anyway, with the surface never shown, then the
    /// window is being covered by something else and none of the other samples mean anything.
    /// </summary>
    internal static bool NoGesture;

    public HandoffWindow(string[] lines, bool sweep, bool bgTest)
    {
        _lines = lines;
        _sweep = sweep;
        _bgTest = bgTest;
        Title = "Handoff A/B (C3) - wheel to scroll, F9 toggles the presenter";
        Width = 1000;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(BackColor);

        _wpf = new WpfTextPanel(lines);
        _d2d = new PresenterSurface(lines);
        _readout = new TextBlock
        {
            Foreground = Brushes.Gainsboro,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Margin = new Thickness(8, 4, 8, 4),
            Text = "presenter ON (F9 to toggle)",
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_readout, 0);
        Grid.SetRow(_wpf, 1);
        grid.Children.Add(_readout);
        grid.Children.Add(_wpf);
        Content = grid;

        // The presenter window is created once the WPF window has a handle to parent it to,
        // and follows the text panel's bounds from then on. It is not in the visual tree: a
        // hosted window makes WPF exclude that region from its own rendering, which is what
        // left a see-through hole when it was hidden.
        SourceInitialized += (_, _) =>
        {
            if (MonitorIndex >= 0)
            {
                PlaceOnMonitor(MonitorIndex);
                UpdateLayout();
            }

            var b = PanelBounds();
            _d2d.Create(new WindowInteropHelper(this).Handle, b.x, b.y, b.w, b.h);
        };
        _wpf.SizeChanged += (_, _) =>
        {
            var b = PanelBounds();
            _d2d.SetBounds(b.x, b.y, b.w, b.h);
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath, $"handoff log, {lines.Length} lines, {DateTime.Now:HH:mm:ss}"
                + Environment.NewLine);
        }
        catch (IOException) { }

        MouseWheel += OnWheel;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F9) return;
            _handoffEnabled = !_handoffEnabled;
            _readout.Text = _handoffEnabled
                ? "presenter ON  (F9 to toggle) - scrolling hands over to Direct2D"
                : "presenter OFF (F9 to toggle) - scrolling stays in WPF";
            e.Handled = true;
        };

        ContentRendered += async (_, _) =>
        {
            if (_bgTest)
            {
                CompositionTarget.Rendering += OnFrame;
                try { await BackgroundTestAsync(); }
                catch (Exception ex) { Log($"BG TEST FAILED: {ex.GetType().Name}: {ex.Message}"); }
                Close();
                return;
            }

            if (!_sweep)
            {
                // Straight to the A/B. The sweep depends on screen capture, which depends on
                // nothing covering the window - not a thing to make someone wait through.
                CompositionTarget.Rendering += OnFrame;
                return;
            }

            try { await SweepAsync(); }
            catch (Exception ex)
            {
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(Path.Combine(OutDir, "handoff.txt"),
                    $"FAILED: {ex.GetType().Name}: {ex.Message}"
                    + Environment.NewLine + Environment.NewLine + ex.StackTrace);
            }
        };

        Closed += (_, _) => _d2d.Stop();
    }

    /// <summary>Debug hook: paint the presenter background magenta. See DebugMagenta.</summary>
    internal static void SetDebugMagenta() => PresenterSurface.DebugMagenta = true;

    public static HandoffWindow Open(string? file, bool sweep, bool bgTest)
    {
        string path = file ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StockRaisin2", "reports", "2026-08-31", "SR_report_2026-08-31_DUH768767.md");

        string[] lines = (File.Exists(path)
                ? File.ReadAllLines(path)
                : Enumerable.Range(0, 2000)
                    .Select(i => $"Line {i}: the quick brown fox jumps over the lazy dog.")
                    .ToArray())
            .Where(l => l.Trim().Length > 0)
            .ToArray();

        return new HandoffWindow(lines, sweep, bgTest);
    }

    // --- the gesture ----------------------------------------------------------------------

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        double notches = -e.Delta / 120.0;

        if (!_handoffEnabled)
        {
            _velocity += notches * PixelsPerNotch * WheelDamping;
            return;
        }

        Log($"wheel {notches:F0}, presenting={_presenting}");

        if (!_presenting) TakeOver();
        _d2d.Wheel(notches);
    }

    /// <summary>
    /// Draw at the current offset first, show second. The surface is a child window and sits
    /// over the WPF content, so showing it before it holds the right pixels would put one
    /// frame of stale content - or an empty buffer - on screen.
    /// </summary>
    /// <summary>
    /// Shows the surface. Nothing is waited for: the presenter has been drawing this very
    /// offset all along, so its buffer is already right. Waiting for frames here put up to
    /// 26ms of UI thread into the start of every gesture, which is what froze the scroll.
    /// </summary>
    private void TakeOver()
    {
        _presenting = true;
        _handingBack = false;
        _d2d.Show();
        Log($"TAKE OVER at offset {_offset:F1}");
    }

    /// <summary>
    /// Move WPF first, let it paint, hide the surface last - the mirror of taking over. WPF
    /// paints on its own schedule, so this waits a render pass rather than assuming.
    /// </summary>
    private void HandBack()
    {
        // _presenting has to be cleared here, not in the callback below. It is the flag that
        // stops OnFrame calling this again, and OnFrame runs every frame: leaving it set until
        // an asynchronous callback ran queued one hand-back per frame, each re-invalidating WPF
        // and re-hiding the surface.
        if (_handingBack) return;
        _handingBack = true;
        _presenting = false;

        double was = _wpf.Offset;
        _offset = _d2d.Offset;
        _wpf.Offset = _offset;
        _wpf.InvalidateVisual();
        _handBackTicks = 0;
        _handBackClock = Stopwatch.StartNew();
        Log($"HAND BACK at offset {_offset:F1} (WPF was showing {was:F1}, " +
            $"{Math.Abs(_offset - was):F0}px stale)");
    }

    /// <summary>
    /// Render ticks counted since a hand-back, before the surface is allowed to go.
    /// </summary>
    /// <remarks>
    /// WPF is not updated during a gesture, so when the presenter finishes it is still showing
    /// wherever the scroll began - up to a whole coast away. Setting its offset and then hiding
    /// the surface on a queued callback uncovered it before its new frame had reached the
    /// screen, so the text jumped back to the old position for a few frames.
    ///
    /// CompositionTarget.Rendering fires before each render pass, so counting a few of those
    /// waits for actual frames, rather than for a dispatcher priority that says nothing about
    /// what has been presented.
    /// </remarks>
    private int _handBackTicks;
    private Stopwatch? _handBackClock;
    private const int HandBackTicks = 3;

    /// <summary>WPF's own scrolling, used when the presenter is switched off.</summary>
    private void OnFrame(object? sender, EventArgs e)
    {
        if (_presenting)
        {
            if (_d2d.IsIdle) HandBack();
            return;
        }

        if (_handingBack)
        {
            // The surface keeps covering WPF until WPF has genuinely drawn the new offset.
            if (++_handBackTicks >= HandBackTicks)
            {
                _d2d.Hide();
                _handingBack = false;
                Log($"  surface hidden after {_handBackTicks} render ticks, " +
                    $"{_handBackClock?.Elapsed.TotalMilliseconds ?? 0:F1}ms");
            }
            return;
        }

        // While WPF owns the scroll the presenter shadows it, so its buffer is always ready to
        // be shown without a stall or a stale frame.
        _d2d.SetOffset(_offset);

        if (Math.Abs(_velocity) < 0.5) { _velocity = 0; return; }

        double dt = 1.0 / 144;
        double decay = Math.Exp(-dt * WheelDamping);
        _offset += _velocity * (1 - decay) / WheelDamping;
        _velocity *= decay;
        _offset = Math.Max(0, Math.Min(_offset, _lines.Length * LineHeight - _wpf.ActualHeight));

        _wpf.Offset = _offset;
        _wpf.InvalidateVisual();
    }

    // --- watching its own pixels -----------------------------------------------------------

    /// <summary>
    /// Drives its own gesture and samples its own background, so the flash can be caught
    /// without anyone having to see it.
    /// </summary>
    /// <remarks>
    /// A thin strip down the right edge is sampled every few milliseconds. Text is lighter
    /// than the background, so the darkest pixel in the strip is the background whatever the
    /// text is doing, and a flash shows as that darkest value rising above the theme colour.
    ///
    /// Each sample is recorded against the state at the time - which renderer should be on
    /// screen, and whether a handoff is in progress - so a spike can be attributed instead of
    /// guessed at.
    /// </remarks>
    private async Task BackgroundTestAsync()
    {
        Topmost = true;
        Activate();
        await Task.Delay(600);

        Log($"BG TEST start, theme background is {BackColor.R},{BackColor.G},{BackColor.B}, " +
            $"magenta={PresenterSurface.DebugMagenta}, noGesture={NoGesture}");

        var samples = new List<(double t, int min, string state)>(4000);
        var clock = Stopwatch.StartNew();
        double nextNotch = 0.4;

        while (clock.Elapsed.TotalSeconds < 14)
        {
            double t = clock.Elapsed.TotalSeconds;

            // A notch every 1.6s: long enough for a coast to finish and hand back, so both
            // ends of a gesture are covered repeatedly.
            if (t >= nextNotch && !NoGesture)
            {
                nextNotch += 1.6;
                Log($"  [test] injecting notch at {t:F3}, presenting={_presenting}");
                if (!_presenting) TakeOver();
                _d2d.Wheel(1);
            }

            int min = SampleRightEdgeMinimum();

            if (min > BackColor.R + 12 && _ownerLogs < 6)
            {
                _ownerLogs++;
                var (sx, sy) = SamplePointScreen();
                Log($"  [test] bright min={min} state={(_presenting ? "D2D" : "WPF")} " +
                    $"owner: {WindowAtSamplePoint(sx, sy)}");
            }
            string state = _presenting ? (_handingBack ? "handing-back" : "D2D")
                                       : (_d2d.IsShown ? "D2D-still-shown" : "WPF");

            // Save the first bright frame, and one quiet one to compare it against. A
            // percentage cannot say whether the window is flashing or whether the sampler is
            // reading the wrong pixels; a picture can.
            if (min > BackColor.R + 12 && state == "D2D" && !_savedFlash)
            {
                _savedFlash = true;
                SaveFullCapture($"flash_D2D_t{t:F3}_min{min}");
                Log($"  [test] saved D2D flash capture at t={t:F3}, min={min}");
            }
            else if (min > BackColor.R + 12 && state == "WPF" && !_savedWpfFlash)
            {
                _savedWpfFlash = true;
                SaveFullCapture($"flash_WPF_t{t:F3}_min{min}");
                Log($"  [test] saved WPF flash capture at t={t:F3}, min={min}");
            }
            else if (min <= BackColor.R + 12 && !_savedQuiet && t > 2)
            {
                _savedQuiet = true;
                SaveFullCapture($"quiet_t{t:F3}_min{min}");
                Log($"  [test] saved quiet capture at t={t:F3}, min={min}");
            }
            samples.Add((t, min, state));

            await Task.Delay(8);
        }

        // Report: anything meaningfully lighter than the theme background is the flash.
        int threshold = BackColor.R + 12;
        var flashes = samples.Where(x => x.min > threshold).ToList();

        Log($"BG TEST done: {samples.Count} samples, {flashes.Count} above {threshold}");

        if (flashes.Count == 0)
        {
            Log("  no flash observed");
        }
        else
        {
            foreach (var g in flashes.GroupBy(x => x.state))
                Log($"  state {g.Key,-16} {g.Count(),4} samples, " +
                    $"brightest {g.Max(x => x.min)}, median {g.OrderBy(x => x.min).ElementAt(g.Count() / 2).min}");

            Log("  first 25 flash samples:");
            foreach (var f in flashes.Take(25))
                Log($"    t={f.t,7:F3}  min={f.min,4}  state={f.state}");
        }

        // And the quiet baseline, to prove the sampling is reading our window at all.
        var quiet = samples.Where(x => x.min <= threshold).ToList();
        if (quiet.Count > 0)
            Log($"  baseline: {quiet.Count} samples, darkest {quiet.Min(x => x.min)}, " +
                $"brightest {quiet.Max(x => x.min)}");
    }

    private bool _savedFlash, _savedWpfFlash, _savedQuiet;
    private int _ownerLogs;

    /// <summary>The whole client area, saved so it can be looked at.</summary>
    private void SaveFullCapture(string name)
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            using var shot = Capture();
            shot.Save(Path.Combine(OutDir, name + ".png"),
                System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex) { Log($"  [test] capture save failed: {ex.Message}"); }
    }

    /// <summary>Screen coordinates of the middle of the sampled strip.</summary>
    private (int x, int y) SamplePointScreen()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        GetClientRect(hwnd, out RECT r);
        var origin = new POINT { X = r.Left, Y = r.Top };
        ClientToScreen(hwnd, ref origin);
        return (origin.X + (r.Right - r.Left) - 5, origin.Y + (r.Bottom - r.Top) / 2);
    }

    /// <summary>Darkest pixel in a strip down the right edge of the scrolling area.</summary>
    private int SampleRightEdgeMinimum()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        GetClientRect(hwnd, out RECT r);
        var origin = new POINT { X = r.Left, Y = r.Top };
        ClientToScreen(hwnd, ref origin);

        var top = _wpf.TranslatePoint(new Point(0, 0), this);
        int skip = (int)Math.Round(top.Y) + 40;        // below the "WPF"/"D2D" tag
        int w = 6;
        int h = r.Bottom - r.Top - skip - 4;
        if (h <= 0) return 255;

        using var bmp = new System.Drawing.Bitmap(w, h,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(origin.X + (r.Right - r.Left) - w - 2, origin.Y + skip, 0, 0,
                new System.Drawing.Size(w, h), System.Drawing.CopyPixelOperation.SourceCopy);

        int min = 255;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var px = bmp.GetPixel(x, y);
            int v = Math.Max(px.R, Math.Max(px.G, px.B));
            if (v < min) min = v;
        }
        return min;
    }

    // --- the objective half ---------------------------------------------------------------

    /// <summary>
    /// Differences the two renderers at every sub-line scroll offset. Agreement at offset zero
    /// says nothing about a gesture, which hands over wherever it happens to be.
    /// </summary>
    private async Task SweepAsync()
    {
        // CopyFromScreen reads the desktop, so it returns whatever is on screen at those
        // coordinates - including another window sitting over this one. Without this the sweep
        // silently measures the wrong pixels, which it did: backgrounds came back as 102,98,97
        // and 63,62,62 where the theme is 30,30,30.
        Topmost = true;
        Activate();
        await Task.Delay(300);

        Directory.CreateDirectory(OutDir);
        var report = new System.Text.StringBuilder();
        report.AppendLine($"handoff sweep, line height {LineHeight}, {FontFamily} {FontSize}");
        report.AppendLine();
        report.AppendLine("offset   mean abs   >8      >64    max");

        double worstMean = 0, worstOver8 = 0;
        int worstOffset = 0;

        for (int off = 0; off < (int)LineHeight + 2; off++)
        {
            _wpf.Offset = off;
            _wpf.InvalidateVisual();
            _d2d.SetOffset(off);

            _d2d.Hide();
            using var wpf = await CaptureWhenVisibleAsync();

            _d2d.Show();
            using var d2d = await CaptureWhenVisibleAsync();

            // Keep one pair mid-line, so the residual can be looked at rather than inferred
            // from a percentage.
            if (off == 11)
            {
                wpf.Save(Path.Combine(OutDir, "handoff_wpf.png"), System.Drawing.Imaging.ImageFormat.Png);
                d2d.Save(Path.Combine(OutDir, "handoff_d2d.png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            var m = Measure(wpf, d2d);

            // If the captured background is not the theme colour, something was over the
            // window and the numbers mean nothing. Say so rather than reporting them.
            var bg = wpf.GetPixel(wpf.Width - 12, wpf.Height / 2);
            bool occluded = Math.Abs(bg.R - BackColor.R) > 6 || Math.Abs(bg.G - BackColor.G) > 6;
            if (occluded) report.AppendLine($"{off,5}   OCCLUDED - captured background " +
                                            $"{bg.R},{bg.G},{bg.B}, expected " +
                                            $"{BackColor.R},{BackColor.G},{BackColor.B}");
            if (!occluded)
                report.AppendLine($"{off,5}   {m.meanAbs,8:F2}   {m.over8,5:F2}%  {m.over64,5:F2}%  {m.max,4}");

            if (!occluded && m.meanAbs > worstMean)
            {
                worstMean = m.meanAbs;
                worstOver8 = m.over8;
                worstOffset = off;
            }
        }

        report.AppendLine();
        report.AppendLine($"worst offset {worstOffset}: mean {worstMean:F2}, {worstOver8:F2}% differing by >8");
        report.AppendLine("(offset 0 alone measured mean 0.54 / 3.16% in the static comparison)");

        _d2d.Hide();
        _wpf.Offset = 0;
        _offset = 0;
        _wpf.InvalidateVisual();
        Topmost = false;

        File.WriteAllText(Path.Combine(OutDir, "handoff.txt"), report.ToString());
        _readout.Text = $"sweep done - worst offset {worstOffset}, {worstOver8:F2}% differing. " +
                        "Wheel to scroll, F9 toggles the presenter.";

        // Only now: while it is subscribed, nothing below Render priority gets a turn.
        CompositionTarget.Rendering += OnFrame;
    }

    /// <summary>
    /// Waits for the change to reach the screen. A plain delay rather than an idle-priority
    /// yield: the capture reads the composed desktop, so what matters is that DWM has
    /// presented, and idle priority is starved by anything animating.
    /// </summary>
    /// <summary>The text panel's area in client pixels, where the presenter window goes.</summary>
    private (int x, int y, int w, int h) PanelBounds()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var origin = _wpf.TranslatePoint(new Point(0, 0), this);
        return ((int)Math.Round(origin.X * dpi.DpiScaleX),
                (int)Math.Round(origin.Y * dpi.DpiScaleY),
                (int)Math.Round(_wpf.ActualWidth * dpi.DpiScaleX),
                (int)Math.Round(_wpf.ActualHeight * dpi.DpiScaleY));
    }

    private static async Task SettleAsync() => await Task.Delay(200);

    /// <summary>
    /// Captures, and retries while something is covering the window. The desktop is shared
    /// with whatever else is running, so a capture is only worth keeping once the background
    /// reads as the theme colour.
    /// </summary>
    private async Task<System.Drawing.Bitmap> CaptureWhenVisibleAsync()
    {
        System.Drawing.Bitmap? shot = null;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (attempt > 0)
            {
                Topmost = false;
                Topmost = true;
                Activate();
            }

            await SettleAsync();
            shot?.Dispose();
            shot = Capture();

            var bg = shot.GetPixel(shot.Width - 12, shot.Height / 2);
            if (Math.Abs(bg.R - BackColor.R) <= 6 && Math.Abs(bg.G - BackColor.G) <= 6)
                return shot;
        }

        return shot!;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);

    /// <summary>Which monitor to sit on. Only one display here is HDR, so this isolates it.</summary>
    internal static int MonitorIndex = -1;

    private static List<RECT> Monitors()
    {
        var found = new List<RECT>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr m, IntPtr dc, ref RECT r, IntPtr d) => { found.Add(r); return true; },
            IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// Places the window on a chosen monitor, in physical pixels. SetWindowPos rather than
    /// Left/Top, so no device-independent conversion is involved and per-monitor DPI cannot
    /// land the window somewhere other than asked for.
    /// </summary>
    private void PlaceOnMonitor(int index)
    {
        var all = Monitors();
        Log($"monitors: {all.Count}");
        for (int i = 0; i < all.Count; i++)
            Log($"  [{i}] {all[i].Left},{all[i].Top} .. {all[i].Right},{all[i].Bottom}");

        if (index < 0 || index >= all.Count) return;

        var r = all[index];
        int w = Math.Min(1000, r.Right - r.Left - 80);
        int h = Math.Min(700, r.Bottom - r.Top - 80);
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
            r.Left + 40, r.Top + 40, w, h, 0x0004 /* SWP_NOZORDER */);
        Log($"placed on monitor {index} at {r.Left + 40},{r.Top + 40} size {w}x{h}");
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, System.Text.StringBuilder name, int max);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    /// <summary>
    /// Which window actually owns the pixel being sampled. The alternative to knowing this is
    /// another round of theories about why our window looks wrong, when it may not be our
    /// window at all.
    /// </summary>
    private string WindowAtSamplePoint(int screenX, int screenY)
    {
        IntPtr at = WindowFromPoint(new POINT { X = screenX, Y = screenY });
        IntPtr root = GetAncestor(at, 2);                 // GA_ROOT
        IntPtr mine = new WindowInteropHelper(this).Handle;

        var cls = new System.Text.StringBuilder(128);
        GetClassNameW(at, cls, cls.Capacity);
        GetWindowThreadProcessId(at, out uint pid);

        string who;
        try { who = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
        catch { who = "?"; }

        return $"hwnd={at} root={root} mine={mine} same={(root == mine)} class={cls} proc={who}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    /// <summary>The scrolling area only, so the readout above it is not compared.</summary>
    private System.Drawing.Bitmap Capture()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        GetClientRect(hwnd, out RECT r);
        var origin = new POINT { X = r.Left, Y = r.Top };
        ClientToScreen(hwnd, ref origin);

        var top = _wpf.TranslatePoint(new Point(0, 0), this);
        int skip = (int)Math.Round(top.Y);

        int w = r.Right - r.Left, h = r.Bottom - r.Top - skip;
        var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen(origin.X, origin.Y + skip, 0, 0, new System.Drawing.Size(w, h),
            System.Drawing.CopyPixelOperation.SourceCopy);
        return bmp;
    }

    private static (double meanAbs, double over8, double over64, int max) Measure(
        System.Drawing.Bitmap a, System.Drawing.Bitmap b)
    {
        int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
        byte[] pa = ToBytes(a, w, h), pb = ToBytes(b, w, h);

        long sum = 0, channels = 0, over8 = 0, over64 = 0, pixels = 0;
        int max = 0;

        for (int y = 4; y < h - 4; y++)
        for (int x = 4; x < w - 4; x++)
        {
            int i = (y * w + x) * 4;
            int dr = Math.Abs(pa[i] - pb[i]);
            int dg = Math.Abs(pa[i + 1] - pb[i + 1]);
            int db = Math.Abs(pa[i + 2] - pb[i + 2]);
            int d = Math.Max(dr, Math.Max(dg, db));

            sum += dr + dg + db;
            channels += 3;
            pixels++;
            if (d > 8) over8++;
            if (d > 64) over64++;
            if (d > max) max = d;
        }

        return ((double)sum / channels, 100.0 * over8 / pixels, 100.0 * over64 / pixels, max);
    }

    private static byte[] ToBytes(System.Drawing.Bitmap bmp, int w, int h)
    {
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * w * 4, w * 4);
        bmp.UnlockBits(data);
        return bytes;
    }

    // --- the two renderers -----------------------------------------------------------------

    private sealed class WpfTextPanel : FrameworkElement
    {
        private readonly string[] _lines;
        public double Offset { get; set; }

        public WpfTextPanel(string[] lines) => _lines = lines;

        protected override void OnRender(DrawingContext dc)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            dc.DrawRectangle(new SolidColorBrush(BackColor), null,
                new System.Windows.Rect(0, 0, ActualWidth, ActualHeight));

            var typeface = new Typeface(FontFamily);
            var brush = new SolidColorBrush(TextColor);

            int first = Math.Max(0, (int)(Offset / LineHeight));
            int last = Math.Min(_lines.Length - 1, (int)((Offset + ActualHeight) / LineHeight));

            for (int i = first; i <= last; i++)
            {
                var ft = new FormattedText(_lines[i], CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight, typeface, FontSize, brush, dpi);
                dc.DrawText(ft, new Point(PaddingX, Math.Round(i * LineHeight - Offset)));
            }

            // Which renderer is on screen, so a flash can be attributed to one of them by
            // looking rather than by theory.
            var tag = new FormattedText("WPF", CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight, typeface, 20,
                new SolidColorBrush(Color.FromRgb(0xFF, 0x60, 0x60)), dpi);
            dc.DrawText(tag, new Point(ActualWidth - 60, 6));
        }
    }

    /// <summary>The paced Direct2D presenter, settled to the parameters the seam sweep found.</summary>
    /// <remarks>
    /// Deliberately not an HwndHost. WPF excludes a hosted window's region from its own
    /// rendering for as long as the host is in the visual tree, so hiding the child left a hole
    /// showing whatever was behind the window - which is the background "getting brighter".
    /// Measured: with the presenter never shown, 907 samples of the background all read the
    /// theme colour exactly; once it had been shown and hidden, over a hundred read between 68
    /// and 163.
    ///
    /// Parenting the child window directly means WPF knows nothing about it. WPF renders its
    /// whole client area as normal, and this window simply covers part of it while shown.
    /// </remarks>
    private sealed class PresenterSurface
    {
        private const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000;

        /// <summary>
        /// Paints the presenter background a colour nothing else could be, so that a captured
        /// background either is that colour - the surface is opaque and the brightness comes
        /// from somewhere else - or is a blend of it, which proves the surface is translucent.
        /// </summary>
        internal static bool DebugMagenta;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name,
            int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint ms);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassExW(ref WNDCLASSEX cls);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string? name);

        private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public int style;
            [MarshalAs(UnmanagedType.FunctionPtr)] public WndProc lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        // Held for the lifetime of the process: the class keeps a pointer to this delegate, and
        // letting it be collected would leave the window calling into freed memory.
        private static WndProc? _wndProc;
        private static string? _className;

        /// <summary>
        /// A window class with no background brush.
        /// </summary>
        /// <remarks>
        /// The predefined "static" class carries a light background brush, so every time the
        /// surface was shown Windows painted that area pale before Direct2D drew anything -
        /// which is visible as the background flashing bright at the end of a gesture. With a
        /// null brush nothing is erased and the swapchain's own pixels are all that appear.
        /// </remarks>
        private static string EnsureWindowClass()
        {
            if (_className != null) return _className;

            _wndProc = DefWindowProcW;
            var cls = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                hInstance = GetModuleHandleW(null),
                hbrBackground = IntPtr.Zero,
                lpszClassName = "RaisinDocsPresenterSurface",
            };

            RegisterClassExW(ref cls);
            return _className = cls.lpszClassName;
        }

        private readonly string[] _lines;
        private readonly Dictionary<int, IDWriteTextLayout> _layouts = new();

        private IntPtr _hwnd;
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGISwapChain2? _swapChain;
        private ID2D1Factory1? _d2dFactory;
        private ID2D1Device? _d2dDevice;
        private ID2D1DeviceContext? _d2d;
        private ID2D1Bitmap1? _target;
        private IDWriteFactory? _dwrite;
        private IDWriteTextFormat? _format;
        private ID2D1SolidColorBrush? _brush;
        private ID2D1SolidColorBrush? _tagBrush;
        private IDWriteTextFormat? _tagFormat;

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        private Thread? _thread;
        private volatile bool _running;
        private long _frames;
        private int _bufferWidth, _bufferHeight;
        private SharpGen.Runtime.Result _lastPresent;
        private double _offset;
        private double _velocity;
        private int _wheelPending;

        public PresenterSurface(string[] lines) => _lines = lines;

        public double Offset => Volatile.Read(ref _offset);
        /// <summary>
        /// Idle means nothing left to scroll - including input that has arrived but which the
        /// render thread has not applied yet. Without the pending check the presenter looks
        /// idle in the moment between a wheel notch being queued and the next frame consuming
        /// it, so the handoff unwound a millisecond after it was set up, on every notch.
        /// </summary>
        public bool IsIdle => Volatile.Read(ref _wheelPending) == 0
                              && Math.Abs(Volatile.Read(ref _velocity)) < 0.5;

        public void SetOffset(double offset)
        {
            Volatile.Write(ref _offset, offset);
            Volatile.Write(ref _velocity, 0);
        }

        public void Wheel(double notches) => Interlocked.Add(ref _wheelPending, (int)notches);

        /// <summary>Blocks until the surface has drawn a frame, so it is safe to show.</summary>
        public long WaitForFrame()
        {
            // Bounded tightly: while the surface is hidden its swapchain may be occluded and
            // stop advancing altogether, and this runs on the UI thread. Showing one stale
            // frame is a far smaller fault than freezing the gesture that is starting.
            long start = Interlocked.Read(ref _frames);
            var sw = Stopwatch.StartNew();
            while (Interlocked.Read(ref _frames) <= start + 1 && sw.ElapsedMilliseconds < 25)
                Thread.Sleep(1);
            return Interlocked.Read(ref _frames) - start;
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);

        private const int SW_HIDE = 0, SW_SHOWNA = 8;

        public bool IsShown { get; private set; }

        /// <summary>Creates the child window, hidden, and starts drawing into it.</summary>
        public void Create(IntPtr parent, int x, int y, int w, int h)
        {
            // Created without WS_VISIBLE: it must not appear until the first gesture.
            _hwnd = CreateWindowExW(0, EnsureWindowClass(), null, WS_CHILD,
                x, y, Math.Max(1, w), Math.Max(1, h), parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "handoff-presenter" };
            _thread.Start();
        }

        public void SetBounds(int x, int y, int w, int h)
        {
            if (_hwnd != IntPtr.Zero && w > 0 && h > 0) MoveWindow(_hwnd, x, y, w, h, false);
        }

        /// <summary>SW_SHOWNA: shown without taking activation away from the WPF window.</summary>
        public void Show()
        {
            if (_hwnd == IntPtr.Zero || IsShown) return;
            ShowWindow(_hwnd, SW_SHOWNA);
            IsShown = true;
        }

        public void Hide()
        {
            if (_hwnd == IntPtr.Zero || !IsShown) return;
            ShowWindow(_hwnd, SW_HIDE);
            IsShown = false;
        }

        public void Destroy()
        {
            Stop();
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join(500);
            foreach (var l in _layouts.Values) l.Dispose();
            _layouts.Clear();
            _tagBrush?.Dispose(); _tagFormat?.Dispose();
            _brush?.Dispose(); _format?.Dispose(); _dwrite?.Dispose();
            _target?.Dispose(); _d2d?.Dispose(); _d2dDevice?.Dispose(); _d2dFactory?.Dispose();
            _swapChain?.Dispose(); _context?.Dispose(); _device?.Dispose();
            _device = null;
        }

        private void Loop()
        {
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                levels, out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
            _device = device;
            _context = context;

            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            using var factory = adapter.GetParent<IDXGIFactory2>();

            using (var sc1 = factory.CreateSwapChainForHwnd(_device, _hwnd, new SwapChainDescription1
            {
                Width = 0,
                Height = 0,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipSequential,
                // Opaque, explicitly. The D2D target bitmap is created AlphaMode.Ignore so
                // ClearType works, but the swapchain defaults to unspecified alpha - so its
                // alpha channel was undefined and DWM composited the surface with whatever was
                // behind it. That is the washed out, tinted background: the desktop showing
                // through the presenter.
                AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
                Flags = SwapChainFlags.FrameLatencyWaitableObject,
            }))
            {
                _swapChain = sc1.QueryInterface<IDXGISwapChain2>();
            }
            _swapChain.MaximumFrameLatency = 1;

            // What colour space is the display actually in? If it is one of the HDR ones, the
            // system is tone mapping our SDR pixels and the brightness is not ours to fix in
            // the renderer.
            try
            {
                using var output = _swapChain.GetContainingOutput();
                using var output6 = output.QueryInterface<IDXGIOutput6>();
                var d = output6.Description1;
                HandoffWindow.Log($"  [d2d] display colour space {d.ColorSpace}, " +
                                  $"bits {d.BitsPerColor}, " +
                                  $"maxLuminance {d.MaxLuminance:F0}, " +
                                  $"minLuminance {d.MinLuminance:F4}");
            }
            catch (Exception ex)
            {
                HandoffWindow.Log($"  [d2d] output description unavailable: {ex.Message}");
            }

            // Declare SDR sRGB explicitly. Without it the composition of a DXGI swapchain can
            // be treated as an unspecified colour space and tone mapped on an HDR capable
            // display, which lifts midtones across the whole window - the background reading 71
            // and 94 where the theme colour is 30, with pure black and white left untouched
            // because they are fixed points of the transform.
            try
            {
                using var sc3 = _swapChain.QueryInterface<IDXGISwapChain3>();
                var space = ColorSpaceType.RgbFullG22NoneP709;
                if (sc3.CheckColorSpaceSupport(space).HasFlag(SwapChainColorSpaceSupportFlags.Present))
                {
                    sc3.SetColorSpace1(space);
                    HandoffWindow.Log("  [d2d] colour space set to RgbFullG22NoneP709 (SDR sRGB)");
                }
                else
                {
                    HandoffWindow.Log("  [d2d] RgbFullG22NoneP709 not supported for present");
                }
            }
            catch (Exception ex)
            {
                HandoffWindow.Log($"  [d2d] colour space not set: {ex.GetType().Name}: {ex.Message}");
            }

            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
            _d2d = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

            _dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();
            _format = _dwrite.CreateTextFormat(FontFamily,
                Vortice.DirectWrite.FontWeight.Normal,
                Vortice.DirectWrite.FontStyle.Normal,
                Vortice.DirectWrite.FontStretch.Normal,
                (float)FontSize);

            CreateTarget();
            _d2d.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Cleartype;

            // The settings the seam sweep landed on: mean difference 0.54 of 255 against WPF.
            _d2d.TextRenderingParams = _dwrite.CreateCustomRenderingParams(
                2.2f, 0f, 1.0f, Vortice.DirectWrite.PixelGeometry.Rgb,
                Vortice.DirectWrite.RenderingMode.Natural);

            _brush = _d2d.CreateSolidColorBrush(ToColor4(TextColor));
            _tagBrush = _d2d.CreateSolidColorBrush(new Color4(0.38f, 1f, 0.45f, 1f));
            _tagFormat = _dwrite.CreateTextFormat(FontFamily,
                Vortice.DirectWrite.FontWeight.Normal,
                Vortice.DirectWrite.FontStyle.Normal,
                Vortice.DirectWrite.FontStretch.Normal, 20f);

            var waitable = _swapChain.FrameLatencyWaitableObject;
            var clock = Stopwatch.StartNew();
            long last = clock.ElapsedTicks;

            while (_running)
            {
                // A short timeout, so a hidden or occluded swapchain cannot park this thread:
                // it has to keep drawing at the current offset, ready to be shown.
                if (waitable != IntPtr.Zero) WaitForSingleObject(waitable, 100);

                long now = clock.ElapsedTicks;
                double dt = (now - last) / (double)Stopwatch.Frequency;
                last = now;
                if (dt > 0.05) dt = 0.05;

                EnsureBufferSize();
                Advance(dt);
                Draw();

                var result = _swapChain.Present(1, PresentFlags.None);
                if (result != _lastPresent)
                {
                    HandoffWindow.Log($"  [d2d] Present -> {result} (was {_lastPresent}), " +
                                      $"frame {_frames}");
                    _lastPresent = result;
                }

                Interlocked.Increment(ref _frames);

                double frameMs = (clock.ElapsedTicks - now) / (double)Stopwatch.Frequency * 1000;
                if (frameMs > 20)
                    HandoffWindow.Log($"  [d2d] slow frame {frameMs:F1}ms at offset {_offset:F1}");
            }
        }

        /// <summary>
        /// Keeps the swapchain the size of its window. The surface starts collapsed, so its
        /// HWND is created at a placeholder size and the swapchain is built to match; without
        /// this, showing it later stretches that buffer over the real area and the presenter
        /// draws something quite different from what WPF drew.
        /// </summary>
        private void EnsureBufferSize()
        {
            GetClientRect(_hwnd, out RECT rc);
            int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
            if (w <= 0 || h <= 0 || (w == _bufferWidth && h == _bufferHeight)) return;

            // The target holds a reference to the back buffer, so it has to go first.
            _d2d!.Target = null;
            _target?.Dispose();
            _target = null;

            HandoffWindow.Log($"  [d2d] resize {_bufferWidth}x{_bufferHeight} -> {w}x{h}");
            _swapChain!.ResizeBuffers(0, (uint)w, (uint)h, Format.Unknown,
                SwapChainFlags.FrameLatencyWaitableObject);

            CreateTarget();
            _bufferWidth = w;
            _bufferHeight = h;
        }

        private void CreateTarget()
        {
            using var surface = _swapChain!.GetBuffer<IDXGISurface>(0);
            // AlphaMode.Ignore: ClearType needs an opaque target, and without it Direct2D
            // falls back to greyscale and disagrees with WPF on every glyph edge.
            _target = _d2d!.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
                new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm,
                    Vortice.DCommon.AlphaMode.Ignore),
                96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
            _d2d.Target = _target;
        }

        private void Advance(double dt)
        {
            int notches = Interlocked.Exchange(ref _wheelPending, 0);
            double v = Volatile.Read(ref _velocity);
            if (notches != 0)
            {
                v += notches * PixelsPerNotch * WheelDamping;
                // Immediately, not at the end of this method: between the exchange above and
                // the write below, the surface would otherwise read as idle.
                Volatile.Write(ref _velocity, v);
            }

            double offset = Volatile.Read(ref _offset);
            if (Math.Abs(v) > 0.5)
            {
                double decay = Math.Exp(-dt * WheelDamping);
                offset += v * (1 - decay) / WheelDamping;
                v *= decay;
            }
            else v = 0;

            double max = Math.Max(0, _lines.Length * LineHeight - _d2d!.Size.Height);
            Volatile.Write(ref _offset, Math.Clamp(offset, 0, max));
            Volatile.Write(ref _velocity, v);
        }

        private void Draw()
        {
            var size = _d2d!.Size;
            double offset = Volatile.Read(ref _offset);

            _d2d.BeginDraw();
            _d2d.Clear(DebugMagenta ? new Color4(1f, 0f, 1f, 1f) : ToColor4(BackColor));

            int first = Math.Max(0, (int)(offset / LineHeight));
            int last = Math.Min(_lines.Length - 1, (int)((offset + size.Height) / LineHeight));

            for (int i = first; i <= last; i++)
            {
                if (!_layouts.TryGetValue(i, out var layout))
                {
                    // No wrapping: DirectWrite wraps inside a text layout by default, but
                    // the canvas wraps in LayoutEngine and hands the renderer visual lines that
                    // are already final. Wrapping again here re-flows text WPF left alone, and
                    // the two modes then disagree about where lines break.
                    layout = _dwrite!.CreateTextLayout(
                        _lines[i], _format!, size.Width - (float)PaddingX * 2, (float)LineHeight);
                    layout.WordWrapping = Vortice.DirectWrite.WordWrapping.NoWrap;
                    _layouts[i] = layout;
                }

                _d2d.DrawTextLayout(
                    new System.Numerics.Vector2((float)PaddingX,
                        (float)Math.Round(i * LineHeight - offset)),
                    layout, _brush!);
            }

            using (var tag = _dwrite!.CreateTextLayout("D2D", _tagFormat!, 100, 30))
            {
                tag.WordWrapping = Vortice.DirectWrite.WordWrapping.NoWrap;
                _d2d.DrawTextLayout(new System.Numerics.Vector2((float)size.Width - 60, 6),
                    tag, _tagBrush!);
            }

            _d2d.EndDraw();
            Trim(first, last);
        }

        private void Trim(int first, int last)
        {
            const int window = 400;
            if (_layouts.Count <= window * 2) return;
            foreach (int k in _layouts.Keys.Where(k => k < first - window || k > last + window).ToList())
            {
                _layouts[k].Dispose();
                _layouts.Remove(k);
            }
        }

        private static Color4 ToColor4(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
    }
}
