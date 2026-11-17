// ArchTweaker: What's new dialog and the first-run tour.
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
    // ================= WHAT'S NEW =================
    // Shown once after an update, and from Settings. Text: src/Generated/Changelog.cs, generated from README.md.
    public static class WhatsNew
    {
        const string Key = @"Software\ArchTweaker";
        public static bool Due { get { object o = Reg.Get(Hive.HKCU, Key, "SeenVersion"); return o != null && o.ToString() != Program.Version; } }
        public static void MarkSeen() { try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(Key)) k.SetValue("SeenVersion", Program.Version); } catch { } }
        // 3.19: the whole version history (Changelog.All), with a version list on the left that jumps to a version
        public static void Show(IWin32Window owner)
        {
            MarkSeen();
            using (var f = new Form { Text = L.T("What's new · version history"), Size = new Size(Theme.S(1040), Theme.S(700)), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Bg, ForeColor = Theme.Text,
                FormBorderStyle = FormBorderStyle.Sizable, MinimizeBox = false, ShowInTaskbar = false, KeyPreview = true, AutoScaleMode = AutoScaleMode.None })
            {
                Chrome.Hook(f);
                var box = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.F, DetectUrls = false };
                var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(20), Theme.S(16), Theme.S(12), Theme.S(8)), BackColor = Theme.Panel };
                pad.Controls.Add(box);
                var list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Side, ForeColor = Theme.Text, Font = Theme.F, IntegralHeight = false, AccessibleName = "Versions" };
                var listHead = new Label { Dock = DockStyle.Top, Height = Theme.S(30), ForeColor = Theme.Muted, Font = Theme.FS, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Theme.S(8), 0, 0, 0), BackColor = Theme.Side, Tag = "notr" };
                var side = new Panel { Dock = DockStyle.Left, Width = Theme.S(310), BackColor = Theme.Side, Padding = new Padding(Theme.S(4), 0, 0, 0) };
                side.Controls.Add(list); side.Controls.Add(listHead);
                // 3.28: one category across every version (New features, Tweaks, Design, Fixes, Security, Under the hood)
                var chips = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Theme.Panel, Padding = new Padding(0, 0, 0, Theme.S(10)), AccessibleName = L.T("Show changes") };
                pad.Controls.Add(chips);
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = Theme.S(52), Padding = new Padding(Theme.S(12)), BackColor = Theme.Bg };
                var ok = Theme.Primary("Close", delegate { f.Close(); });
                bar.Controls.Add(ok); f.AcceptButton = ok; f.CancelButton = ok;
                f.Controls.Add(pad); f.Controls.Add(side); f.Controls.Add(bar);

                var heads = new List<KeyValuePair<string, int>>();
                list.SelectedIndexChanged += delegate
                {
                    int i = list.SelectedIndex; if (i < 0 || i >= heads.Count) return;
                    // to the end first, then back: the heading lands at the top of the view, not the bottom
                    box.SelectionStart = box.TextLength; box.ScrollToCaret();
                    box.SelectionStart = heads[i].Value; box.SelectionLength = 0; box.ScrollToCaret();
                };
                var chipBtns = new List<Button>();
                Action<int> show = sel =>
                {
                    string cat = sel == 0 ? null : Cats[sel - 1];
                    for (int i = 0; i < chipBtns.Count; i++)
                    {
                        var b = chipBtns[i]; bool on = i == sel;
                        b.Font = on ? Theme.FB : Theme.F; b.ForeColor = on ? Theme.Strong : Theme.Text;
                        b.FlatAppearance.BorderColor = on ? Theme.Accent : Theme.Border; b.FlatAppearance.BorderSize = on ? Math.Max(2, Theme.S(2)) : 1;
                        b.AccessibleDescription = on ? L.T("selected") : "";
                    }
                    heads = Render(box, Only(Changelog.All, cat));
                    list.BeginUpdate(); list.Items.Clear(); foreach (var h in heads) list.Items.Add(h.Key); list.EndUpdate();
                    listHead.Text = L.F("{0} versions", heads.Count);
                    box.SelectionStart = 0; box.ScrollToCaret();
                };
                f.Shown += delegate
                {
                    var names = new[] { "All changes" }.Concat(Cats).ToArray();
                    for (int i = 0; i < names.Length; i++)
                    {
                        int k = i;
                        int n = Count(Changelog.All, i == 0 ? null : Cats[i - 1]);
                        var b = Theme.Btn(L.T(names[i]) + "  " + n, delegate { show(k); });
                        b.Margin = new Padding(0, 0, Theme.S(6), Theme.S(6)); b.Tag = "notr";
                        chipBtns.Add(b); chips.Controls.Add(b);
                    }
                    show(0);
                };
                f.ShowDialog(owner);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        // 3.28: the headings each version groups its changes under (docs/CHANGELOG.md), in display order
        public static readonly string[] Cats = { "New features", "Tweaks", "Design", "Fixes", "Security", "Under the hood" };

        // Only one category: each version that has it, with its title and that section. cat null = everything. Pure (unit-tested).
        public static string Only(string md, string cat)
        {
            if (cat == null) return md ?? "";
            var o = new System.Text.StringBuilder(); string title = null; bool inCat = false, wrote = false;
            foreach (var raw in (md ?? "").Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith("## ")) { title = raw; inCat = false; wrote = false; continue; }
                if (raw.StartsWith("### ")) { inCat = raw.Substring(4).Trim() == cat; continue; }
                if (!inCat || title == null) continue;
                if (!wrote) { if (o.Length > 0) o.Append('\n'); o.Append(title).Append('\n'); wrote = true; }
                o.Append(raw).Append('\n');
            }
            return o.ToString();
        }
        // Number of top-level changes ("- " lines) in one category, or in all of them. Pure (unit-tested).
        public static int Count(string md, string cat)
        {
            return Only(md, cat).Replace("\r", "").Split('\n').Count(l => l.StartsWith("- "));
        }

        // "3.19.0: title" for each version heading, newest first. Pure (unit-tested).
        public static List<string> Versions(string md)
        {
            return (md ?? "").Replace("\r", "").Split('\n').Where(l => l.StartsWith("## ")).Select(l => l.Substring(3).Trim()).ToList();
        }

        // a small Markdown subset: ## headings, - bullets, **bold** lead-ins, `code`. Returns each heading and its text offset.
        static List<KeyValuePair<string, int>> Render(RichTextBox box, string md)
        {
            var heads = new List<KeyValuePair<string, int>>();
            bool live = box.IsHandleCreated;
            if (live) SendMessage(box.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);   // WM_SETREDRAW off while 60+ versions are written
            try { RenderCore(box, md, heads); }
            finally { if (live) { SendMessage(box.Handle, 0x000B, new IntPtr(1), IntPtr.Zero); box.Invalidate(); } }
            return heads;
        }
        static void RenderCore(RichTextBox box, string md, List<KeyValuePair<string, int>> heads)
        {
            box.Clear();
            foreach (var raw in (md ?? "").Replace("\r", "").Split('\n'))
            {
                string l = raw.TrimEnd();
                if (l.StartsWith("## "))
                {
                    if (box.TextLength > 0) Add(box, "\n", Theme.F, Theme.Text);
                    heads.Add(new KeyValuePair<string, int>(l.Substring(3), box.TextLength));
                    Add(box, l.Substring(3) + "\n", Theme.Semi(12f), Theme.Strong); continue;
                }
                if (l.StartsWith("### ")) { Add(box, "\n▍ ", Theme.FB, Theme.Accent); Add(box, L.T(l.Substring(4).Trim()) + "\n", Theme.FB, Theme.Strong); continue; }
                if (l.StartsWith("- ")) { Add(box, "•  ", Theme.F, Theme.Accent); l = l.Substring(2); }
                else if (l.StartsWith("  - ")) { Add(box, "      –  ", Theme.F, Theme.Muted); l = l.Substring(4); }
                var parts = l.Split(new[] { "**" }, StringSplitOptions.None);
                for (int i = 0; i < parts.Length; i++) Add(box, parts[i].Replace("`", ""), i % 2 == 1 ? Theme.FB : Theme.F, i % 2 == 1 ? Theme.Strong : Theme.Text);
                Add(box, "\n", Theme.F, Theme.Text);
            }
            box.SelectionStart = 0; box.ScrollToCaret();
        }
        static void Add(RichTextBox b, string s, Font f, Color c) { b.SelectionStart = b.TextLength; b.SelectionLength = 0; b.SelectionFont = f; b.SelectionColor = c; b.AppendText(s); }
    }

    // ================= FIRST-RUN TOUR =================
    // Five short cards about where things are. Shown once (after the setup wizard), and from Settings.
    public static class Tour
    {
        const string Key = @"Software\ArchTweaker";
        public static bool Done
        {
            get { object o = Reg.Get(Hive.HKCU, Key, "TourDone"); return o is int && (int)o == 1; }
            set { try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(Key)) k.SetValue("TourDone", value ? 1 : 0, RegistryValueKind.DWord); } catch { } }
        }
        public static readonly string[][] Steps =
        {
            new[] { "Home", "", "Home shows the health score, what waits for Apply, your newest latency scan and your last apply. Start with Select the picks for this PC." },
            new[] { "Tweaks", "", "Each page lists tweaks for one area. Switch a tweak, then click Apply at the bottom. The tags show the risk, a restart and the evidence. Click a row to see exactly what it changes." },
            new[] { "Diagnostics, measuring and tuning", "", "Diagnostics finds problems on this PC. Measure & test shows if a change helped: ArchTweaker saves each run, so the charts show before and after. Tune tries settings and keeps only what helps. History lists every value an apply wrote." },
            new[] { "Clean & apps", "", "Cleanup, Deep clean, Uninstaller, Startup and Services. Every list shows what it will remove before it removes it." },
            new[] { "Undo and safety", "", "Undo last apply is at the bottom. Tools & backup has the apply history, restore points and the recovery kit. Undo everything puts back every original value." },
        };
        public static void Show(IWin32Window owner)
        {
            Done = true;
            int i = 0;
            using (var f = new Form { Text = L.T("Quick tour"), ClientSize = new Size(Theme.S(560), Theme.S(300)), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Panel,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, KeyPreview = true, AutoScaleMode = AutoScaleMode.None })
            {
                Chrome.Hook(f);
                var card = new TourCard { Dock = DockStyle.Fill };
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = Theme.S(56), Padding = new Padding(Theme.S(16), Theme.S(10), Theme.S(16), Theme.S(10)), BackColor = Theme.Panel };
                Button next = null, back = null;
                Action show = () =>
                {
                    card.Set(Steps[i][1], L.T(Steps[i][0]), L.T(Steps[i][2]), i, Steps.Length);
                    next.Text = L.T(i == Steps.Length - 1 ? "Done" : "Next"); back.Enabled = i > 0;
                };
                next = Theme.Primary("Next", delegate { if (i == Steps.Length - 1) f.Close(); else { i++; show(); } });
                back = Theme.Btn("Back", delegate { if (i > 0) { i--; show(); } });
                var skip = Theme.Btn("Skip", delegate { f.Close(); });
                bar.Controls.Add(next); bar.Controls.Add(back); bar.Controls.Add(skip);
                f.Controls.Add(card); f.Controls.Add(bar);
                f.AcceptButton = next; f.CancelButton = skip;
                f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Right) { next.PerformClick(); e.Handled = true; } else if (e.KeyCode == Keys.Left && i > 0) { back.PerformClick(); e.Handled = true; } };
                show();
                f.ShowDialog(owner);
            }
        }
    }

    public class TourCard : Control
    {
        string glyph = "", title = "", body = ""; int step, count;
        public TourCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel; AccessibleRole = AccessibleRole.StaticText;
        }
        public void Set(string g, string t, string b, int i, int n)
        {
            glyph = g; title = t; body = b; step = i; count = n;
            AccessibleName = t + ". " + b + " " + L.F("Step {0} of {1}", i + 1, n);
            Invalidate(); AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            int pad = Theme.S(28), x = pad, y = pad;
            var icf = NavItem.IconFont;
            var c = new Rectangle(x, y, Theme.S(44), Theme.S(44));
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Gfx.Mix(BackColor, Theme.Accent, 0.16))) g.FillEllipse(b, c);
            if (icf != null) using (var f = new Font(icf.FontFamily, Theme.S(20f), GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, glyph, f, c, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            int tx = x + c.Width + Theme.S(16), tw = Width - tx - pad;
            TextRenderer.DrawText(g, title, Theme.Semi(14f), new Rectangle(tx, y + Theme.S(8), tw, Theme.S(30)), Theme.Strong, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, body, Theme.F, new Rectangle(tx, y + Theme.S(46), tw, Height - y - Theme.S(80)), Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int d = Theme.S(8), gap = Theme.S(8), sx = tx, sy = Height - Theme.S(28);
            for (int i = 0; i < count; i++)
                using (var b = new SolidBrush(i == step ? Theme.Accent : Theme.Border)) g.FillEllipse(b, sx + i * (d + gap), sy, d, d);
            g.SmoothingMode = old;
        }
    }
}
