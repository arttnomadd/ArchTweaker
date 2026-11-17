// ArchTweaker 3.20: interrupt tuner.
// For each chosen PCI device (network, USB, audio; the graphics card on request) it tries MSI mode, high
// interrupt priority and pinning its interrupts to one of two fast cores. Each setting is compared with the
// current one in turns (current, setting, setting, current…): the device restarts (pnputil /restart-device)
// and a kernel trace records the device driver's DPC and ISR time. A setting stays only when the driver's mean
// time per DPC/ISR drops by at least 5 % and Welch's t-test agrees; otherwise the device goes back.
// Every write goes through Reg.Set (journaled), so Undo everything also covers it. The values from before
// are kept in irq-tune-backup.tsv, so "Put back the original settings" restores them exactly.
// Storage controllers are never touched: a boot drive that loses its interrupt mode does not start.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    // One interrupt setup of a device. Null fields keep what is there.
    public class IrqConfig
    {
        public string Name = "";
        public int? Msi;            // MSISupported 1 / 0; -1 = value absent
        public int? Policy;         // DevicePolicy; -1 = absent
        public int? Priority;       // DevicePriority 1 low, 2 normal, 3 high; -1 = absent
        public ulong? Mask;         // AssignmentSetOverride; 0 = absent
        public IrqConfig Clone() { return (IrqConfig)MemberwiseClone(); }
    }

    public class IrqTrial
    {
        public string Setting = "";
        public List<double> Base = new List<double>(), Try = new List<double>();
        public double MeanBase, MeanTry, DiffPct, WorstBase, WorstTry; public bool Significant, Kept; public string Note = "";
    }

    public class IrqDeviceResult
    {
        public IrqDevice Dev; public string Driver = ""; public string Note = "";
        public List<IrqTrial> Trials = new List<IrqTrial>();
    }

    public class IrqTuneResult
    {
        public DateTime When = DateTime.Now; public int Secs, Rounds; public bool Cancelled; public string Error, Report;
        public List<IrqDeviceResult> Devices = new List<IrqDeviceResult>();
    }

    public static class IrqTuner
    {
        public static volatile bool Cancel;
        const string Marker = "IrqTuneInProgress";
        static string BackupFile { get { return Path.Combine(Paths.Root, "irq-tune-backup.tsv"); } }

        // ---------- pure helpers (unit-tested) ----------

        // "SYSTEM\CurrentControlSet\Enum\PCI\VEN_x\inst" → "PCI\VEN_x\inst"
        public static string InstanceId(string key)
        {
            const string E = @"SYSTEM\CurrentControlSet\Enum\";
            return key != null && key.StartsWith(E, StringComparison.OrdinalIgnoreCase) ? key.Substring(E.Length) : key;
        }

        // "\SystemRoot\System32\drivers\e2f.sys" or "System32\DRIVERS\USBXHCI.SYS" → "e2f.sys"; empty → service + ".sys"
        public static string DriverFile(string imagePath, string service)
        {
            string p = (imagePath ?? "").Trim().Trim('"');
            if (p.Length > 0) { int i = p.LastIndexOf('\\'); return (i >= 0 ? p.Substring(i + 1) : p).ToLowerInvariant(); }
            return string.IsNullOrEmpty(service) ? "" : service.ToLowerInvariant() + ".sys";
        }

        // Settings to try for a device. cpus = candidate logical processors to pin to.
        public static List<IrqConfig> Configs(IrqDevice d, IList<int> cpus)
        {
            var o = new List<IrqConfig>();
            if (d == null || d.Kind == "Storage") return o;
            if (d.HasMsiKey && !d.Msi) o.Add(new IrqConfig { Name = "MSI mode on", Msi = 1 });
            o.Add(new IrqConfig { Name = "High interrupt priority", Priority = 3 });
            foreach (int c in cpus ?? new int[0])
                if (d.PinnedCpu != c) o.Add(new IrqConfig { Name = L.F("Interrupts on CPU {0}", c), Policy = 4, Mask = 1UL << c });
            return o;
        }

        // Fast cores to try: skip core 0 (and its SMT sibling), then the first thread of the next two physical cores.
        public static List<int> PinCpus(ulong fastMask, bool smt, int threads)
        {
            var fast = new List<int>();
            for (int i = 0; i < 64; i++) if ((fastMask >> i & 1) != 0) fast.Add(i);
            if (fast.Count == 0) for (int i = 0; i < Math.Min(64, Math.Max(1, threads)); i++) fast.Add(i);
            int step = smt ? 2 : 1;
            return fast.Where(c => c >= step && (c % step) == 0).Take(2).ToList();
        }

        // Lower mean time per DPC/ISR is better. Kept: at least 5 % better, significant, and the worst call not over 25 % worse.
        public static void Decide(IrqTrial t)
        {
            var c = ABTest.Compare(t.Base.ToArray(), t.Try.ToArray());
            t.MeanBase = c.MeanA; t.MeanTry = c.MeanB; t.DiffPct = c.DiffPct; t.Significant = c.Significant;
            bool worstOk = t.WorstBase <= 0 || t.WorstTry <= t.WorstBase * 1.25;
            t.Kept = c.Significant && c.DiffPct <= -5.0 && worstOk;
            t.Note = t.Kept ? "helped" : c.DiffPct <= -5.0 && c.Significant && !worstOk ? "faster on average, worse worst case" : !c.Significant || Math.Abs(c.DiffPct) < 5 ? "within noise" : "made it worse";
        }

        public static string Line(IrqDevice d, IrqConfig c)
        {
            Func<int?, string> n = x => x.HasValue ? x.Value.ToString(CultureInfo.InvariantCulture) : "-1";
            return d.Key + "\t" + n(c.Msi) + "\t" + n(c.Policy) + "\t" + n(c.Priority) + "\t" + (c.Mask.HasValue && c.Mask.Value != 0 ? c.Mask.Value.ToString("X", CultureInfo.InvariantCulture) : "");
        }
        public static bool ParseLine(string l, out string key, out IrqConfig c)
        {
            key = null; c = null;
            var p = (l ?? "").Split('\t'); if (p.Length < 5 || p[0].Length == 0) return false;
            int a, b, e; ulong m = 0;
            if (!int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out a) || !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out b)
                || !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out e)) return false;
            if (p[4].Length > 0 && !ulong.TryParse(p[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out m)) return false;
            key = p[0]; c = new IrqConfig { Name = "original", Msi = a, Policy = b, Priority = e, Mask = m };
            return true;
        }

        // ---------- registry ----------

        public static IrqConfig Read(IrqDevice d)
        {
            Func<string, string, int> dw = (k, n) => { object v = Reg.Get(Hive.HKLM, k, n); return v is int ? (int)v : -1; };
            ulong mask = 0;
            var b = Reg.Get(Hive.HKLM, d.AffKey, "AssignmentSetOverride") as byte[];
            if (b != null) for (int i = 0; i < Math.Min(8, b.Length); i++) mask |= (ulong)b[i] << (8 * i);
            return new IrqConfig { Name = "current", Msi = d.HasMsiKey ? dw(d.MsiKey, "MSISupported") : (int?)null, Policy = dw(d.AffKey, "DevicePolicy"), Priority = dw(d.AffKey, "DevicePriority"), Mask = mask };
        }

        // Writes the fields that are set. -1 / 0 mask = delete the value.
        static void Write(IrqDevice d, IrqConfig c)
        {
            if (c.Msi.HasValue && d.HasMsiKey) { if (c.Msi.Value < 0) Reg.DeleteValue(Hive.HKLM, d.MsiKey, "MSISupported"); else Reg.Set(Hive.HKLM, d.MsiKey, "MSISupported", RegistryValueKind.DWord, c.Msi.Value); }
            if (c.Priority.HasValue) { if (c.Priority.Value < 0) Reg.DeleteValue(Hive.HKLM, d.AffKey, "DevicePriority"); else Reg.Set(Hive.HKLM, d.AffKey, "DevicePriority", RegistryValueKind.DWord, c.Priority.Value); }
            if (c.Policy.HasValue) { if (c.Policy.Value < 0) Reg.DeleteValue(Hive.HKLM, d.AffKey, "DevicePolicy"); else Reg.Set(Hive.HKLM, d.AffKey, "DevicePolicy", RegistryValueKind.DWord, c.Policy.Value); }
            if (c.Mask.HasValue)
            {
                if (c.Mask.Value == 0) Reg.DeleteValue(Hive.HKLM, d.AffKey, "AssignmentSetOverride");
                else { var b = new byte[8]; for (int i = 0; i < 8; i++) b[i] = (byte)(c.Mask.Value >> (8 * i)); Reg.Set(Hive.HKLM, d.AffKey, "AssignmentSetOverride", RegistryValueKind.Binary, b); }
            }
        }

        // Restarts the device so the new interrupt setup takes effect. Null on success, else the reason.
        static string Restart(IrqDevice d)
        {
            string o = Shell.Run("pnputil.exe", "/restart-device \"" + InstanceId(d.Key) + "\"", true, 60000) ?? "";
            if (o.IndexOf("reboot", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("restart is required", StringComparison.OrdinalIgnoreCase) >= 0) return "the device can only change after a Windows restart";
            if (o.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0) return "pnputil could not restart it: " + o.Trim().Split('\n').Last().Trim();
            for (int w = 0; w < 6; w++) Thread.Sleep(500);   // let the driver come back
            return null;
        }
        static string Set(IrqDevice d, IrqConfig c) { Write(d, c); return Restart(d); }

        static string DriverOf(IrqDevice d)
        {
            string img = (Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\" + d.Service, "ImagePath") ?? "").ToString();
            return DriverFile(img, d.Service);
        }

        // One sample: mean µs per DPC/ISR of the driver, and its worst call. -1 = too few calls to judge.
        static double Sample(string driver, int secs, out double worst, out string err)
        {
            worst = 0;
            var l = DriverTrace.Run(secs, out err);
            if (l == null) return -1;
            var s = l.FirstOrDefault(x => string.Equals(x.Driver, driver, StringComparison.OrdinalIgnoreCase));
            if (s == null || s.Dpcs + s.Isrs < 50) { err = null; return -1; }
            worst = s.MaxUs;
            return (s.DpcTotalUs + s.IsrTotalUs) / (s.Dpcs + s.Isrs);
        }

        // ---------- backup and recovery ----------

        static void SaveOriginal(IrqDevice d, IrqConfig c)
        {
            try
            {
                var lines = File.Exists(BackupFile) ? File.ReadAllLines(BackupFile).ToList() : new List<string>();
                if (lines.Any(l => l.StartsWith(d.Key + "\t", StringComparison.OrdinalIgnoreCase))) return;   // the first original stays
                lines.Add(Line(d, c)); File.WriteAllLines(BackupFile, lines);
            }
            catch (Exception ex) { Log.W("Interrupt tuner: backup: {0}", ex.Message); }
        }
        public static bool HasBackup { get { try { return File.Exists(BackupFile) && File.ReadAllLines(BackupFile).Any(l => l.Trim().Length > 0); } catch { return false; } } }

        static void Pending(string key)
        {
            try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\ArchTweaker")) if (key == null) k.DeleteValue(Marker, false); else k.SetValue(Marker, key); }
            catch (Exception ex) { Log.W("Interrupt tuner: progress marker: {0}", ex.Message); }
        }

        // 3.27.1: the backup decides what the elevated app writes to device keys: only an administrator-owned file counts
        static bool BackupTrusted() { return File.Exists(BackupFile) && Secure.TrustedData(BackupFile, "the interrupt tuner backup"); }

        static IrqDevice Find(string key) { return Irq.List().FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)); }
        static IrqConfig Original(string key)
        {
            try
            {
                if (!BackupTrusted()) return null;
                foreach (var l in File.ReadAllLines(BackupFile)) { string k; IrqConfig c; if (ParseLine(l, out k, out c) && string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return c; }
            }
            catch { }
            return null;
        }

        // At start: ArchTweaker closed during a test. The device under test goes back to how it was before that device's test.
        public static void Recover()
        {
            try
            {
                string key = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", Marker) as string;
                if (string.IsNullOrEmpty(key)) return;
                var d = Find(key); var c = Original(key);
                if (d != null && c != null) { string e = Set(d, c); Log.Say("ArchTweaker closed while the interrupt tuner tested {0}. ArchTweaker put its original settings back{1}.", d.Name, e == null ? "" : " (" + e + ")"); }
                Pending(null);
            }
            catch (Exception ex) { Log.W("Interrupt tuner recovery: {0}", ex.Message); }
        }

        // Puts every device in the backup file back to its original settings, then empties the file.
        public static int RestoreAll(out string errors)
        {
            int n = 0; var errs = new List<string>();
            try
            {
                var keep = new List<string>();   // 3.27.1: lines that did not go back stay, so Put back can try again
                if (BackupTrusted())
                    foreach (var l in File.ReadAllLines(BackupFile))
                    {
                        string k; IrqConfig c; if (!ParseLine(l, out k, out c)) continue;
                        var d = Find(k); if (d == null) { errs.Add(k + ": not present"); keep.Add(l); continue; }
                        string e = Set(d, c);
                        if (e != null) { errs.Add(d.Name + ": " + e); keep.Add(l); } else n++;
                    }
                if (keep.Count == 0) { if (File.Exists(BackupFile)) File.Delete(BackupFile); }
                else File.WriteAllLines(BackupFile, keep.ToArray());
            }
            catch (Exception ex) { errs.Add(ex.Message); }
            errors = errs.Count == 0 ? null : string.Join("; ", errs.ToArray());
            Log.W("Interrupt tuner: original settings put back on {0} device(s){1}", n, errors == null ? "" : " (" + errors + ")");
            return n;
        }

        // ---------- the run ----------

        public static IrqTuneResult Run(List<IrqDevice> devs, int secs, int rounds, Action<string> say)
        {
            Cancel = false;
            var res = new IrqTuneResult { Secs = secs, Rounds = rounds };
            var cpu = HwProbe.Cpu;
            var cpus = cpu == null ? new List<int>() : PinCpus(cpu.FastMask, cpu.Smt, cpu.Threads);
            Log.W("Interrupt tuner: {0} device(s), {1} round(s) × {2} s, pin candidates {3}", devs.Count, rounds, secs, string.Join(",", cpus.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));
            for (int n = 0; n < devs.Count; n++)
            {
                var d = devs[n];
                var dr = new IrqDeviceResult { Dev = d, Driver = DriverOf(d) };
                res.Devices.Add(dr);
                if (Cancel || res.Error != null) { dr.Note = "not tested (stopped)"; continue; }
                var orig = Read(d);
                SaveOriginal(d, orig);
                Pending(d.Key);
                var current = orig.Clone();
                try
                {
                    // first check: is the device busy enough to measure?
                    say(L.F("Device {0} of {1}: {2} · checking activity…", n + 1, devs.Count, d.Name));
                    double w0; string err;
                    double probe = Sample(dr.Driver, secs, out w0, out err);
                    if (err != null) { res.Error = err; dr.Note = err; break; }
                    if (probe < 0) { dr.Note = L.F("{0} had too few interrupts to measure. Use the device during the test (move the mouse, stream, play audio).", dr.Driver); continue; }

                    foreach (var cfg in Configs(d, cpus))
                    {
                        if (Cancel) break;
                        var tr = new IrqTrial { Setting = cfg.Name };
                        dr.Trials.Add(tr);
                        var trial = current.Clone();
                        if (cfg.Msi.HasValue) trial.Msi = cfg.Msi; if (cfg.Priority.HasValue) trial.Priority = cfg.Priority;
                        if (cfg.Policy.HasValue) trial.Policy = cfg.Policy; if (cfg.Mask.HasValue) trial.Mask = cfg.Mask;
                        string e = null; double wb = 0, wt = 0;
                        for (int r = 1; r <= rounds && e == null && !Cancel; r++)
                            foreach (bool on in r % 2 == 1 ? new[] { false, true } : new[] { true, false })
                            {
                                if (Cancel) break;
                                say(L.F("Device {0} of {1}: {2} · {3} · round {4} of {5}, {6}", n + 1, devs.Count, d.Name, L.T(cfg.Name), r, rounds, on ? L.T("new setting") : L.T("current setting")));
                                e = Set(d, on ? trial : current);
                                if (e != null) break;
                                double w; string se;
                                double v = Sample(dr.Driver, secs, out w, out se);
                                if (se != null) { e = se; break; }
                                if (v < 0) { e = "too few interrupts during this sample"; break; }
                                if (on) { tr.Try.Add(v); wt = Math.Max(wt, w); } else { tr.Base.Add(v); wb = Math.Max(wb, w); }
                            }
                        tr.WorstBase = wb; tr.WorstTry = wt;
                        if (e != null || Cancel || tr.Base.Count < 2 || tr.Try.Count < 2)
                        {
                            tr.Note = e != null ? "stopped: " + e : "not finished";
                            string back = Set(d, current); if (back != null) Log.W("Interrupt tuner: {0}: {1}", d.Name, back);
                            if (e != null && e.StartsWith("the device can only change", StringComparison.Ordinal)) { dr.Note = e; break; }
                            continue;
                        }
                        Decide(tr);
                        if (tr.Kept) current = trial;
                        string b2 = Set(d, current); if (b2 != null) Log.W("Interrupt tuner: {0}: {1}", d.Name, b2);
                        Log.W("Interrupt tuner: {0} · {1}: {2:0.00} → {3:0.00} µs ({4:+0.0;-0.0} %) → {5}", d.Name, cfg.Name, tr.MeanBase, tr.MeanTry, tr.DiffPct, tr.Kept ? "kept" : "back");
                    }
                }
                catch (Exception ex) { dr.Note = ex.Message; try { Set(d, current); } catch { } }
                finally { Pending(null); }
            }
            res.Cancelled = Cancel;
            try { res.Report = WriteReport(res); } catch (Exception ex) { Log.W("Interrupt tuner report: {0}", ex.Message); }
            return res;
        }

        static string N(double v, string f) { return v.ToString(f, CultureInfo.InvariantCulture); }

        public static string WriteReport(IrqTuneResult r)
        {
            var sb = new StringBuilder();
            sb.Append(ReportHtml.Head("ArchTweaker interrupt tuner " + r.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
            sb.Append("<h1>Interrupt tuner</h1><div class=mut>" + ReportHtml.Esc(r.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)) + " · ArchTweaker " + ReportHtml.Esc(Program.Version) + " · " + r.Rounds + " × " + r.Secs + " s per side</div>");
            sb.Append("<p>Metric: the device driver's mean time per DPC and ISR (µs, lower is better). A setting stays only when it is at least 5 % faster, passes Welch's t-test at 95 %, and its worst call is not over 25 % slower."
                + (r.Cancelled ? " <span class=bad>The run was stopped before the end.</span>" : "") + (r.Error != null ? " <span class=bad>" + ReportHtml.Esc(r.Error) + "</span>" : "") + "</p>");
            foreach (var d in r.Devices)
            {
                sb.Append("<h2>" + ReportHtml.Esc(d.Dev.Name) + " <span class=mut>(" + ReportHtml.Esc(d.Dev.Kind) + ", " + ReportHtml.Esc(d.Driver) + ")</span></h2>");
                if (d.Note.Length > 0) sb.Append("<div class=mut>" + ReportHtml.Esc(d.Note) + "</div>");
                if (d.Trials.Count == 0) continue;
                sb.Append("<table><tr><th>Setting</th><th class=n>Current</th><th class=n>New</th><th class=n>Change</th><th class=n>Worst current</th><th class=n>Worst new</th><th>Result</th></tr>");
                foreach (var t in d.Trials)
                {
                    bool done = t.Base.Count >= 2 && t.Try.Count >= 2;
                    string cls = t.Kept ? "ok" : t.Note == "made it worse" ? "bad" : "mut";
                    sb.Append("<tr><td>" + ReportHtml.Esc(t.Setting) + "</td>" + (done ? "<td class=n>" + N(t.MeanBase, "0.00") + " µs</td><td class=n>" + N(t.MeanTry, "0.00") + " µs</td><td class=n>" + N(t.DiffPct, "+0.0;-0.0") + " %</td><td class=n>" + N(t.WorstBase, "0") + " µs</td><td class=n>" + N(t.WorstTry, "0") + " µs</td>"
                        : "<td class=n>—</td><td class=n>—</td><td class=n>—</td><td class=n>—</td><td class=n>—</td>") + "<td class=" + cls + ">" + ReportHtml.Esc(t.Kept ? "kept" : t.Note) + "</td></tr>");
                }
                sb.Append("</table>");
            }
            sb.Append("<p class=mut>Interrupt tuner → Put back the original settings restores every tested device. Undo everything also covers these values.</p></body></html>");
            string file = Path.Combine(Bench.ReportDir, "irq_" + r.When.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".html");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            return file;
        }
    }

    public class IrqTunerPage : Panel, IPage
    {
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, CheckBoxes = true, MultiSelect = false };
        readonly ComboBox secs = Theme.Drop(70, "Seconds per sample");
        readonly ComboBox rounds = Theme.Drop(60, "Rounds");
        readonly Button runBtn, stopBtn, restoreBtn;
        readonly Label status, est;
        bool running;

        public IrqTunerPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("Device", 300); lv.Columns.Add("Kind", 90); lv.Columns.Add("Driver", 130); lv.Columns.Add("MSI", 60); lv.Columns.Add("Priority", 70); lv.Columns.Add("CPU", 70); lv.Columns.Add("Settings to try", 300);
            lv.ItemChecked += delegate { Estimate(); };
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.BarLabel("Seconds per sample:")); secs.Items.AddRange(new object[] { "5 s", "10 s", "20 s" }); secs.SelectedIndex = 1; bar.Controls.Add(secs);
            bar.Controls.Add(Theme.BarLabel("Rounds:")); rounds.Items.AddRange(new object[] { "2", "3", "4" }); rounds.SelectedIndex = 1; bar.Controls.Add(rounds);
            secs.SelectedIndexChanged += delegate { Estimate(); }; rounds.SelectedIndexChanged += delegate { Estimate(); };
            est = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.FS, Margin = new Padding(4, 8, 0, 0), UseMnemonic = false, Tag = "notr" };
            bar.Controls.Add(est);
            var bar2 = Theme.Bar();
            runBtn = Theme.Lead("Start tuning", delegate { Run(); }); bar2.Controls.Add(runBtn);
            stopBtn = Theme.Btn("Stop", delegate { IrqTuner.Cancel = true; status.Text = L.T("Stopping after this sample…"); }); stopBtn.Enabled = false; bar2.Controls.Add(stopBtn);
            restoreBtn = Theme.Btn("Put back the original settings", delegate { Restore(); }); bar2.Controls.Add(restoreBtn);
            bar2.Controls.Add(Theme.Btn("Refresh", delegate { Fill(); }));
            status = Theme.StatusText();
            bar2.Controls.Add(status);
            Controls.Add(lv);
            Controls.Add(Theme.Note("The graphics card is not ticked: its restart closes running games, so test it with no game open."));
            Controls.Add(Theme.Note("Keep using the ticked devices during the test: move the mouse, stream a video, play audio. Each device restarts many times, and the network drops for a moment each time."));
            Controls.Add(Theme.Note("For each device, ArchTweaker tries MSI mode, high interrupt priority and two fast cores. It keeps a setting only when the device driver gets measurably faster. ArchTweaker never changes storage controllers."));
            Controls.Add(bar2); Controls.Add(bar);
            Skin.SetEmpty(lv, "Loading devices…");
        }
        int Secs { get { return secs.SelectedIndex == 0 ? 5 : secs.SelectedIndex == 2 ? 20 : 10; } }
        int Rounds { get { return rounds.SelectedIndex + 2; } }

        public void OnShow() { Fill(); }
        public void Filter(string q) { }

        void Fill()
        {
            if (running) return;
            restoreBtn.Enabled = IrqTuner.HasBackup;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<IrqDevice> l; List<int> cpus;
                try
                {
                    l = Irq.List().Where(d => d.Kind == "Graphics" || d.Kind == "Network" || d.Kind == "USB" || d.Kind == "Audio").ToList();
                    var c = HwProbe.Cpu; cpus = c == null ? new List<int>() : IrqTuner.PinCpus(c.FastMask, c.Smt, c.Threads);
                }
                catch (Exception ex) { Log.W("Interrupt tuner: {0}", ex.Message); l = new List<IrqDevice>(); cpus = new List<int>(); }
                if (!IsHandleCreated || IsDisposed) return;
                try { BeginInvoke((Action)(() => Show(l, cpus))); } catch (InvalidOperationException) { }
            });
        }
        void Show(List<IrqDevice> l, List<int> cpus)
        {
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var d in l)
            {
                string pri = "default";
                try { object p = Reg.Get(Hive.HKLM, d.AffKey, "DevicePriority"); if (p is int) pri = (int)p == 3 ? "high" : (int)p == 1 ? "low" : "normal"; } catch { }
                var cfgs = IrqTuner.Configs(d, cpus);
                var it = new ListViewItem(new[] { d.Name, L.T(d.Kind), IrqTuner.DriverFile((Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Services\" + d.Service, "ImagePath") ?? "").ToString(), d.Service),
                    !d.HasMsiKey ? "—" : d.Msi ? L.T("on") : L.T("off"), L.T(pri), d.PinnedCpu >= 0 ? d.PinnedCpu.ToString(CultureInfo.InvariantCulture) : L.T("any"),
                    string.Join(", ", cfgs.Select(c => L.T(c.Name)).ToArray()) }) { Tag = d };
                it.Checked = d.Kind != "Graphics" && cfgs.Count > 0;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
            Skin.Loaded(lv, "ArchTweaker found no PCI device with interrupt settings.");
            Estimate();
        }

        void Estimate()
        {
            int samples = 0;
            foreach (ListViewItem it in lv.CheckedItems) { var d = it.Tag as IrqDevice; if (d != null) samples += 1 + IrqTuner.Configs(d, new[] { 2, 4 }).Count * Rounds * 2; }
            est.Text = samples == 0 ? "" : L.F("About {0} min", Math.Max(1, (int)Math.Ceiling(samples * (Secs + 4) / 60.0)));
        }

        void Run()
        {
            if (running) return;
            var devs = lv.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag as IrqDevice).Where(d => d != null).ToList();
            if (devs.Count == 0) { Dlg.Error("No device ticked", "Tick at least one device to tune."); return; }
            if (OSInfo.Build < 19041) { Dlg.Error("Windows is too old", "The interrupt tuner restarts devices with pnputil /restart-device, which needs Windows 10 version 2004 or newer."); return; }
            bool gpu = devs.Any(d => d.Kind == "Graphics");
            if (!Dlg.Confirm("Start the interrupt tuner?", (gpu ? L.T("The graphics card is ticked: close every game first. The screen goes black for a moment at each restart.") + "\n\n" : "")
                + L.T("Each ticked device restarts many times. ArchTweaker saves the original settings, and Put back the original settings restores them."), "Start tuning", destructive: false)) return;
            int s = Secs, r = Rounds;
            running = true; runBtn.Enabled = false; stopBtn.Enabled = true; restoreBtn.Enabled = false; lv.Enabled = false;
            IrqTuneResult res = null;
            Action<string> say = Theme.SayTo(this, status);
            MainForm.Instance.Busy("Tuning interrupts…", delegate { res = IrqTuner.Run(devs, s, r, say); }, delegate
            {
                running = false; runBtn.Enabled = true; stopBtn.Enabled = false; lv.Enabled = true; status.Text = "";
                Fill();
                if (res == null) return;
                int kept = res.Devices.Sum(d => d.Trials.Count(t => t.Kept));
                Log.Say("Interrupt tuner done: {0} setting(s) kept.", kept);
                if (res.Error != null) Log.Say("Interrupt tuner: {0}", res.Error);
                if (res.Report != null) Shell.OpenVisible("explorer.exe", "\"" + res.Report + "\"");
            });
        }

        void Restore()
        {
            if (running) return;
            if (!Dlg.Confirm("Put back the original settings?", L.T("ArchTweaker writes the interrupt settings from before the first tuning run back to each tested device and restarts it."), "Put back", destructive: false)) return;
            string err = null; int n = 0;
            MainForm.Instance.Busy("Putting back the original settings…", delegate { n = IrqTuner.RestoreAll(out err); }, delegate
            {
                Log.Say("Original interrupt settings put back on {0} device(s).", n);
                if (err != null) Dlg.Error("Some devices did not change", err);
                Fill();
            });
        }
    }
}
