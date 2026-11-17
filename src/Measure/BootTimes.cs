// ArchTweaker: Boot times from the Diagnostics-Performance log, and the Boot times page.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace ArchTweaker
{
    public class BootRun
    {
        public DateTime When; public int BootMs, MainPathMs, PostBootMs;
        public List<KeyValuePair<string, int>> Slow = new List<KeyValuePair<string, int>>();
    }

    public static class BootTimes
    {
        // 3.27.1: no DTDs and no external entities, like Update.cs and NipFiles.cs
        static XmlDocument Load(string xml)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var r = XmlReader.Create(new System.IO.StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc.Load(r);
            return doc;
        }

        // One event 100 (Microsoft-Windows-Diagnostics-Performance) as XML → a BootRun.
        public static BootRun Parse(string xml)
        {
            try
            {
                var doc = Load(xml);
                var ns = new XmlNamespaceManager(doc.NameTable); ns.AddNamespace("e", "http://schemas.microsoft.com/win/2004/08/events/event");
                Func<string, int> d = n => { var x = doc.SelectSingleNode("//e:Data[@Name='" + n + "']", ns); int v; return x != null && int.TryParse(x.InnerText, out v) ? v : -1; };
                var r = new BootRun { BootMs = d("BootTime"), MainPathMs = d("MainPathBootTime"), PostBootMs = d("BootPostBootTime") };
                var t = doc.SelectSingleNode("//e:TimeCreated/@SystemTime", ns);
                DateTime w; if (t != null && DateTime.TryParse(t.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out w)) r.When = w.ToLocalTime();
                return r.BootMs > 0 ? r : null;
            }
            catch { return null; }
        }

        const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";

        // wevtutil /f:xml prints the events one after another without a root element.
        public static List<string> Split(string text)
        {
            var o = new List<string>();
            if (string.IsNullOrEmpty(text)) return o;
            int i = 0;
            while (true)
            {
                int a = text.IndexOf("<Event ", i, StringComparison.Ordinal); if (a < 0) a = text.IndexOf("<Event>", i, StringComparison.Ordinal);
                if (a < 0) break;
                int b = text.IndexOf("</Event>", a, StringComparison.Ordinal); if (b < 0) break;
                o.Add(text.Substring(a, b + 8 - a)); i = b + 8;
            }
            return o;
        }

        static string Query(string xpath, int count)
        {
            return Shell.Run("wevtutil.exe", "qe \"" + LogName + "\" /q:\"" + xpath + "\" /rd:true /f:xml /c:" + count.ToString(CultureInfo.InvariantCulture), true, 30000);
        }

        // One slow-item event (101–110) as XML: the name and the time it took, or null.
        public static KeyValuePair<string, int>? SlowItem(string xml)
        {
            try
            {
                var doc = Load(xml);
                var ns = new XmlNamespaceManager(doc.NameTable); ns.AddNamespace("e", "http://schemas.microsoft.com/win/2004/08/events/event");
                var name = doc.SelectSingleNode("//e:Data[@Name='FriendlyName']", ns) ?? doc.SelectSingleNode("//e:Data[@Name='Name']", ns);
                var tt = doc.SelectSingleNode("//e:Data[@Name='TotalTime']", ns);
                int ms; if (name != null && tt != null && name.InnerText.Trim().Length > 0 && int.TryParse(tt.InnerText, out ms)) return new KeyValuePair<string, int>(name.InnerText.Trim(), ms);
            }
            catch { }
            return null;
        }
        static DateTime When(string xml)
        {
            var r = Regex.Match(xml, "SystemTime='([^']+)'|SystemTime=\"([^\"]+)\"");
            DateTime w; string v = r.Success ? (r.Groups[1].Success ? r.Groups[1].Value : r.Groups[2].Value) : null;
            return v != null && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out w) ? w.ToLocalTime() : DateTime.MinValue;
        }

        // Newest first. Events 101–110 (slow app, driver, service at start) of the last boot add the slow items.
        public static List<BootRun> Read(int max) { string e; return Read(max, out e); }
        // 3.27.1: error = why the log could not be read; an empty list without an error means Windows has no timings
        public static List<BootRun> Read(int max, out string error)
        {
            var o = new List<BootRun>(); error = null;
            try
            {
                string text = Query("*[System[(EventID=100)]]", max);
                error = QueryError(text);
                if (error != null) { Log.W("Boot times: {0}", error); return o; }
                foreach (var x in Split(text)) { var r = Parse(x); if (r != null) o.Add(r); }
                if (o.Count > 0 && o[0].When != DateTime.MinValue)
                {
                    var since = o[0].When.AddMinutes(-15);
                    foreach (var x in Split(Query("*[System[(EventID>=101 and EventID<=110)]]", 60)))
                    {
                        var w = When(x);
                        if (w != DateTime.MinValue && w < since) break;
                        var s = SlowItem(x); if (s.HasValue) o[0].Slow.Add(s.Value);
                    }
                }
                Log.W("Boot times: {0} start(s) found.", o.Count);
            }
            catch (Exception ex) { error = ex.Message; Log.W("Boot times: {0}", ex.Message); }
            return o;
        }
        // wevtutil prints XML on success; anything else (no output, "Failed to…", access denied) is an error
        public static string QueryError(string text)
        {
            if (text == null || text.Trim().Length == 0) return null;   // no events (or no answer): handled as "no timings"
            string t = text.TrimStart();
            if (t.StartsWith("<", StringComparison.Ordinal)) return null;
            return t.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
        }
    }

    public class BootTimesPage : Panel, IPage
    {
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        readonly ListView slow = new ListView { Dock = DockStyle.Bottom, Height = 170, View = View.Details, FullRowSelect = true, MultiSelect = false };
        readonly TrendChart trend = new TrendChart();
        readonly Label sum;
        public BootTimesPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv); Theme.DarkList(slow);
            lv.Columns.Add("Boot", 140); lv.Columns.Add("Total", 100); lv.Columns.Add("Until the desktop", 140); lv.Columns.Add("After the desktop", 140);
            slow.Columns.Add("Slow at the last start", 420); slow.Columns.Add("Time", 100);
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Btn("Refresh", delegate { Fill(); }));
            sum = Theme.StatusText();
            bar.Controls.Add(sum);
            Controls.Add(lv); Controls.Add(slow); Controls.Add(trend);
            Controls.Add(Theme.Note("Windows measures every start. Total = from power-on to a ready desktop. Until the desktop = until you can log on and the desktop shows. After the desktop = the start-up programs and services that load afterwards. Fast Startup boots are shorter and are not always recorded."));
            Controls.Add(bar);
            Skin.SetEmpty(lv, "Reading boot times…");
        }
        public void OnShow() { Fill(); }
        public void Filter(string q) { }
        void Fill()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err; var l = BootTimes.Read(30, out err);
                if (!IsHandleCreated || IsDisposed) return;
                try { BeginInvoke((Action)(() => Show(l, err))); } catch (InvalidOperationException) { }
            });
        }
        static string S(int ms) { return ms < 0 ? "—" : (ms / 1000.0).ToString("0.0", CultureInfo.CurrentCulture) + " s"; }
        void Show(List<BootRun> l, string err)
        {
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var r in l) lv.Items.Add(new ListViewItem(new[] { r.When.ToString("dd MMM HH:mm"), S(r.BootMs), S(r.MainPathMs), S(r.PostBootMs) }));
            lv.EndUpdate();
            Skin.Loaded(lv, err != null ? L.T("Could not read the Diagnostics-Performance log:") + " " + err : L.T("Windows has no boot timings yet. The Diagnostics-Performance log may be off."));
            slow.BeginUpdate(); slow.Items.Clear();
            if (l.Count > 0) foreach (var s in l[0].Slow.OrderByDescending(x => x.Value).Take(15)) slow.Items.Add(new ListViewItem(new[] { s.Key, S(s.Value) }));
            slow.EndUpdate();
            Skin.Loaded(slow, "Windows reported nothing slow at the last start.");
            trend.Set("Total boot time per start", "s", "0.0", Enumerable.Reverse(l).Select(r => new TrendChart.Point2 { When = r.When, V = r.BootMs / 1000.0 }).ToList());
            sum.Text = l.Count == 0 ? "" : L.F("Last start: {0} · average of {1} starts: {2}", S(l[0].BootMs), l.Count, S((int)l.Average(r => r.BootMs)));
        }
    }
}
