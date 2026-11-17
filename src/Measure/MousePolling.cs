// ArchTweaker 3.20: mouse polling test.
// Reads every mouse report with Raw Input on its own high-priority thread and stamps it with the
// performance counter. From the gaps between reports it shows the real polling rate, its jitter, the 99th
// percentile gap, late reports (gap over 1.5 times the nominal period) and the longest gap while moving.
// Gaps over 50 ms count as the mouse standing still and are left out. Read-only: it changes nothing.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class PollStats
    {
        public string Device = ""; public int Reports;
        public double RateHz, JitterUs, P99Us, LatePct, MaxUs; public int NominalHz;
        public List<double> GapsUs = new List<double>();
        public DateTime When = DateTime.Now;
    }

    public static class PollMath
    {
        static readonly int[] Rates = { 125, 250, 500, 1000, 2000, 4000, 8000 };
        public static int Nominal(double hz)
        {
            int best = Rates[0];
            foreach (int r in Rates) if (Math.Abs(Math.Log(hz / r)) < Math.Abs(Math.Log(hz / best))) best = r;
            return best;
        }

        // stamps = performance-counter ticks of one device's reports, in order. Null when there are too few moving gaps.
        public static PollStats Analyse(IList<long> stamps, double freq)
        {
            if (stamps == null || stamps.Count < 50 || freq <= 0) return null;
            var gaps = new List<double>();
            for (int i = 1; i < stamps.Count; i++)
            {
                double us = (stamps[i] - stamps[i - 1]) * 1e6 / freq;
                if (us > 0 && us <= 50000) gaps.Add(us);      // over 50 ms: the mouse stood still
            }
            if (gaps.Count < 50) return null;
            var asc = gaps.OrderBy(x => x).ToList();
            double median = PresentMon.Quantile(asc, 0.5);
            var s = new PollStats { Reports = stamps.Count, GapsUs = gaps };
            s.RateHz = 1e6 / median;
            s.NominalHz = Nominal(s.RateHz);
            double mean = gaps.Average();
            s.JitterUs = Math.Sqrt(gaps.Sum(g => (g - mean) * (g - mean)) / gaps.Count);
            s.P99Us = PresentMon.Quantile(asc, 0.99);
            s.MaxUs = asc[asc.Count - 1];
            double period = 1e6 / s.NominalHz;
            s.LatePct = 100.0 * gaps.Count(g => g > period * 1.5) / gaps.Count;
            return s;
        }

        // "\\?\HID#VID_046D&PID_C539&MI_01#..." → "VID 046D, PID C539"
        public static string VidPid(string path)
        {
            var m = Regex.Match(path ?? "", @"VID_([0-9A-Fa-f]{4}).*?PID_([0-9A-Fa-f]{4})");
            return m.Success ? "VID " + m.Groups[1].Value.ToUpperInvariant() + ", PID " + m.Groups[2].Value.ToUpperInvariant() : "";
        }
    }

    public static class PollRuns
    {
        const int Keep = 30;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string File_ { get { return Path.Combine(Paths.Root, "mouse-runs.tsv"); } }
        public static string Line(PollStats s)
        {
            return string.Join("\t", new[] { s.When.ToString("yyyy-MM-dd HH:mm:ss", Inv), (s.Device ?? "").Replace('\t', ' '), s.Reports.ToString(Inv), s.RateHz.ToString("0.0", Inv),
                s.NominalHz.ToString(Inv), s.JitterUs.ToString("0.0", Inv), s.P99Us.ToString("0", Inv), s.LatePct.ToString("0.00", Inv), s.MaxUs.ToString("0", Inv) });
        }
        public static PollStats ParseLine(string l)
        {
            var p = (l ?? "").Split('\t'); if (p.Length < 9) return null;
            var s = new PollStats();
            if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", Inv, DateTimeStyles.None, out s.When)) return null;
            s.Device = p[1];
            int.TryParse(p[2], NumberStyles.Integer, Inv, out s.Reports); int.TryParse(p[4], NumberStyles.Integer, Inv, out s.NominalHz);
            double.TryParse(p[3], NumberStyles.Float, Inv, out s.RateHz); double.TryParse(p[5], NumberStyles.Float, Inv, out s.JitterUs);
            double.TryParse(p[6], NumberStyles.Float, Inv, out s.P99Us); double.TryParse(p[7], NumberStyles.Float, Inv, out s.LatePct); double.TryParse(p[8], NumberStyles.Float, Inv, out s.MaxUs);
            return s;
        }
        public static void Save(PollStats s)
        {
            try
            {
                var lines = File.Exists(File_) ? File.ReadAllLines(File_).ToList() : new List<string>();
                lines.Add(Line(s)); if (lines.Count > Keep) lines = lines.Skip(lines.Count - Keep).ToList();
                File.WriteAllLines(File_, lines);
            }
            catch (Exception ex) { Log.W("Mouse runs: {0}", ex.Message); }
        }
        public static List<PollStats> All()
        {
            var o = new List<PollStats>();
            try { if (File.Exists(File_)) foreach (var l in File.ReadAllLines(File_)) { var s = ParseLine(l); if (s != null) o.Add(s); } } catch { }
            return o;
        }
    }

    // Raw Input reader: a message-only window on its own thread.
    public sealed class PollCapture : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr data, ref uint size, uint header);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr dev, uint cmd, System.Text.StringBuilder data, ref uint size);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        const uint RIDEV_INPUTSINK = 0x100, RIDEV_REMOVE = 0x1, RID_HEADER = 0x10000005, RIDI_DEVICENAME = 0x20000007, WM_INPUT = 0xFF, WM_CLOSE = 0x10;

        sealed class Sink : NativeWindow
        {
            public PollCapture Owner;
            public Sink() { CreateHandle(new CreateParams { Caption = "ArchTweakerPoll", Parent = new IntPtr(-3) }); }   // HWND_MESSAGE
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == (int)WM_INPUT) Owner.Input(m.LParam);
                else if (m.Msg == (int)WM_CLOSE) { DestroyHandle(); Application.ExitThread(); return; }
                base.WndProc(ref m);
            }
        }

        readonly object gate = new object();
        readonly Dictionary<IntPtr, List<long>> stamps = new Dictionary<IntPtr, List<long>>();
        Thread th; Sink sink; volatile IntPtr hwnd = IntPtr.Zero;
        public string Error { get; private set; }

        public int Count { get { lock (gate) return stamps.Values.Sum(l => l.Count); } }

        void Input(IntPtr h)
        {
            long t = Stopwatch.GetTimestamp();
            int hs = IntPtr.Size == 8 ? 24 : 16;
            uint size = (uint)hs;
            IntPtr buf = Marshal.AllocHGlobal(hs);
            try
            {
                if (GetRawInputData(h, RID_HEADER, buf, ref size, (uint)hs) == uint.MaxValue) return;
                if (Marshal.ReadInt32(buf, 0) != 0) return;            // RIM_TYPEMOUSE = 0
                IntPtr dev = Marshal.ReadIntPtr(buf, 8);
                lock (gate) { List<long> l; if (!stamps.TryGetValue(dev, out l)) stamps[dev] = l = new List<long>(8192); l.Add(t); }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public static PollCapture Start()
        {
            var c = new PollCapture();
            var ready = new ManualResetEvent(false);
            c.th = new Thread(() =>
            {
                try
                {
                    c.sink = new Sink { Owner = c };
                    var d = new[] { new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = c.sink.Handle } };
                    if (!RegisterRawInputDevices(d, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)))) c.Error = "Windows refused the raw mouse input (error " + Marshal.GetLastWin32Error() + ").";
                    c.hwnd = c.sink.Handle;
                }
                catch (Exception ex) { c.Error = ex.Message; }
                ready.Set();
                if (c.Error == null) Application.Run();
                try { var d = new[] { new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = RIDEV_REMOVE, Target = IntPtr.Zero } }; RegisterRawInputDevices(d, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))); } catch { }
            }) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "ArchTweaker mouse poll" };
            c.th.SetApartmentState(ApartmentState.STA);
            c.th.Start();
            ready.WaitOne(5000);
            return c;
        }

        // Per device, the busiest first.
        public List<PollStats> Stop()
        {
            Dispose();
            var o = new List<PollStats>();
            Dictionary<IntPtr, List<long>> copy;
            lock (gate) copy = stamps.ToDictionary(k => k.Key, k => k.Value.ToList());
            foreach (var kv in copy.OrderByDescending(k => k.Value.Count))
            {
                var s = PollMath.Analyse(kv.Value, Stopwatch.Frequency);
                if (s == null) continue;
                s.Device = Name(kv.Key);
                o.Add(s);
            }
            return o;
        }

        static string Name(IntPtr dev)
        {
            try
            {
                uint n = 0; GetRawInputDeviceInfo(dev, RIDI_DEVICENAME, null, ref n);
                var sb = new System.Text.StringBuilder((int)n + 2);
                if (n > 0 && GetRawInputDeviceInfo(dev, RIDI_DEVICENAME, sb, ref n) != uint.MaxValue)
                {
                    string vp = PollMath.VidPid(sb.ToString());
                    return vp.Length > 0 ? L.F("Mouse ({0})", vp) : L.T("Mouse");
                }
            }
            catch { }
            return dev == IntPtr.Zero ? L.T("Mouse (remote or touchpad)") : L.T("Mouse");
        }

        public void Dispose()
        {
            if (hwnd != IntPtr.Zero) { PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); hwnd = IntPtr.Zero; }
            if (th != null) { th.Join(3000); th = null; }
        }
    }

    // Gaps between reports as a histogram from 0 to 2 nominal periods; the last bar collects longer gaps.
    public class PollHistogram : Control
    {
        PollStats s;
        public PollHistogram() { DoubleBuffered = true; Dock = DockStyle.Top; Height = 150; Visible = false; }
        public void Set(PollStats st) { s = st; Visible = st != null; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Bg);
            if (s == null || s.GapsUs.Count == 0) return;
            const int N = 40; var bins = new int[N];
            double period = 1e6 / s.NominalHz, w = 2 * period / (N - 1);
            foreach (var x in s.GapsUs) bins[Math.Min(N - 1, (int)(x / w))]++;
            int max = Math.Max(1, bins.Max());
            int pad = Theme.S(8), top = Theme.S(22), bottom = Height - Theme.S(20);
            float bw = (Width - 2f * pad) / N;
            TextRenderer.DrawText(g, L.T("Gaps between reports. Left of the line: on time. Last bar: 2 periods and longer."), Theme.FS, new Point(pad, 2), Theme.Muted, TextFormatFlags.NoPrefix);
            using (var b = new SolidBrush(Theme.Accent)) using (var late = new SolidBrush(Theme.Warn))
                for (int i = 0; i < N; i++)
                {
                    float h = (bottom - top) * bins[i] / (float)max;
                    bool isLate = i * w > period * 1.5;
                    g.FillRectangle(isLate ? late : b, pad + i * bw + 1, bottom - h, Math.Max(1, bw - 2), h);
                }
            float px = pad + (float)(period * 1.5 / w) * bw;
            using (var p = new Pen(Theme.Muted) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawLine(p, px, top, px, bottom);
            TextRenderer.DrawText(g, "0", Theme.FS, new Point(pad, bottom + 2), Theme.Muted, TextFormatFlags.NoPrefix);
            string mid = L.F("{0} µs (1 period)", period.ToString("0", CultureInfo.InvariantCulture));
            TextRenderer.DrawText(g, mid, Theme.FS, new Point((int)(pad + (N - 1) / 2f * bw), bottom + 2), Theme.Muted, TextFormatFlags.NoPrefix);
        }
    }

    public class MousePollPage : Panel, IPage
    {
        readonly ComboBox dur = Theme.Drop(70, "Test length");
        readonly Button runBtn;
        readonly Label status, result;
        readonly PollHistogram hist = new PollHistogram();
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        bool running;

        public MousePollPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("When", 120); lv.Columns.Add("Device", 230); lv.Columns.Add("Rate", 90); lv.Columns.Add("Set to", 80); lv.Columns.Add("Jitter", 80); lv.Columns.Add("99th pct gap", 100); lv.Columns.Add("Late", 70); lv.Columns.Add("Longest gap", 100); lv.Columns.Add("Reports", 80);
            var bar = Theme.Bar();
            runBtn = Theme.Lead("Start test", delegate { Run(); }); bar.Controls.Add(runBtn);
            bar.Controls.Add(new Label { Text = "Length:", ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, Margin = new Padding(8, 8, 4, 0) });
            dur.Items.AddRange(new object[] { "5 s", "10 s", "20 s" }); dur.SelectedIndex = 0; bar.Controls.Add(dur);
            status = Theme.StatusText();
            bar.Controls.Add(status);
            result = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 54, ForeColor = Theme.Text, Font = Theme.FB, UseMnemonic = false, Tag = "notr", Padding = new Padding(2, 6, 2, 0), Visible = false };
            var head = new Label { Text = "Saved tests", Font = Theme.FB, ForeColor = Theme.Text, Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false };
            Controls.Add(lv); Controls.Add(head); Controls.Add(hist); Controls.Add(result);
            Controls.Add(Theme.Note("Click Start test, then move the mouse in fast circles until the test ends. ArchTweaker reads every report the mouse sends and shows its real polling rate and how evenly the reports arrive. Run it before and after a tweak, a new USB port or a mouse firmware setting. Jitter under 10 % of the period is very good."));
            Controls.Add(bar);
            Skin.SetEmpty(lv, "No tests yet. Click Start test and move the mouse.");
        }

        public void OnShow() { Reload(); }
        public void Filter(string q) { }

        void Reload()
        {
            var all = PollRuns.All();
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var s in Enumerable.Reverse(all))
                lv.Items.Add(new ListViewItem(new[] { s.When.ToString("dd MMM HH:mm"), s.Device, s.RateHz.ToString("0") + " Hz", s.NominalHz + " Hz", s.JitterUs.ToString("0.0") + " µs",
                    s.P99Us.ToString("0") + " µs", s.LatePct.ToString("0.0") + " %", s.MaxUs.ToString("0") + " µs", s.Reports.ToString() }));
            lv.EndUpdate();
            Skin.Loaded(lv, "No tests yet. Click Start test and move the mouse.");
        }

        void Run()
        {
            if (running) return;
            int secs = dur.SelectedIndex == 0 ? 5 : dur.SelectedIndex == 1 ? 10 : 20;
            var cap = PollCapture.Start();
            if (cap.Error != null) { cap.Dispose(); Dlg.Error("The test could not start", cap.Error); return; }
            running = true; runBtn.Enabled = false;
            var sw = Stopwatch.StartNew();
            var t = new System.Windows.Forms.Timer { Interval = 250 };
            t.Tick += delegate
            {
                double left = secs - sw.Elapsed.TotalSeconds;
                status.Text = L.F("Move the mouse… {0} s · {1} reports", Math.Max(0, (int)Math.Ceiling(left)), cap.Count);
                if (left > 0) return;
                t.Stop(); t.Dispose();
                var res = cap.Stop();
                running = false; runBtn.Enabled = true; status.Text = "";
                result.Visible = true;
                if (res.Count == 0) { result.Text = L.T("Too few reports. Move the mouse the whole time, then try again."); hist.Set(null); return; }
                var s = res[0];
                double period = 1e6 / s.NominalHz;
                result.ForeColor = s.JitterUs > period * 0.25 || s.LatePct > 2 ? Theme.Warn : Theme.Text;
                result.Text = L.F("{0}: {1} Hz (set to {2} Hz) · jitter {3} µs ({4} % of the period) · 99th percentile gap {5} µs · late {6} % · longest gap {7} µs",
                    s.Device, s.RateHz.ToString("0"), s.NominalHz, s.JitterUs.ToString("0.0"), (100 * s.JitterUs / period).ToString("0"), s.P99Us.ToString("0"), s.LatePct.ToString("0.0"), s.MaxUs.ToString("0"));
                hist.Set(s);
                PollRuns.Save(s);
                Log.W("Mouse polling: {0}", result.Text);
                Reload();
            };
            t.Start();
        }
    }
}
