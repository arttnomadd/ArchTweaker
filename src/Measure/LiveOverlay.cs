// ArchTweaker 3.19: live latency overlay.
//  - LiveFrames: PresentMon streaming its CSV to stdout. Live FPS (last second) and 1 % low (last 10 s) of the
//    active window's process, or of one chosen game.
//  - OverlayForm: a small always-on-top, click-through, no-activate window in a screen corner. Once a second it
//    shows FPS, frame time, % DPC and % ISR time (all cores and the busiest core), interrupts/s and timer resolution.
//  - OverlayHost: starts and stops the overlay, Ctrl+Shift+F10 hides and shows it from inside a game.
//  - OverlayPage (Analyse → Live overlay).
// Read-only: the overlay changes nothing on the system. It draws over borderless and windowed games; a game in
// exclusive full screen hides every other window, so the overlay is not visible there.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArchTweaker
{
    // Pure frame-window maths (unit-tested).
    public static class LiveMath
    {
        // FPS over the newest `spanMs` of frames, and the 1 % low over the whole window (average FPS of the slowest 1 %).
        public static void Stats(IList<double> ms, double spanMs, out double fps, out double low1, out double lastMs)
        {
            fps = low1 = lastMs = -1;
            if (ms == null || ms.Count == 0) return;
            lastMs = ms[ms.Count - 1];
            double sum = 0; int n = 0;
            for (int i = ms.Count - 1; i >= 0 && sum < spanMs; i--) { sum += ms[i]; n++; }
            if (sum > 0) fps = n * 1000.0 / sum;
            if (ms.Count >= 20)
            {
                var worst = ms.OrderByDescending(x => x).Take(Math.Max(1, ms.Count / 100)).ToList();
                double avg = worst.Average();
                if (avg > 0) low1 = 1000.0 / avg;
            }
        }

        // Column indexes in a PresentMon CSV header: frame time (1.x, 2.x and 3.x names) and process id.
        public static void Columns(string header, out int ft, out int pid)
        {
            var h = (header ?? "").Split(',').Select(x => x.Trim().Trim('"').ToLowerInvariant()).ToArray();
            ft = -1; pid = Array.IndexOf(h, "processid");
            foreach (var n in new[] { "msbetweenpresents", "frametime", "msbetweenappstart" }) { ft = Array.IndexOf(h, n); if (ft >= 0) break; }
        }

        // 0 = fine, 1 = watch, 2 = bad. % DPC or % ISR time of the busiest core.
        public static int CoreLevel(double pct) { return pct >= 5 ? 2 : pct >= 2 ? 1 : 0; }
        public static int FpsLevel(double fps, double low1) { return low1 > 0 && fps > 0 && low1 < fps * 0.5 ? 2 : low1 > 0 && fps > 0 && low1 < fps * 0.7 ? 1 : 0; }
    }

    // PresentMon streaming to stdout. One instance per overlay run.
    public sealed class LiveFrames : IDisposable
    {
        public const string Session = "ArchTweakerLive";
        Process p;
        readonly object gate = new object();
        readonly List<double> win = new List<double>();   // newest last, about 10 s of frames
        double winSum; long lastFrameMs = -1;             // running total of win; when the newest frame came
        readonly Stopwatch clock = Stopwatch.StartNew();
        volatile int ft = -1, pidCol = -1;
        volatile int follow;                               // process id to keep, 0 = every process
        public string Error { get; private set; }
        public bool Running { get { try { return p != null && !p.HasExited; } catch { return false; } } }
        public int FollowPid { get { return follow; } set { if (follow != value) { follow = value; lock (gate) { win.Clear(); winSum = 0; lastFrameMs = -1; } } } }

        // processName null = every process (the overlay then keeps the active window's frames).
        public static LiveFrames Start(string processName)
        {
            var lf = new LiveFrames();
            string exe = PresentMon.Find();
            if (exe == null) { lf.Error = "PresentMon not found"; return lf; }
            if (processName != null && processName.IndexOf('"') >= 0) { lf.Error = "Invalid process name."; return lf; }
            bool v2 = true;
            try { var v = FileVersionInfo.GetVersionInfo(exe); v2 = v.FileMajorPart == 0 || v.FileMajorPart >= 2; } catch { }
            string target = processName == null ? "" : (v2 ? "--process_name \"" : "-process_name \"") + processName + "\" ";
            string args = v2
                ? target + "--output_stdout --no_console_stats --stop_existing_session --session_name " + Session
                : target + "-output_stdout -no_top -stop_existing_session -session_name " + Session;
            try
            {
                lf.p = new Process { StartInfo = Shell.HiddenInfo(exe, args), EnableRaisingEvents = true };   // 3.35.1: shared start settings
                lf.p.OutputDataReceived += (s, e) => lf.Line(e.Data);
                lf.p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data) && lf.Error == null && lf.ft < 0) lf.Error = e.Data.Trim(); };
                lf.p.Start();
                try { lf.p.BeginOutputReadLine(); lf.p.BeginErrorReadLine(); }
                catch { try { lf.p.Kill(); } catch { } throw; }
                Log.W("Live overlay: PresentMon started ({0})", args);
            }
            catch (Exception ex) { lf.Error = ex.Message; lf.p = null; Log.W("Live overlay: PresentMon did not start: {0}", ex.Message); }
            return lf;
        }

        void Line(string l)
        {
            if (string.IsNullOrEmpty(l)) return;
            if (ft < 0)
            {
                int a, b; LiveMath.Columns(l, out a, out b);
                if (a >= 0) { pidCol = b; ft = a; }
                return;
            }
            var f = l.Split(',');
            if (ft >= f.Length) return;
            int want = follow;
            if (want != 0 && pidCol >= 0)
            {
                int pid; if (pidCol >= f.Length || !int.TryParse(f[pidCol].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid) || pid != want) return;
            }
            double v;
            if (!double.TryParse(f[ft].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v <= 0 || v > 5000) return;
            lock (gate)
            {
                win.Add(v); winSum += v; lastFrameMs = clock.ElapsedMilliseconds;
                int drop = 0;
                while (drop < win.Count - 1 && winSum - win[drop] >= 10000) { winSum -= win[drop]; drop++; }
                if (drop > 0) win.RemoveRange(0, drop);
            }
        }

        // FPS over the last second, 1 % low over the last 10 s, newest frame time. -1 = no frames, or none for 2 s
        // (the game paused, lost focus or closed: old numbers would look live).
        public void Sample(out double fps, out double low1, out double lastMs)
        {
            List<double> copy;
            lock (gate)
            {
                if (lastFrameMs < 0 || clock.ElapsedMilliseconds - lastFrameMs > 2000) { win.Clear(); winSum = 0; lastFrameMs = -1; }
                copy = win.ToList();
            }
            LiveMath.Stats(copy, 1000, out fps, out low1, out lastMs);
        }

        public void Dispose()
        {
            try { if (p != null && !p.HasExited) { p.Kill(); p.WaitForExit(2000); } } catch { }
            try { if (p != null) p.Dispose(); } catch { }
            p = null;
            Etw.Stop(Session);   // a killed PresentMon leaves its ETW session running
        }
    }

    // Stops a named ETW session (never the kernel logger, which only DriverTrace starts and stops).
    static class Etw
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ControlTraceW")] static extern uint ControlTrace(ulong handle, string name, IntPtr props, uint code);
        public static void Stop(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Equals("NT Kernel Logger", StringComparison.OrdinalIgnoreCase)) return;
            int nameOff = IntPtr.Size == 8 ? 120 : 116, size = nameOff + 2048;
            IntPtr pr = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i++) Marshal.WriteByte(pr, i, 0);
                Marshal.WriteInt32(pr, 0, size);
                Marshal.WriteInt32(pr, nameOff - 4, nameOff);          // LoggerNameOffset
                Marshal.WriteInt32(pr, nameOff - 8, nameOff + 1024);   // LogFileNameOffset
                ControlTrace(0, name, pr, 1);                          // EVENT_TRACE_CONTROL_STOP; "not found" is fine
            }
            catch { }
            finally { Marshal.FreeHGlobal(pr); }
        }
    }

    public sealed class OverlayForm : Form
    {
        // fixed dark colours: the overlay sits on top of a game, whatever the app theme is
        static readonly Color Back = Color.FromArgb(14, 14, 17), Fg = Color.FromArgb(233, 233, 238), Dim = Color.FromArgb(154, 154, 166);
        static readonly Color[] Lv = { Color.FromArgb(67, 196, 120), Color.FromArgb(230, 178, 58), Color.FromArgb(255, 107, 107) };
        public struct Row { public string K, V; public int Level; public bool Plain; }
        List<Row> rows = new List<Row>();
        readonly Font fK, fV;
        public int Corner;   // 0 top-left, 1 top-right, 2 bottom-left, 3 bottom-right
        public Rectangle Area = Screen.PrimaryScreen.Bounds;   // 3.19.1: the monitor of the game, not always the primary one

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            BackColor = Back; Opacity = 0.86; DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
            float px = 13f * Theme.Dpi / 96f;
            fK = new Font("Segoe UI", px, FontStyle.Regular, GraphicsUnit.Pixel);
            fV = new Font("Segoe UI Semibold", px, FontStyle.Regular, GraphicsUnit.Pixel);
            Size = new Size(Theme.S(200), Theme.S(60));
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x8 | 0x20 | 0x80 | 0x80000 | 0x08000000;   // TOPMOST | TRANSPARENT (click-through) | TOOLWINDOW | LAYERED | NOACTIVATE
                return cp;
            }
        }

        public void SetRows(List<Row> r)
        {
            rows = r ?? new List<Row>();
            int pad = Theme.S(10), lh = (int)Math.Ceiling(fV.GetHeight()) + Theme.S(3), kw = 0, vw = 0;
            foreach (var x in rows)
            {
                kw = Math.Max(kw, TextRenderer.MeasureText(x.K ?? "", fK).Width);
                vw = Math.Max(vw, TextRenderer.MeasureText(x.V ?? "", fV).Width);
            }
            var sz = new Size(pad * 2 + kw + Theme.S(10) + vw, pad * 2 + lh * Math.Max(1, rows.Count) - Theme.S(3));
            var b = Area; int m = Theme.S(12);
            int x0 = Corner == 1 || Corner == 3 ? b.Right - sz.Width - m : b.Left + m;
            int y0 = Corner >= 2 ? b.Bottom - sz.Height - m : b.Top + m;
            Bounds = new Rectangle(x0, y0, sz.Width, sz.Height);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int pad = Theme.S(10), lh = (int)Math.Ceiling(fV.GetHeight()) + Theme.S(3), kw = 0;
            foreach (var x in rows) kw = Math.Max(kw, TextRenderer.MeasureText(x.K ?? "", fK).Width);
            int y = pad;
            foreach (var x in rows)
            {
                TextRenderer.DrawText(e.Graphics, x.K ?? "", fK, new Point(pad, y), Dim, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics, x.V ?? "", fV, new Point(pad + kw + Theme.S(10), y), x.Plain ? Fg : Lv[Math.Max(0, Math.Min(2, x.Level))], TextFormatFlags.NoPrefix);
                y += lh;
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { fK.Dispose(); fV.Dispose(); } base.Dispose(disposing); }
    }

    public static class OverlayHost
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        const int HotId = 0x5319;

        sealed class HotWindow : NativeWindow
        {
            public HotWindow() { CreateHandle(new CreateParams { Caption = "ArchTweakerOverlayHotkey", Parent = new IntPtr(-3) }); }   // HWND_MESSAGE
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotId) Toggle();
                base.WndProc(ref m);
            }
        }

        static OverlayForm form; static LiveFrames frames; static CoreCounters counters; static Timer timer; static HotWindow hot;
        static string game; static bool hidden, exitHooked; static int selfPid;
        public static bool Running { get { return form != null; } }
        public static bool HotkeyOk { get; private set; }
        public static string Game { get { return game; } }
        public static event Action Changed;

        static int corner = -1;
        public static int Corner
        {
            get
            {
                if (corner < 0) { corner = 1; try { object o = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "OverlayCorner"); if (o is int) corner = Math.Max(0, Math.Min(3, (int)o)); } catch { } }
                return corner;
            }
            set
            {
                corner = Math.Max(0, Math.Min(3, value));
                try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\ArchTweaker")) k.SetValue("OverlayCorner", corner, Microsoft.Win32.RegistryValueKind.DWord); }
                catch (Exception ex) { Log.W("Overlay corner not saved: {0}", ex.Message); }
                if (form != null) { form.Corner = corner; Tick(); }
            }
        }
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // gameExe null = follow the active window. Runs on the UI thread.
        // Null on success, else why the overlay could not start.
        public static string Start(string gameExe)
        {
            try { StartCore(gameExe); return null; }
            catch (Exception ex) { Log.W("Live overlay did not start: {0}", ex.Message); Stop(); return ex.Message; }
        }
        static void StartCore(string gameExe)
        {
            Stop();
            game = gameExe;
            counters = new CoreCounters();
            if (PresentMon.Available) frames = LiveFrames.Start(gameExe);
            form = new OverlayForm { Corner = Corner };
            hidden = false;
            if (hot == null) try { hot = new HotWindow(); } catch (Exception ex) { Log.W("Overlay hotkey window: {0}", ex.Message); }
            HotkeyOk = hot != null && RegisterHotKey(hot.Handle, HotId, 0x2 | 0x4 | 0x4000, 0x79);   // Ctrl+Shift+F10, no auto-repeat
            if (!HotkeyOk) Log.W("Live overlay: Ctrl+Shift+F10 is in use by another program, so the hotkey is off.");
            if (!exitHooked) { exitHooked = true; Application.ApplicationExit += delegate { Stop(); }; }
            timer = new Timer { Interval = 1000 };
            timer.Tick += delegate { Tick(); };
            Tick(); form.Show(); timer.Start();
            Log.W("Live overlay started ({0}).", gameExe ?? "follows the active window");
            var c = Changed; if (c != null) c();
        }

        public static void Stop()
        {
            if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; }
            if (hot != null) { try { UnregisterHotKey(hot.Handle, HotId); } catch { } }
            HotkeyOk = false;
            if (frames != null) { frames.Dispose(); frames = null; }
            if (counters != null) { counters.Dispose(); counters = null; }
            bool was = form != null;
            if (form != null) { try { form.Close(); form.Dispose(); } catch { } form = null; }
            if (was) { Log.W("Live overlay stopped."); var c = Changed; if (c != null) c(); }
        }

        public static void Toggle()
        {
            if (form == null) return;
            hidden = !hidden;
            if (hidden) form.Hide(); else { Tick(); form.Show(); }
        }

        static int ForegroundPid()
        {
            try { uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return (int)pid; } catch { return 0; }
        }

        static string N(double v, string f) { return v.ToString(f, CultureInfo.InvariantCulture); }

        // 3.19.1: the monitor of the foreground window when it belongs to another program (the game); while
        // ArchTweaker is in front, the overlay stays where it was.
        static Rectangle lastArea = Rectangle.Empty;
        static Rectangle GameScreen()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (selfPid == 0) using (var me = Process.GetCurrentProcess()) selfPid = me.Id;
                if (h != IntPtr.Zero && pid != 0 && (int)pid != selfPid) lastArea = Screen.FromHandle(h).Bounds;
            }
            catch { }
            return lastArea.IsEmpty ? Screen.PrimaryScreen.Bounds : lastArea;
        }

        static void Tick()
        {
            if (form == null || hidden) return;
            var rows = new List<OverlayForm.Row>();
            if (frames != null)
            {
                if (game == null)
                {
                    int fp = ForegroundPid();
                    // ArchTweaker itself in front: keep the last game instead of showing its own (absent) frames
                    if (selfPid == 0) try { using (var me = Process.GetCurrentProcess()) selfPid = me.Id; } catch { }
                    if (fp != 0 && fp != selfPid) frames.FollowPid = fp;
                }
                double fps, low, last; frames.Sample(out fps, out low, out last);
                if (fps > 0)
                {
                    rows.Add(new OverlayForm.Row { K = "FPS", V = N(fps, "0") + (low > 0 ? "   1% " + N(low, "0") : ""), Level = LiveMath.FpsLevel(fps, low) });
                    rows.Add(new OverlayForm.Row { K = "Frame", V = N(last, "0.0") + " ms", Plain = true });
                }
                else rows.Add(new OverlayForm.Row { K = "FPS", V = frames.Running ? "no frames" : "PresentMon stopped" + (frames.Error != null ? ": " + (frames.Error.Length > 40 ? frames.Error.Substring(0, 40) + "…" : frames.Error) : ""), Plain = true, Level = frames.Running ? 0 : 1 });
            }
            else rows.Add(new OverlayForm.Row { K = "FPS", V = "needs PresentMon", Plain = true });

            var cs = counters != null && counters.Ok ? counters.Read() : new List<CoreSample>();
            if (cs.Count > 0)
            {
                double dpc = cs.Average(x => x.DpcPct), isr = cs.Average(x => x.IsrPct), dMax = cs.Max(x => x.DpcPct), iMax = cs.Max(x => x.IsrPct), ints = cs.Sum(x => x.IntsPerSec);
                rows.Add(new OverlayForm.Row { K = "DPC", V = N(dpc, "0.00") + " %   max " + N(dMax, "0.0") + " %", Level = LiveMath.CoreLevel(dMax) });
                rows.Add(new OverlayForm.Row { K = "ISR", V = N(isr, "0.00") + " %   max " + N(iMax, "0.0") + " %", Level = LiveMath.CoreLevel(iMax) });
                rows.Add(new OverlayForm.Row { K = "Interrupts", V = ints >= 10000 ? N(ints / 1000, "0.0") + "k/s" : N(ints, "0") + "/s", Plain = true });
            }
            else rows.Add(new OverlayForm.Row { K = "DPC/ISR", V = "no counter data", Plain = true });
            double t = TimerRes.CurrentMs();
            if (t > 0) rows.Add(new OverlayForm.Row { K = "Timer", V = N(t, "0.000") + " ms", Level = t > 1.01 ? 1 : 0 });
            form.Corner = Corner;
            form.Area = GameScreen();
            form.SetRows(rows);
            // a borderless game that goes topmost itself would cover the overlay: put it back on top, without focus
            try { SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); } catch { }   // HWND_TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
        }
    }

    public class OverlayPage : Panel, IPage
    {
        readonly ComboBox source = Theme.Drop(260, "Frames from", 8);
        readonly ComboBox corner = Theme.Drop(130, "Overlay corner");
        readonly Button startBtn, stopBtn;
        readonly Label state, pmState;
        const string Follow = "Follow the active window";
        static readonly string[] Corners = { "Top left", "Top right", "Bottom left", "Bottom right" };

        public OverlayPage()
        {
            BackColor = Theme.Bg;
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.BarLabel("Frames from:")); bar.Controls.Add(source);
            bar.Controls.Add(Theme.Btn("Refresh", delegate { FillSources(); }));
            bar.Controls.Add(Theme.BarLabel("Corner:"));
            foreach (var c in Corners) corner.Items.Add(L.T(c));
            corner.SelectedIndex = OverlayHost.Corner;
            corner.SelectedIndexChanged += delegate { OverlayHost.Corner = corner.SelectedIndex; };
            bar.Controls.Add(corner);

            var bar2 = Theme.Bar();
            startBtn = Theme.Lead("Start overlay", delegate { Start(); });
            stopBtn = Theme.Btn("Stop overlay", delegate { OverlayHost.Stop(); });
            bar2.Controls.Add(startBtn); bar2.Controls.Add(stopBtn);
            bar2.Controls.Add(Theme.Btn("Choose PresentMon.exe…", delegate { ChoosePm(); }));
            state = Theme.StatusText();
            bar2.Controls.Add(state);

            pmState = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 22, ForeColor = Theme.Muted, Font = Theme.FS, UseMnemonic = false, Tag = "notr", Padding = new Padding(2, 4, 2, 0) };
            var help = new Label
            {
                Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = Theme.F, Padding = new Padding(2, 10, 2, 0), UseMnemonic = false,
                Text = "What the overlay shows, once a second:\r\n\r\n"
                    + "•  FPS: frames in the last second, and the 1 % low of the last 10 seconds. Needs PresentMon.\r\n"
                    + "•  Frame: the newest frame time in ms.\r\n"
                    + "•  DPC and ISR: the share of time Windows spends in driver DPCs and interrupts, the average of all cores and the busiest core. Under 2 % is fine, from 5 % on a driver is a problem.\r\n"
                    + "•  Interrupts: hardware interrupts per second, all cores.\r\n"
                    + "•  Timer: the current Windows timer resolution.\r\n\r\n"
                    + "Ctrl+Shift+F10 hides and shows the overlay from inside a game. The overlay ignores the mouse, so clicks go to the game.\r\n"
                    + "Use borderless or windowed mode. Exclusive full screen hides every other window, so the overlay is not visible there.\r\n"
                    + "The overlay changes nothing on the system."
            };
            Controls.Add(help); Controls.Add(pmState);
            Controls.Add(Theme.Note("A small live read-out over your game: FPS and 1 % low, driver DPC and interrupt load, and the timer resolution."));
            Controls.Add(bar2); Controls.Add(bar);
            OverlayHost.Changed += Sync;
            Disposed += delegate { OverlayHost.Changed -= Sync; };
        }

        public void OnShow() { FillSources(); Sync(); }
        public void Filter(string q) { }

        void FillSources()
        {
            string keep = source.SelectedItem as string;
            source.Items.Clear(); source.Items.Add(L.T(Follow));
            foreach (var g in Bench.Games()) source.Items.Add(g);
            int i = keep == null ? -1 : source.Items.IndexOf(keep);
            source.SelectedIndex = i >= 0 ? i : 0;
        }

        void Sync()
        {
            if (IsDisposed) return;
            bool on = OverlayHost.Running;
            startBtn.Text = L.T(on ? "Restart overlay" : "Start overlay");
            stopBtn.Enabled = on;
            state.Text = !on ? "" : L.T("Overlay is on") + " · " + (OverlayHost.Game ?? L.T("follows the active window")) + (OverlayHost.HotkeyOk ? " · Ctrl+Shift+F10" : " · " + L.T("hotkey in use by another program"));
            string d = PresentMon.Describe();
            pmState.Text = d != null ? L.T("Frame capture:") + " " + d : L.T("PresentMon not found: the overlay shows latency only. Click Choose PresentMon.exe… to add FPS.");
            pmState.ForeColor = d != null ? Theme.Muted : Theme.Warn;
        }

        void Start()
        {
            string g = source.SelectedIndex > 0 ? source.SelectedItem as string : null;
            string err = OverlayHost.Start(g);
            if (err != null) { Dlg.Error("The overlay did not start", err); Sync(); return; }
            Log.Say(PresentMon.Available ? "Live overlay is on. Ctrl+Shift+F10 hides and shows it." : "Live overlay is on (latency only: PresentMon not found). Ctrl+Shift+F10 hides and shows it.");
            Sync();
        }

        void ChoosePm()
        {
            using (var d = new OpenFileDialog { Filter = "PresentMon|PresentMon*.exe|Programs|*.exe", Title = L.T("Choose PresentMon.exe") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string err = PresentMon.Import(d.FileName);
                if (err != null) Dlg.Error("Could not use this file", err);
                else Log.Say("PresentMon is ready: {0}", PresentMon.Describe() ?? PresentMon.BinPath);
                Sync();
            }
        }
    }
}
