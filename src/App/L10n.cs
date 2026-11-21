// ArchTweaker 3.0: interface language. English is the source text; every other language is a table
// "English text → translation" (lang/<code>.tsv, compiled into L10n<Code>.cs by tools/gen_l10n.py).
//
// How text gets translated without touching every call site:
//  - L.Apply(control) walks a control tree once, translates Text / AccessibleName / AccessibleDescription,
//    and hooks TextChanged and ControlAdded so text set later (status labels, rebuilt lists) is translated too.
//  - Owner-drawn controls (tweak rows, nav, toasts, check rows) call L.T when they paint.
//  - Dlg, Notify and the status line translate their messages.
//  - Text built from pieces ("Selected: 94 MB in 8 items") is matched by pattern entries with {0}, {1}…
//    The translation may pick a plural form: {1|элемент|элемента|элементов} (Russian one / few / many).
// Data stays English: registry values, preset keys, page keys and the log file are never translated.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ArchTweaker
{
    public static class L
    {
        // 3.39: every generated table (src/Generated/L10n<Code>.cs from lang/<code>.tsv) is a language; English first
        static readonly KeyValuePair<string, Type>[] Tables = FindTables();
        public static readonly string[] Codes = new[] { "en" }.Concat(Tables.Select(t => t.Key)).ToArray();
        public static readonly string[] Names = new[] { "English" }.Concat(Tables.Select(t => NameOf(t.Value, t.Key))).ToArray();
        public static string Lang = "en";
        public static bool Active { get { return Lang != "en"; } }

        static Dictionary<string, string> exact = new Dictionary<string, string>(StringComparer.Ordinal);
        static List<Pattern> patterns = new List<Pattern>();
        // Pieces of text that code glues together ("Freed " + size + "."): entries that start or end with a space,
        // or end with ":". Used last, by replacing each piece inside the string, longest first.
        static List<KeyValuePair<string, string>> fragments = new List<KeyValuePair<string, string>>();
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly HashSet<string> missing = new HashSet<string>(StringComparer.Ordinal);
        static readonly object gate = new object();

        class Pattern { public Regex Rx; public string To; public int Literal; }

        static L()
        {
            object o = null;
            try { o = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "Language"); } catch { }
            string code = o == null ? null : o.ToString();
            if (code == null || Array.IndexOf(Codes, code) < 0)
            {
                string ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;   // 3.39: any language that has a table
                code = Array.IndexOf(Codes, ui) >= 0 ? ui : "en";
            }
            Use(code);
        }

        public static void Save(string code)
        {
            using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\ArchTweaker")) k.SetValue("Language", code);
        }

        public static void Use(string code)
        {
            lock (gate)
            {
                Lang = Array.IndexOf(Codes, code) < 0 ? "en" : code;
                exact = new Dictionary<string, string>(StringComparer.Ordinal); patterns = new List<Pattern>(); fragments = new List<KeyValuePair<string, string>>(); cache.Clear(); missing.Clear();
                string t = TableFor(Lang); if (t != null) Load(t);
            }
        }

        static KeyValuePair<string, Type>[] FindTables()
        {
            try
            {
                return typeof(L).Assembly.GetTypes()
                    .Where(t => t.Namespace == "ArchTweaker" && t.Name.Length == 6 && t.Name.StartsWith("L10n", StringComparison.Ordinal)
                        && t.GetField("Table", BindingFlags.Public | BindingFlags.Static) != null)
                    .Select(t => new KeyValuePair<string, Type>(t.Name.Substring(4).ToLowerInvariant(), t))
                    .OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
            }
            catch { return new KeyValuePair<string, Type>[0]; }
        }
        static string NameOf(Type t, string code)
        {
            var f = t.GetField("Name", BindingFlags.Public | BindingFlags.Static);
            return f != null ? (string)f.GetValue(null) : code;
        }
        public static string TableFor(string code)
        {
            foreach (var p in Tables) if (p.Key == code) return (string)p.Value.GetField("Table", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            return null;
        }

        // Table format: one entry per line, "English<TAB>Translation". \n and \t are escaped. Lines starting with # are comments.
        public static void Load(string table)
        {
            foreach (var raw in table.Split(new[] { '\n' }))
            {
                string line = raw.TrimEnd(new[] { '\r' });
                if (line.Length == 0 || line[0] == '#') continue;
                int tab = line.IndexOf('\t'); if (tab <= 0) continue;
                string en = Unescape(line.Substring(0, tab)), to = Unescape(line.Substring(tab + 1));
                if (to.Length == 0) continue;
                if (Slot.IsMatch(en)) AddPattern(en, to);
                else exact[en] = to;
                bool piece = en.Length >= 3 && (char.IsWhiteSpace(en[0]) || char.IsWhiteSpace(en[en.Length - 1]) || en.EndsWith(":", StringComparison.Ordinal) || en.StartsWith("(", StringComparison.Ordinal));
                if (piece && en.Trim().Length >= 2 && !Slot.IsMatch(en)) fragments.Add(new KeyValuePair<string, string>(en, to));
            }
            patterns = patterns.OrderByDescending(p => p.Literal).ToList();   // most specific pattern first
            fragments = fragments.OrderByDescending(f => f.Key.Length).ToList();
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == 't') { sb.Append('\t'); i++; continue; }
                    if (n == 'r') { sb.Append('\r'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
        static readonly Regex Slot = new Regex(@"\{(\d)(?:[,:][^}|]*)?\}");

        static void AddPattern(string en, string to)
        {
            var sb = new StringBuilder("^"); int lit = 0, i = 0;
            foreach (Match m in Slot.Matches(en))
            {
                string part = en.Substring(i, m.Index - i); lit += part.Length;
                sb.Append(Regex.Escape(part)).Append("(?<g").Append(m.Groups[1].Value).Append(">.+?)");
                i = m.Index + m.Length;
            }
            string tail = en.Substring(i); lit += tail.Length;
            sb.Append(Regex.Escape(tail)).Append("$");
            patterns.Add(new Pattern { Rx = new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant), To = to, Literal = lit });
        }

        // Translate one piece of interface text. Unknown text comes back unchanged.
        public static string T(string s)
        {
            if (!Active || string.IsNullOrEmpty(s)) return s;
            lock (gate)
            {
                string r;
                if (exact.TryGetValue(s, out r)) return r;
                if (cache.TryGetValue(s, out r)) return r;
                r = Resolve(s);
                if (cache.Count > 4000) cache.Clear();
                cache[s] = r;
                if ((object)r == (object)s && s.Any(ch => ch < 128 && char.IsLetter(ch)) && !s.Any(ch => ch >= 0x400 && ch <= 0x4FF)) missing.Add(s);
                return r;
            }
        }

        static string Resolve(string s)
        {
            // keep surrounding spaces and a trailing ":" / "…"
            string core = s.Trim(); string r;
            if (core.Length != s.Length && core.Length > 0 && exact.TryGetValue(core, out r))
            {
                int at = s.IndexOf(core, StringComparison.Ordinal);
                return s.Substring(0, at) + r + s.Substring(at + core.Length);
            }
            foreach (var suffix in new[] { ":", "…", "..." })
                if (core.Length > suffix.Length && core.EndsWith(suffix, StringComparison.Ordinal) && exact.TryGetValue(core.Substring(0, core.Length - suffix.Length), out r))
                    return s.Replace(core, r + suffix);
            // several lines (lists, tooltips): translate each line on its own
            if (s.IndexOf('\n') >= 0)
            {
                var lines = s.Split(new[] { '\n' });
                bool changed = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i], cr = ln.EndsWith("\r") ? "\r" : "", body = cr.Length > 0 ? ln.Substring(0, ln.Length - 1) : ln, t = T(body);
                    if (t != body) { lines[i] = t + cr; changed = true; }
                }
                if (changed) return string.Join("\n", lines);
            }
            // bullet: "• text"
            if (s.StartsWith("• ", StringComparison.Ordinal) && s.Length > 2) { string t = T(s.Substring(2)); if (t != s.Substring(2)) return "• " + t; }
            // specific patterns first, then known pieces, then the generic "{0}: {1}" / "{0} · {1}" splitters
            string viaPattern = ApplyPatterns(s, true);
            if (viaPattern != null) return viaPattern;
            if (s.IndexOf(" · ", StringComparison.Ordinal) >= 0 || s.IndexOf(" — ", StringComparison.Ordinal) >= 0)
            {
                string split = ApplyPatterns(s, false);   // joined message: translate each part
                if (split != null && split != s) return split;
            }
            string res = s; bool any = false;
            foreach (var f in fragments)
                if (res.IndexOf(f.Key, StringComparison.Ordinal) >= 0) { res = res.Replace(f.Key, f.Value); any = true; }
            if (any) return res;
            return ApplyPatterns(s, false) ?? s;
        }

        static string ApplyPatterns(string s, bool specific)
        {
            foreach (var p in patterns)
            {
                if ((p.Literal > 4) != specific) continue;
                var m = p.Rx.Match(s);
                if (!m.Success) continue;
                if (specific && !p.Rx.ToString().Contains("·") && Glued(m)) continue;   // let the " · " splitter handle joined messages
                return Regex.Replace(p.To, @"\{(\d)(?:[,:][^}|]*)?(\|[^}]*)?\}", delegate (Match x)
                {
                    var g = m.Groups["g" + x.Groups[1].Value];
                    string v = g.Success ? g.Value : "";
                    if (x.Groups[2].Success) return Plural(v, x.Groups[2].Value.Substring(1).Split(new[] { '|' }));
                    return T(v);   // a piece may itself be translatable ("Gaming", "On")
                });
            }
            return null;
        }

        static bool Glued(Match m)
        {
            for (int i = 1; i < m.Groups.Count; i++) { string v = m.Groups[i].Value; if (v.IndexOf(" · ", StringComparison.Ordinal) >= 0) return true; }
            return false;
        }

        // Russian plural: one (1, 21, 31…), few (2-4, 22-24…), many (0, 5-20, 25-30…). Picks from "one|few|many".
        public static string Plural(string number, string[] forms)
        {
            if (forms.Length == 0) return "";
            long n; string digits = new string(number.Where(char.IsDigit).ToArray());
            if (!long.TryParse(digits, out n) || forms.Length < 3) return forms[0];
            long m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return forms[0];
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return forms[1];
            return forms[2];
        }

        public static string F(string format, params object[] args) { return string.Format(T(format), args); }

        // Text that ran through T but had no entry — for finding gaps (Export diagnostics writes it).
        public static List<string> Missing() { lock (gate) return missing.OrderBy(x => x).ToList(); }

        // ---- control trees ----
        [ThreadStatic] static bool busy;
        public static void Apply(Control c)
        {
            if (!Active || c == null) return;
            if (c.Tag as string == "notr") return;
            // inputs and combo boxes: their Text is data (a combo's text selects an item), so it's never set here
            bool input = c is TextBoxBase || c is ComboBox || c is UpDownBase;
            if (!input) SetText(c);
            if (!string.IsNullOrEmpty(c.AccessibleName)) c.AccessibleName = T(c.AccessibleName);
            if (!string.IsNullOrEmpty(c.AccessibleDescription)) c.AccessibleDescription = T(c.AccessibleDescription);
            var cb = c as ComboBox;
            if (cb != null) { cb.FormattingEnabled = true; cb.Format -= ComboFormat; cb.Format += ComboFormat; }
            var lv = c as ListView;
            if (lv != null) foreach (ColumnHeader h in lv.Columns) h.Text = T(h.Text);
            var dg = c as DataGridView;
            if (dg != null) { foreach (DataGridViewColumn col in dg.Columns) col.HeaderText = T(col.HeaderText); dg.ColumnAdded -= GridColumn; dg.ColumnAdded += GridColumn; }
            var ts = c as ToolStrip;
            if (ts != null) Apply(ts.Items);
            if (c.ContextMenuStrip != null) Apply(c.ContextMenuStrip.Items);
            if (!input) { c.TextChanged -= Changed; c.TextChanged += Changed; }
            c.ControlAdded -= Added; c.ControlAdded += Added;
            foreach (Control k in c.Controls) Apply(k);
        }
        public static void Apply(ToolStripItemCollection items)
        {
            if (!Active) return;
            foreach (ToolStripItem it in items)
            {
                if (!(it is ToolStripTextBox) && !(it is ToolStripComboBox)) it.Text = T(it.Text);
                if (!string.IsNullOrEmpty(it.ToolTipText)) it.ToolTipText = T(it.ToolTipText);
                var dd = it as ToolStripDropDownItem;
                if (dd != null) Apply(dd.DropDownItems);
            }
        }
        static void SetText(Control c)
        {
            if (busy) return;
            string t = c.Text, r = T(t);
            if (r == t) return;
            busy = true; try { c.Text = r; } finally { busy = false; }
        }
        static void Changed(object s, EventArgs e) { SetText((Control)s); }
        static void Added(object s, ControlEventArgs e) { Apply(e.Control); }
        static void ComboFormat(object s, ListControlConvertEventArgs e) { var str = e.Value as string; if (str != null) e.Value = T(str); }
        static void GridColumn(object s, DataGridViewColumnEventArgs e) { e.Column.HeaderText = T(e.Column.HeaderText); }
    }
}
