// ArchTweaker: Change history with before/after values, and its page.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ================= CHANGE HISTORY =================
    public class ChangeRow { public HistEntry Session; public Tweak T; public string Setting, Before, Written; }

    public static class ChangeHistory
    {

        // the registry snapshot taken right before a session: the newest one up to 2 min after it was saved
        public static string SnapshotFor(DateTime when)
        {
            try
            {
                string dir = Path.Combine(Paths.Root, "snapshots"); if (!Directory.Exists(dir)) return null;
                string best = null; DateTime bt = DateTime.MinValue;
                foreach (var f in Directory.GetFiles(dir, "apply_*.reg"))
                {
                    DateTime t;
                    if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f).Substring(6), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) continue;
                    if (t <= when.AddMinutes(2) && t >= when.AddMinutes(-30) && t > bt) { bt = t; best = f; }
                }
                return best;
            }
            catch { return null; }
        }

        // reads a .reg export into key → (value name → text). Value text is the .reg form, e.g. dword:00000001.
        // 3.32: 'wanted' = the keys to keep; every other key is skipped while reading (old snapshots can be 30 MB)
        public static Dictionary<string, Dictionary<string, string>> ParseReg(IEnumerable<string> lines, ICollection<string> wanted = null)
        {
            var o = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> cur = null; string pending = null, pname = null;
            foreach (var raw in lines)
            {
                if (raw.Length == 0) continue;
                if (cur == null && pending == null && raw[0] != '[') continue;   // inside a skipped key: cheapest test first
                string l = raw.TrimEnd();
                if (pending != null)
                {   // a hex value continues on the next line after a backslash
                    pending += l.Trim();
                    if (pending.EndsWith("\\")) { pending = pending.Substring(0, pending.Length - 1); continue; }
                    cur[pname] = pending; pending = null; continue;
                }
                if (l.StartsWith("[") && l.EndsWith("]"))
                {
                    string k = l.Substring(1, l.Length - 2);
                    if (wanted != null && !wanted.Contains(k)) { cur = null; continue; }
                    if (!o.TryGetValue(k, out cur)) o[k] = cur = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                if (cur == null || l.StartsWith(";")) continue;
                string name; int eq;
                if (l.StartsWith("@=")) { name = ""; eq = 1; }
                else if (l.StartsWith("\""))
                {
                    int end = 1; while (end < l.Length && !(l[end] == '"' && l[end - 1] != '\\')) end++;
                    name = l.Substring(1, end - 1).Replace("\\\\", "\\").Replace("\\\"", "\""); eq = l.IndexOf('=', end);
                }
                else continue;
                if (eq < 0) continue;
                string v = l.Substring(eq + 1);
                if (v.EndsWith("\\")) { pending = v.Substring(0, v.Length - 1); pname = name; continue; }
                cur[name] = v;
            }
            return o;
        }

        // .reg text → the way the app shows values (Op.Fmt style)
        public static string Pretty(string reg)
        {
            if (reg == null) return null;
            if (reg == "-") return L.T("(not set)");   // 3.32 snapshots write a value that did not exist as "name"=-
            if (reg.StartsWith("dword:")) { uint u; if (uint.TryParse(reg.Substring(6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out u)) return u + (u > 9 ? " (0x" + u.ToString("X") + ")" : "") + " dword"; }
            if (reg.StartsWith("\"")) return reg;
            if (reg.StartsWith("hex")) { int c = reg.IndexOf(':'); return "hex:" + reg.Substring(c + 1); }
            return reg;
        }

        // 3.32: a parsed snapshot is kept while its file is unchanged, so opening the page again reads nothing
        static readonly Dictionary<string, KeyValuePair<DateTime, Dictionary<string, Dictionary<string, string>>>> parsed =
            new Dictionary<string, KeyValuePair<DateTime, Dictionary<string, Dictionary<string, string>>>>(StringComparer.OrdinalIgnoreCase);
        static Dictionary<string, Dictionary<string, string>> Snapshot(string file, ICollection<string> wanted)
        {
            DateTime m;
            try { m = File.GetLastWriteTimeUtc(file); } catch { return null; }
            lock (parsed)
            {
                KeyValuePair<DateTime, Dictionary<string, Dictionary<string, string>>> c;
                if (parsed.TryGetValue(file, out c) && c.Key == m) return c.Value;
            }
            Dictionary<string, Dictionary<string, string>> d;
            try { d = ParseReg(File.ReadLines(file), wanted); } catch { d = null; }
            lock (parsed) parsed[file] = new KeyValuePair<DateTime, Dictionary<string, Dictionary<string, string>>>(m, d);
            return d;
        }
        // what the page's rows depend on: the history file and the snapshot files
        public static string Signature()
        {
            try
            {
                var sb = new StringBuilder();
                var h = new FileInfo(History.File_);
                if (h.Exists) sb.Append(h.LastWriteTimeUtc.Ticks).Append(':').Append(h.Length);
                string dir = Path.Combine(Paths.Root, "snapshots");
                if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "apply_*.reg").OrderBy(x => x)) sb.Append('|').Append(Path.GetFileName(f));
                return sb.ToString();
            }
            catch { return ""; }
        }

        public static List<ChangeRow> Build(int sessions)
        {
            var rows = new List<ChangeRow>();
            var entries = History.Entries().Take(sessions).ToList();
            // the keys each snapshot is asked about, so the reader can skip everything else
            var files = entries.ToDictionary(e => e, e => SnapshotFor(e.When));
            var wanted = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                string f = files[e]; if (f == null) continue;
                HashSet<string> w; if (!wanted.TryGetValue(f, out w)) wanted[f] = w = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in e.Items) foreach (var v in kv.Key.Ops.OfType<VOp>()) w.Add(RegText.LongHive(v.H) + "\\" + v.Path);
            }
            foreach (var s in entries)
            {
                string sf = files[s];
                var snap = sf == null ? null : Snapshot(sf, wanted[sf]);
                foreach (var kv in s.Items)
                {
                    var vops = kv.Key.Ops.OfType<VOp>().ToList();
                    if (vops.Count == 0)
                    {
                        string d = kv.Key.Ops.Count > 0 ? kv.Key.Ops[0].Describe().Split('\r', '\n')[0] : "";
                        rows.Add(new ChangeRow { Session = s, T = kv.Key, Setting = d, Before = "—", Written = L.T(kv.Value ? "applied" : "reverted") });
                        continue;
                    }
                    foreach (var v in vops)
                    {
                        string before = "—";
                        if (snap != null)
                        {
                            Dictionary<string, string> keyVals;
                            if (snap.TryGetValue(RegText.LongHive(v.H) + "\\" + v.Path, out keyVals)) { string rv; before = keyVals.TryGetValue(v.Name ?? "", out rv) ? Pretty(rv) : L.T("(not set)"); }
                            else before = L.T("(not set)");   // keys that did not exist are not exported
                        }
                        object w = kv.Value ? v.On : v.Off;
                        rows.Add(new ChangeRow { Session = s, T = kv.Key, Setting = Op.HiveName(v.H) + "\\" + v.Path + "\\" + (string.IsNullOrEmpty(v.Name) ? "(Default)" : v.Name),
                            Before = before, Written = w == null ? L.T("(deleted)") : Op.Fmt(w, v.Kind) });
                    }
                }
            }
            return rows;
        }
    }

    // 3.33: one header row per apply (newest open), the tweak name once per tweak, a search box, unchanged values muted
    public class ChangeHistoryPage : Panel, IPage
    {
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
        List<ChangeRow> rows = new List<ChangeRow>();
        readonly Button undo;
        readonly TextBox find = new TextBox { Width = 220 };
        readonly HashSet<string> open = new HashSet<string>();
        bool openSet;
        static string Key(HistEntry h) { return h.When.Ticks + ":" + h.Line; }
        public ChangeHistoryPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("When", 120); lv.Columns.Add("Tweak", 260); lv.Columns.Add("Setting", 380); lv.Columns.Add("Before", 150); lv.Columns.Add("Written", 150);
            var bar = Theme.Bar();
            undo = Theme.Lead("Undo this apply", delegate { var h = SelectedSession(); if (h != null) MainForm.Instance.UndoSession(h); });
            undo.Enabled = false;
            bar.Controls.Add(undo);
            find.AccessibleName = L.T("Search the change history");
            var ff = new SearchField(find, Theme.S(240)) { Margin = new Padding(Theme.S(8), Theme.S(5), Theme.S(8), 0) };
            find.TextChanged += delegate { Fill(); };
            bar.Controls.Add(ff);
            bar.Controls.Add(Theme.Btn("Open all", delegate { foreach (var c in rows) open.Add(Key(c.Session)); Fill(); }));
            bar.Controls.Add(Theme.Btn("Close all", delegate { open.Clear(); Fill(); }));
            bar.Controls.Add(Theme.Btn("Refresh", delegate { Load2(); }));
            bar.Controls.Add(Theme.Btn("Copy as text", delegate { Copy(); }));
            var note = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 40, ForeColor = Theme.Muted, Font = Theme.FS, Padding = new Padding(2, 4, 2, 0), UseMnemonic = false,
                Text = "Every value each apply wrote, newest first. Click an apply to open or close it. Before comes from the registry snapshot taken just before that apply. ArchTweaker keeps the 10 newest snapshots." };
            lv.SelectedIndexChanged += delegate { undo.Enabled = SelectedSession() != null; };
            lv.MouseClick += (s2, e2) => { var it = lv.GetItemAt(e2.X, e2.Y); var h = it == null ? null : it.Tag as HistEntry; if (h != null) Toggle(h); };
            lv.KeyDown += (s2, e2) => { if (e2.KeyCode == Keys.Space && lv.SelectedItems.Count > 0) { var h = lv.SelectedItems[0].Tag as HistEntry; if (h != null) { Toggle(h); e2.Handled = true; } } };
            lv.ItemActivate += delegate { var r = lv.SelectedItems.Count == 0 ? null : lv.SelectedItems[0].Tag as ChangeRow; if (r != null) AppShell.Current.ShowDetails(r.T); };
            Controls.Add(lv); Controls.Add(note); Controls.Add(bar);
            History.Changed += OnHistory; Disposed += delegate { History.Changed -= OnHistory; };
        }
        void OnHistory() { if (IsHandleCreated && !IsDisposed) try { BeginInvoke((Action)Load2); } catch (InvalidOperationException) { } }
        HistEntry SelectedSession()
        {
            if (lv.SelectedItems.Count == 0) return null;
            var t = lv.SelectedItems[0].Tag;
            return t as HistEntry ?? (t is ChangeRow ? ((ChangeRow)t).Session : null);
        }
        void Toggle(HistEntry h)
        {
            string k = Key(h);
            if (!open.Remove(k)) open.Add(k);
            Fill(k);
        }
        // 3.32: showing the page again reuses the rows while nothing changed; a reload runs in the background
        // and leaves the rest of the window usable (no Busy)
        string shownSig; bool loading;
        public void OnShow() { if (rows.Count == 0 || ChangeHistory.Signature() != shownSig) Load2(); }
        public void Filter(string q) { }
        void Load2()
        {
            if (loading) return;
            loading = true;
            if (rows.Count == 0) Skin.SetEmpty(lv, "Loading…");
            List<ChangeRow> r = null; string sig = null;
            var t = new System.Threading.Thread(() =>
            {
                try { sig = ChangeHistory.Signature(); r = ChangeHistory.Build(30); } catch (Exception ex) { Log.W("Change history: {0}", ex.Message); }
                try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() => Show(r, sig))); } catch (InvalidOperationException) { }
                finally { loading = false; }
            }) { IsBackground = true };
            t.Start();
        }
        void Show(List<ChangeRow> r, string sig)
        {
            if (IsDisposed) return;
            rows = r ?? new List<ChangeRow>(); shownSig = sig;
            if (!openSet && rows.Count > 0) { open.Add(Key(rows[0].Session)); openSet = true; }   // the newest apply starts open
            Fill();
        }
        static bool Match(ChangeRow c, string[] words)
        {
            string hay = L.T(c.T.Title) + " " + c.T.Title + " " + c.Setting + " " + c.Before + " " + c.Written;
            return words.All(w => hay.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        void Fill(string keepKey = null)
        {
            var words = find.Text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool searching = words.Length > 0;
            var items = new List<ListViewItem>(); ListViewItem keep = null;
            foreach (var g in rows.GroupBy(c => c.Session))
            {
                var h = g.Key; string k = Key(h);
                var match = searching ? g.Where(c => Match(c, words)).ToList() : g.ToList();
                if (searching && match.Count == 0) continue;
                bool isOpen = searching || open.Contains(k);
                int tweaks = g.Select(c => c.T).Distinct().Count();
                var head = new ListViewItem(new[] { h.When.ToString("dd MMM HH:mm"), (isOpen ? "▾  " : "▸  ") + (tweaks == 1 ? L.T("1 tweak") : L.F("{0} tweaks", tweaks)) + " · " + (g.Count() == 1 ? L.T("1 value") : L.F("{0} values", g.Count())), "", "", "" })
                    { Tag = h, Font = Theme.FB, ForeColor = Theme.Strong };
                items.Add(head);
                if (k == keepKey) keep = head;
                if (!isOpen) continue;
                Tweak prev = null;
                foreach (var c in match)
                {
                    bool same = c.Before == c.Written;
                    var it = new ListViewItem(new[] { "", c.T == prev ? "" : L.T(c.T.Title), c.Setting, c.Before, c.Written }) { Tag = c, ForeColor = same ? Theme.Muted : Theme.Text };
                    prev = c.T;
                    items.Add(it);
                }
            }
            lv.BeginUpdate(); lv.Items.Clear(); lv.Items.AddRange(items.ToArray());
            if (keep != null) { keep.Selected = true; keep.Focused = true; keep.EnsureVisible(); }
            lv.EndUpdate();
            Skin.SetEmpty(lv, rows.Count == 0 ? "No changes yet. Each time you click Apply, every value it writes shows here." : items.Count == 0 ? "Nothing matches the search." : null);
            undo.Enabled = SelectedSession() != null;
        }
        void Copy()
        {
            var sb = new StringBuilder();
            foreach (var c in rows) sb.Append(c.Session.When.ToString("yyyy-MM-dd HH:mm")).Append('\t').Append(c.T.Title).Append('\t').Append(c.Setting).Append('\t').Append(c.Before).Append('\t').Append(c.Written).Append("\r\n");
            if (sb.Length > 0) try { Clipboard.SetText(sb.ToString()); AppShell.Current.Notify("Change history copied", Toast.Kind.Ok); } catch { }
        }
    }
}
