// ArchTweaker: Apply history, history list and the review-before-apply dialog.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= APPLY HISTORY (undo last apply) =================
    public static class History
    {
        public static string File_ { get { return Path.Combine(Paths.Root, "history.txt"); } }
        // 3.13: raised after any change to the file, on the thread that changed it
        public static event Action Changed;
        static void Raise() { var c = Changed; if (c != null) try { c(); } catch { } }
        public static void Push(List<KeyValuePair<Tweak, bool>> batch)
        {
            if (batch.Count == 0) return;
            try
            {
                var lines = File.Exists(File_) ? File.ReadAllLines(File_).ToList() : new List<string>();
                lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\t" + string.Join(",", batch.Select(kv => kv.Key.Id + ":" + (kv.Value ? 1 : 0)).ToArray()));
                if (lines.Count > 30) lines = lines.Skip(lines.Count - 30).ToList();
                File.WriteAllLines(File_, lines);
            }
            catch (Exception ex) { Log.W("History: {0}", ex.Message); }
            Raise();
        }
        public static bool HasLast { get { try { return File.Exists(File_) && File.ReadAllLines(File_).Any(l => l.Contains("\t")); } catch { return false; } } }
        public static List<KeyValuePair<Tweak, bool>> Last()
        {
            try
            {
                var line = File.ReadAllLines(File_).LastOrDefault(l => l.Contains("\t"));
                if (line == null) return null;
                var res = new List<KeyValuePair<Tweak, bool>>();
                foreach (var part in line.Split('\t')[1].Split(','))
                {
                    var p = part.Split(':');
                    var t = Catalog.All.FirstOrDefault(x => x.Id == p[0]);
                    if (t != null && p.Length == 2) res.Add(new KeyValuePair<Tweak, bool>(t, p[1] == "1"));
                }
                return res;
            }
            catch { return null; }
        }
        // 3.13: every saved session, newest first (Line = index in the file, for RemoveLine)
        public static List<HistEntry> Entries()
        {
            var o = new List<HistEntry>();
            try
            {
                if (!File.Exists(File_)) return o;
                var lines = File.ReadAllLines(File_);
                var byId = new Dictionary<string, Tweak>(StringComparer.Ordinal);   // 3.32: one lookup per item, not a scan of the catalog
                foreach (var x in Catalog.All) if (!byId.ContainsKey(x.Id)) byId[x.Id] = x;
                int lastIdx = Array.FindLastIndex(lines, l => l.Contains("\t"));
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    var parts = lines[i].Split('\t'); if (parts.Length < 2) continue;
                    DateTime when; if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out when)) DateTime.TryParse(parts[0], out when);
                    var items = new List<KeyValuePair<Tweak, bool>>();
                    foreach (var part in parts[1].Split(','))
                    {
                        var p = part.Split(':');
                        Tweak t = null; if (p.Length == 2) byId.TryGetValue(p[0], out t);
                        if (t != null) items.Add(new KeyValuePair<Tweak, bool>(t, p[1] == "1"));
                    }
                    string lat = parts.Length > 2 && parts[2].StartsWith("lat=") ? parts[2].Substring(4) : null;
                    if (items.Count > 0) o.Add(new HistEntry { Line = i, When = when, Items = items, IsLast = i == lastIdx, LatencyUs = lat });
                }
            }
            catch (Exception ex) { Log.W("History: {0}", ex.Message); }
            return o;
        }
        // 3.14: worst DPC/ISR (µs) measured right after the newest apply, kept as a third field on its line
        public static void NoteLast(string latencyUs)
        {
            try
            {
                var l = File.ReadAllLines(File_).ToList();
                int i = l.FindLastIndex(x => x.Contains("\t")); if (i < 0) return;
                var p = l[i].Split('\t');
                l[i] = p[0] + "\t" + p[1] + "\tlat=" + latencyUs;
                File.WriteAllLines(File_, l);
            }
            catch { }
            Raise();
        }
        public static void RemoveLine(int i)
        {
            try { var l = File.ReadAllLines(File_).ToList(); if (i >= 0 && i < l.Count) { l.RemoveAt(i); File.WriteAllLines(File_, l); } } catch { }
            Raise();
        }
        public static void Pop()
        {
            try { var l = File.ReadAllLines(File_).ToList(); if (l.Count > 0) { l.RemoveAt(l.Count - 1); File.WriteAllLines(File_, l); } } catch { }
            Raise();
        }
    }

    // ================= REVIEW DIALOG =================
    public class ReviewDialog : Form
    {
        public static bool Confirm(IWin32Window owner, List<KeyValuePair<Tweak, bool>> work, bool restorePoint)
        {
            using (var d = new ReviewDialog(work, restorePoint)) return d.ShowDialog(owner) == DialogResult.OK;
        }

        ReviewDialog(List<KeyValuePair<Tweak, bool>> work, bool restorePoint)
        {
            Text = "Review changes"; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F;
            Size = new Size(720, 560); MinimumSize = new Size(560, 380); StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false; KeyPreview = true;
            Chrome.Hook(this);

            int apply = work.Count(x => x.Value), revert = work.Count - apply;
            int reboot = work.Count(x => x.Key.Reboot), relog = work.Count(x => !x.Key.Reboot && x.Key.Relogon);
            int adv = work.Count(x => x.Value && x.Key.Risk == Risk.Advanced), mod = work.Count(x => x.Value && x.Key.Risk == Risk.Moderate);
            var danger = work.Where(x => x.Value && x.Key.Dangerous).Select(x => x.Key).ToList();   // 3.4

            var head = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(22, 16, 22, 4), BackColor = Theme.Bg };
            var h1 = new Label { Text = work.Count == 1 ? "1 change is ready" : work.Count + " changes are ready", Font = Theme.FH, ForeColor = Theme.Strong, AutoSize = true, Location = new Point(22, 12) };
            var parts = new List<string>();
            if (apply > 0) parts.Add(apply + " to apply");
            if (revert > 0) parts.Add(revert + " to revert");
            if (reboot > 0) parts.Add(reboot + " need a restart");
            else if (relog > 0) parts.Add(relog + " need a sign-out");
            if (danger.Count > 0) parts.Add(danger.Count + " from the Danger zone");
            else if (adv > 0) parts.Add(adv + " advanced");
            var h2 = new Label { Text = string.Join(", ", parts.ToArray()) + ".", ForeColor = Theme.Muted, AutoSize = true, Location = new Point(24, 48) };
            // 3.2: every registry value, service, task and command, as "applied value → what undo restores"
            var exact = new TickBox { Text = "Show exact values", ForeColor = Theme.Text, AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, AccessibleName = "Show exact values for every change" };
            head.Controls.Add(h1); head.Controls.Add(h2); head.Controls.Add(exact);
            head.Resize += delegate { exact.Location = new Point(head.ClientSize.Width - exact.Width - 22, 50); };

            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(22, 4, 10, 4) };
            Theme.VerticalOnly(list);   // 3.4: no momentary horizontal bar while the vertical one appears
            CheckBox ack = null;
            if (danger.Count > 0) list.Controls.Add(DangerBox(danger));
            foreach (var kv in work.OrderByDescending(x => x.Value).ThenByDescending(x => (int)x.Key.Risk).ThenBy(x => Catalog.All.IndexOf(x.Key)))
                list.Controls.Add(Item(kv.Key, kv.Value));
            list.Resize += delegate { foreach (Control c in list.Controls) c.Width = list.ClientSize.Width - 34; };
            exact.CheckedChanged += delegate
            {
                list.SuspendLayout();
                foreach (Control c in list.Controls) { var ex = c.Tag as Action<bool>; if (ex != null) ex(exact.Checked); }
                list.ResumeLayout();
            };

            var foot = new Panel { Dock = DockStyle.Bottom, Height = 92, BackColor = Theme.Side, Padding = new Padding(22, 10, 22, 12) };
            var note = new Label
            {
                Dock = DockStyle.Top, Height = 34, ForeColor = adv > 0 ? Theme.Warn : Theme.Muted, Font = Theme.FS,
                // 3.4: each sentence translated on its own (the joined text was half-translated), so L.Apply skips it
                Tag = "notr",
                Text = (danger.Count > 0 ? (restorePoint ? L.T("ArchTweaker makes a restore point first. ") : L.T("A restore point was already made this session. "))
                        : (adv + mod > 0 ? L.T("Advanced and moderate tweaks trade something away (security, power or features). Each one says what in its details. ") : "")
                          + (restorePoint ? L.T("ArchTweaker makes a restore point first. ") : ""))
                     + L.T("ArchTweaker saves every original value, so 'Undo last apply' or 'Undo everything' can put it back.")
            };
            var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Theme.Side };
            var ok = Theme.Primary(work.Count == 1 ? "Apply 1 change" : "Apply " + work.Count + " changes", delegate { DialogResult = DialogResult.OK; Close(); });
            if (danger.Count > 0)
            {   // 3.4: nothing from the Danger zone is applied without an explicit tick
                ack = new TickBox { Text = danger.Count == 1 ? "I understand what this one breaks" : "I understand what these " + danger.Count + " break", ForeColor = Theme.Strong, AutoSize = true, Margin = new Padding(0, 6, 18, 0), AccessibleName = "Confirm the Danger zone changes" };
                ok.Enabled = false; ack.CheckedChanged += delegate { ok.Enabled = ack.Checked; };
                note.ForeColor = Theme.Bad;
            }
            var back = Theme.Btn("Back to editing", delegate { DialogResult = DialogResult.Cancel; Close(); });
            btns.Controls.Add(ok); btns.Controls.Add(back);
            if (ack != null) btns.Controls.Add(ack);
            foot.Controls.Add(btns); foot.Controls.Add(note);
            AcceptButton = ack == null ? ok : null; CancelButton = back;

            Controls.Add(list); Controls.Add(foot); Controls.Add(head);
            Chrome.Polish(this);
            L.Apply(this);   // 3.2: the review was English-only in Russian
            Shown += delegate { foreach (Control c in list.Controls) c.Width = list.ClientSize.Width - 34; if (ack != null) ack.Focus(); else ok.Focus(); };
        }

        // 3.4: the Danger zone items, each with what it breaks, above the normal list
        static Control DangerBox(List<Tweak> danger)
        {
            var p = new Panel { BackColor = Gfx.Mix(Theme.Panel, Theme.Bad, Theme.Dark ? 0.12 : 0.07), Margin = new Padding(0, 0, 0, 10) };
            p.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 3, BackColor = Theme.Bad });
            var head = new Label { Text = danger.Count == 1 ? "1 change is from the Danger zone. Read what it breaks:" : danger.Count + " changes are from the Danger zone. Read what each one breaks:", Font = Theme.FB, ForeColor = Theme.Strong, AutoSize = true, UseMnemonic = false, Location = new Point(16, 10) };
            p.Controls.Add(head);
            var lines = new List<Label>();
            foreach (var t in danger)
            {
                var l = new Label { Text = L.T(t.Title) + "  —  " + L.T(t.Breaks ?? ""), ForeColor = Theme.Text, AutoSize = false, UseMnemonic = false, Tag = "notr" };
                lines.Add(l); p.Controls.Add(l);
            }
            p.Resize += delegate
            {
                int y = head.Bottom + 6, w = Math.Max(120, p.Width - 34);
                foreach (var l in lines)
                {
                    l.Location = new Point(18, y); l.Width = w;
                    l.Height = TextRenderer.MeasureText(l.Text, l.Font, new Size(w, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 2;
                    y = l.Bottom + 4;
                }
                int h = y + 8; if (p.Height != h) p.Height = h;
            };
            return p;
        }

        static Control Item(Tweak t, bool apply)
        {
            var p = new Panel { Height = 40, BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, 4) };
            p.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 3, BackColor = apply ? Theme.On : Theme.AccentOff });
            var detail = new Label { UseMnemonic = false, ForeColor = Theme.Muted, Font = Theme.Mono, AutoSize = false, Location = new Point(80, 38), Visible = false, Tag = "notr" };
            p.Tag = (Action<bool>)(on =>
            {
                if (on && detail.Text.Length == 0)
                {
                    string d; try { d = t.Details(); } catch (Exception ex) { d = "(" + ex.Message + ")"; }
                    detail.Text = (d.Length > 0 ? d : L.T("• Uses an internal action — see description.")).Replace("\r\n\r\n", "\r\n").TrimEnd();
                }
                detail.Visible = on;
                if (on) { detail.Width = Math.Max(120, p.Width - detail.Left - 14); detail.Height = TextRenderer.MeasureText(detail.Text, detail.Font, new Size(detail.Width, 4000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 8; }
                p.Height = on ? 44 + detail.Height : 40;
            });
            p.Controls.Add(detail);
            // 3.4: texts are translated here and measured, not left to L.Apply, so Russian labels never overlap
            string vt = L.T(apply ? "Apply" : "Revert");
            int vw = Math.Max(64, TextRenderer.MeasureText(L.T("Apply"), Theme.FB).Width + 6);
            vw = Math.Max(vw, TextRenderer.MeasureText(L.T("Revert"), Theme.FB).Width + 6);
            int tx = 14 + vw + 4;
            var verb = new Label { UseMnemonic = false, Text = vt, Tag = "notr", ForeColor = apply ? Theme.Strong : Theme.Muted, Font = Theme.FB, AutoSize = false, Size = new Size(vw, 22), Location = new Point(14, 10) };
            var title = new Label { Text = L.T(t.Title), Tag = "notr", ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true, Location = new Point(tx, 11), Height = 20, UseMnemonic = false };
            var cat = new Label { Text = L.T(t.Category), Tag = "notr", ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = false, Height = 18, UseMnemonic = false };
            cat.Width = TextRenderer.MeasureText(cat.Text, cat.Font, Size.Empty, TextFormatFlags.NoPrefix).Width + 4;
            detail.Location = new Point(tx, 38);
            p.Controls.Add(verb); p.Controls.Add(title); p.Controls.Add(cat);
            var chips = new List<Chip>();
            if (apply && t.Dangerous) chips.Add(new Chip(L.T("Danger"), Theme.Risk));
            else if (apply && t.Risk == Risk.Advanced) chips.Add(new Chip(L.T("Advanced"), Theme.Risk));
            else if (apply && t.Risk == Risk.Moderate) chips.Add(new Chip(L.T("Moderate risk"), Theme.Warn));
            if (t.Reboot) chips.Add(new Chip(L.T("Restart"), Theme.Muted));
            foreach (var c in chips) { c.Tag = "notr"; c.AutoSize = false; c.Size = Chip.Measure(c.Text); p.Controls.Add(c); }
            p.Resize += delegate
            {
                int x = p.Width - 10;
                for (int i = chips.Count - 1; i >= 0; i--) { x -= chips[i].Width; chips[i].Location = new Point(x, 9); x -= 6; }
                x -= cat.Width + 4; cat.Location = new Point(x, 13);
                title.Width = Math.Max(60, x - tx - 8);
                if (detail.Visible) detail.Width = Math.Max(120, p.Width - tx - 14);
            };
            return p;
        }
    }

    // ================= HOME =================
    public class Stage
    {
        public string Name, Page, Hint; public int Tuned, Total;
        public double Ratio { get { return Total == 0 ? 1 : (double)Tuned / Total; } }
    }

    // ================= APPLY HISTORY =================
    public class HistEntry { public int Line; public DateTime When; public List<KeyValuePair<Tweak, bool>> Items; public bool IsLast; public string LatencyUs; }

    // The last apply sessions with an Undo for each. Lives in a card on Tools & backup.
    public class HistoryList : FlowLayoutPanel
    {
        // laid out in S() units: the page-wide DPI scale pass must not scale it again
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { }
        protected override bool ScaleChildren { get { return false; } }
        const int ShowMax = 8;
        public HistoryList()
        {
            FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Theme.Panel; Margin = new Padding(0, Theme.S(2), 0, Theme.S(4)); Padding = Padding.Empty;
            AccessibleName = L.T("Apply history");
            Action onChange = () => { if (IsDisposed || !IsHandleCreated) return; try { BeginInvoke((Action)Reload); } catch (InvalidOperationException) { } };
            History.Changed += onChange;
            Disposed += delegate { History.Changed -= onChange; };
            HandleCreated += delegate { Reload(); };
        }
        public void Reload()
        {
            SuspendLayout();
            foreach (Control c in Controls.Cast<Control>().ToList()) { Controls.Remove(c); c.Dispose(); }
            var list = History.Entries().Take(ShowMax).ToList();
            if (list.Count == 0)
                Controls.Add(new Label { Text = L.T("No apply yet. Each time you click Apply, the changes show here."), ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, UseMnemonic = false });
            foreach (var e in list)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, Theme.S(6)), Padding = Padding.Empty };
                int on = e.Items.Count(x => x.Value), off = e.Items.Count - on;
                string what = (on > 0 ? on + " " + L.T("applied") : "") + (on > 0 && off > 0 ? ", " : "") + (off > 0 ? off + " " + L.T("reverted") : "");
                string names = string.Join(", ", e.Items.Take(3).Select(x => L.T(x.Key.Title)).ToArray()) + (e.Items.Count > 3 ? " …" : "");
                var when = new Label { Text = e.When.ToString("dd MMM HH:mm"), Font = Theme.FB, ForeColor = Theme.Strong, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, Theme.S(7), Theme.S(12), 0), Tag = "notr" };
                if (e.LatencyUs != null) what += "  ·  " + e.LatencyUs + " µs " + L.T("after");   // 3.14: scan after apply
                var info = new Label { Text = what + "  ·  " + names, Font = Theme.FS, ForeColor = Theme.Muted, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, Theme.S(8), 0, 0), Tag = "notr", AutoEllipsis = true };
                var entry = e;
                var undo = Theme.Btn(e.IsLast ? "Undo" : "Undo this one", delegate { MainForm.Instance.UndoSession(entry); });
                undo.Margin = new Padding(0, 0, Theme.S(12), 0);
                int uw = new[] { "Undo", "Undo this one" }.Max(x => TextRenderer.MeasureText(L.T(x), undo.Font).Width) + Theme.S(40);
                undo.AutoSize = false; undo.Size = new Size(uw, undo.PreferredSize.Height);   // same width on every row, so the summaries line up
                undo.AccessibleName = L.T("Undo the apply of") + " " + e.When.ToString("dd MMM HH:mm");
                var tip = Tips.New(); tip.SetToolTip(info, string.Join("\n", e.Items.Select(x => (x.Value ? "+ " : "− ") + L.T(x.Key.Title)).ToArray()));
                row.Disposed += delegate { tip.Dispose(); };
                row.Controls.Add(when); row.Controls.Add(undo); row.Controls.Add(info);
                Controls.Add(row);
            }
            ResumeLayout();
        }
        // the summary text takes what is left of the row and ends with "…"; the tooltip lists every change
        protected override void OnLayout(LayoutEventArgs e)
        {
            int w = MaximumSize.Width;
            if (w > 0)
                foreach (Control row in Controls)
                {
                    if (row.Controls.Count < 3) continue;
                    var info = row.Controls[2] as Label; if (info == null) continue;
                    int used = row.Controls[0].Width + row.Controls[0].Margin.Horizontal + row.Controls[1].Width + row.Controls[1].Margin.Horizontal;
                    int pw = info.PreferredWidth;
                    info.AutoSize = false; info.Height = info.PreferredHeight;
                    info.Width = Math.Max(Theme.S(60), Math.Min(pw, w - used - info.Margin.Horizontal - Theme.S(4)));
                }
            base.OnLayout(e);
        }
    }
}
