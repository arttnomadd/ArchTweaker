// ArchTweaker 3.10.4: the View button's Appearance flyout (mouse) and the tidied View menu (keyboard, right-click).
// Both change the same settings through MainForm's setters. Colours come from Theme.Preview, so swatches and the
// preview show exactly what a choice will look like.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ---- small images for menu items and the flyout
    public static class Swatches
    {
        static readonly Color[] RingColours = { Color.FromArgb(0xE1, 0x1D, 0x2E), Color.FromArgb(0xF5, 0x9E, 0x0B), Color.FromArgb(0x22, 0xC5, 0x5E), Color.FromArgb(0x14, 0xB8, 0xA6), Color.FromArgb(0x3B, 0x82, 0xF6), Color.FromArgb(0xA8, 0x55, 0xF7) };

        static Bitmap New(int w, int h, out Graphics g)
        {
            var b = new Bitmap(Math.Max(1, w), Math.Max(1, h));
            g = Graphics.FromImage(b); g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            return b;
        }
        // a scheme: its background with a panel and the accent stripe
        public static void DrawScheme(Graphics g, RectangleF r, Theme.Look l)
        {
            using (var p = Gfx.Round(r, r.Height * 0.18f)) using (var b = new SolidBrush(l.Bg)) g.FillPath(b, p);
            var panel = new RectangleF(r.X + r.Width * 0.38f, r.Y + r.Height * 0.2f, r.Width * 0.5f, r.Height * 0.6f);
            using (var p = Gfx.Round(panel, r.Height * 0.1f)) using (var b = new SolidBrush(l.Panel)) g.FillPath(b, p);
            using (var b = new SolidBrush(l.Side)) g.FillRectangle(b, r.X + r.Width * 0.08f, r.Y + r.Height * 0.2f, r.Width * 0.2f, r.Height * 0.6f);
            using (var b = new SolidBrush(l.Accent)) g.FillRectangle(b, panel.X + panel.Width * 0.12f, panel.Bottom - panel.Height * 0.34f, panel.Width * 0.5f, panel.Height * 0.18f);
            using (var b = new SolidBrush(l.Text)) g.FillRectangle(b, panel.X + panel.Width * 0.12f, panel.Y + panel.Height * 0.22f, panel.Width * 0.7f, Math.Max(1f, panel.Height * 0.12f));
            using (var pen = new Pen(l.Border, 1f)) using (var p = Gfx.Round(r, r.Height * 0.18f)) g.DrawPath(pen, p);
        }
        // "pick your own": a ring of colours around the picked colour
        public static void DrawCustom(Graphics g, RectangleF r, Color picked)
        {
            float a = 0, step = 360f / RingColours.Length, w = Math.Max(2f, r.Width * 0.16f);
            var ring = RectangleF.Inflate(r, -w / 2, -w / 2);
            foreach (var c in RingColours) { using (var pen = new Pen(c, w)) g.DrawArc(pen, ring, a - 90, step + 1); a += step; }
            var dot = RectangleF.Inflate(r, -r.Width * 0.3f, -r.Height * 0.3f);
            using (var b = new SolidBrush(picked)) g.FillEllipse(b, dot);
        }
        public static Bitmap Scheme(int i, int w, int h)
        {
            Graphics g; var b = New(w, h, out g);
            using (g)
            {
                if (i == Theme.CustomScheme) DrawCustom(g, new RectangleF((w - h) / 2f, 0.5f, h - 1, h - 1), Theme.SchemeCustom);
                else DrawScheme(g, new RectangleF(0.5f, 0.5f, w - 1.5f, h - 1.5f), Theme.Preview(i, Theme.AccentIndex, Theme.ProgressIndex));
            }
            return b;
        }
        public static Bitmap Dot(Color c, int s)
        {
            Graphics g; var b = New(s, s, out g);
            using (g) using (var br = new SolidBrush(c)) g.FillEllipse(br, 1, 1, s - 3, s - 3);
            return b;
        }
        public static Bitmap Bar(Color c, int s)
        {
            Graphics g; var b = New(s, s, out g);
            using (g) using (var p = Gfx.Round(new RectangleF(0.5f, s * 0.36f, s - 1.5f, s * 0.3f), s * 0.14f)) using (var br = new SolidBrush(c)) g.FillPath(br, p);
            return b;
        }
        // the current choice: a tick badge in the swatch's corner (the image keeps its size, so the menu's image column fits on every runtime)
        public static Bitmap WithTick(Image swatch, bool tick)
        {
            Graphics g; var b = New(swatch.Width, swatch.Height, out g);
            using (g)
            {
                g.DrawImage(swatch, 0, 0, swatch.Width, swatch.Height);
                if (tick)
                {
                    float d = swatch.Height * 0.62f; var c = new RectangleF(swatch.Width - d, swatch.Height - d, d - 0.5f, d - 0.5f);
                    using (var br = new SolidBrush(Theme.Accent)) g.FillEllipse(br, c);
                    using (var pen = new Pen(Theme.Panel, 1f)) g.DrawEllipse(pen, c);
                    using (var pen = new Pen(Theme.OnAccent, Math.Max(1.2f, d * 0.14f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(pen, new[] { new PointF(c.X + d * 0.26f, c.Y + d * 0.52f), new PointF(c.X + d * 0.44f, c.Y + d * 0.7f), new PointF(c.X + d * 0.76f, c.Y + d * 0.32f) });
                }
            }
            return b;
        }
        public static Bitmap Custom(Color picked, int s)
        {
            Graphics g; var b = New(s, s, out g);
            using (g) DrawCustom(g, new RectangleF(1, 1, s - 3, s - 3), picked);
            return b;
        }
    }

    // ---- the View menu: one line per setting, each opening a submenu that shows the current value
    public static class ViewMenu
    {
        static ToolStripMenuItem Item(string text, Image img, bool check, Action click, string right = null)
        {
            // a swatch item gets one image: a tick slot, then the swatch. Windows and Mono draw a check next to an image
            // differently (a box around it, or nothing), so the tick is drawn here and the renderer only draws the image.
            if (img != null) img = Swatches.WithTick(img, check);
            var mi = new ToolStripMenuItem(text, img) { Checked = check };
            if (img != null) { mi.Tag = "swatch"; if (check) mi.Font = new Font(Theme.F, FontStyle.Bold); }
            if (right != null) mi.ShortcutKeyDisplayString = L.T(right);
            if (click != null) mi.Click += delegate { click(); };
            return mi;
        }
        static ToolStripMenuItem Sub(string title, string value)
        {
            var mi = new ToolStripMenuItem(L.T(title) + ": " + L.T(value)) { Tag = "notr" };
            Style(mi.DropDown);
            return mi;
        }
        static void Style(ToolStripDropDown d)
        {
            d.Renderer = new CarbonMenuRenderer(); d.BackColor = Theme.Panel; d.ForeColor = Theme.Text; d.Font = Theme.F;
            var dm = d as ToolStripDropDownMenu;
            if (dm != null) { dm.ShowCheckMargin = false; dm.ShowImageMargin = true; }
        }

        public static ContextMenuStrip Build(MainForm f, Action openFlyout)
        {
            int sw = Theme.S(16);
            var m = new ContextMenuStrip(); Style(m);
            bool hc = Theme.HighContrast;
            m.Items.Add(Item("Appearance…", null, false, openFlyout));
            m.Items.Add(new ToolStripSeparator());
            var dl = Item("Switch dark / light", null, false, f.SwitchTheme); dl.ShortcutKeyDisplayString = "Ctrl+Shift+L"; dl.Enabled = !hc;
            m.Items.Add(dl);
            m.Items.Add(Item("Follow Windows theme", null, Theme.FollowWindows, () => f.SetFollowWindows(!Theme.FollowWindows)));
            if (hc) m.Items.Add(new ToolStripLabel("High-contrast theme is on: Windows colours are used") { ForeColor = Theme.Muted, Font = Theme.FS });
            m.Items.Add(new ToolStripSeparator());

            var sc = Sub("Colour scheme", Theme.SchemeNames[Theme.Scheme]); sc.Enabled = !hc;
            for (int i = 0; i < Theme.SchemeNames.Length; i++)
            {
                int si = i; bool custom = si == Theme.CustomScheme;
                sc.DropDownItems.Add(Item(custom ? "Custom…" : Theme.SchemeNames[si], Swatches.Scheme(si, sw, sw), Theme.Scheme == si,
                    () => { if (custom) f.PickCustomScheme(); else f.SetScheme(si); }, si == 0 ? "default" : null));
            }
            m.Items.Add(sc);

            var ac = Sub("Accent colour", Theme.AccentNames[Theme.AccentIndex]); ac.Enabled = !hc;
            for (int i = 0; i < Theme.AccentNames.Length; i++)
            {
                int ai = i; bool custom = ai == Theme.AccentNames.Length - 1;
                ac.DropDownItems.Add(Item(custom ? "Custom…" : Theme.AccentNames[ai], custom ? Swatches.Custom(Theme.CustomColor, sw) : Swatches.Dot(Theme.AccentSwatch(ai), sw), Theme.AccentIndex == ai,
                    () => { if (custom) f.PickCustomAccent(); else f.SetAccent(ai); }, ai == 0 ? "default" : null));
            }
            m.Items.Add(ac);

            var pr = Sub("Progress bars", Theme.ProgressNames[Theme.ProgressIndex]); pr.Enabled = !hc;
            for (int i = 0; i < Theme.ProgressNames.Length; i++)
            {
                int pi = i; bool custom = pi == Theme.ProgressNames.Length - 1;
                pr.DropDownItems.Add(Item(custom ? "Custom…" : Theme.ProgressNames[pi], custom ? Swatches.Custom(Theme.ProgressCustom, sw) : Swatches.Bar(Theme.ProgressSwatch(pi), sw), Theme.ProgressIndex == pi,
                    () => { if (custom) f.PickCustomProgress(); else f.SetProgress(pi); }, pi == 0 ? "default" : null));
            }
            m.Items.Add(pr);

            var ts = Sub("Text size", Theme.TextSizeNames[Theme.TextSize]);
            for (int i = 0; i < Theme.TextSizeNames.Length; i++)
            {
                int ti = i;
                var it = Item(Theme.TextSizeNames[i], null, Theme.TextSize == i, () => f.SetTextSize(ti), i == 0 ? "default" : null); it.Tag = "notr";
                ts.DropDownItems.Add(it);
            }
            m.Items.Add(ts);
            m.Items.Add(new ToolStripSeparator());

            m.Items.Add(Item("Compact rows", null, Theme.Compact, () => f.SetCompact(!Theme.Compact)));
            m.Items.Add(Item("Slim sidebar", null, Theme.Slim, () => f.SetSlim(!Theme.Slim)));
            var so = Sub("Sort tweaks", TweakSort.Names[TweakSort.Mode]);
            for (int i = 0; i < TweakSort.Names.Length; i++)
            {
                int si = i;
                so.DropDownItems.Add(Item(TweakSort.Names[i], null, TweakSort.Mode == i, () => f.SetSort(si), i == 0 ? "default" : null));
            }
            // the sort order changes without a rebuild: keep the submenu's ticks and title current
            so.DropDownOpening += delegate
            {
                for (int i = 0; i < so.DropDownItems.Count; i++) ((ToolStripMenuItem)so.DropDownItems[i]).Checked = TweakSort.Mode == i;
            };
            so.DropDownItemClicked += delegate { m.BeginInvoke((Action)(() => so.Text = L.T("Sort tweaks") + ": " + L.T(TweakSort.Names[TweakSort.Mode]))); };
            m.Items.Add(so);
            L.Apply(m.Items);
            return m;
        }
    }

    // ---- the Appearance flyout: preview, scheme cards, colour rows, text size, layout and sort
    public class AppearanceFlyout : ToolStripDropDown
    {
        // set before a choice that rebuilds the window; the new window opens the flyout again (only right after the choice)
        public static DateTime ReopenUntil;
        public static bool Reopen { get { return DateTime.UtcNow < ReopenUntil; } set { ReopenUntil = value ? DateTime.UtcNow.AddSeconds(5) : DateTime.MinValue; } }
        readonly AppearancePanel panel;
        public AppearanceFlyout(MainForm f)
        {
            panel = new AppearancePanel(f, this);
            var host = new ToolStripControlHost(panel) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = panel.Size };
            Padding = new Padding(1); Margin = Padding.Empty; BackColor = Theme.Border; AutoSize = true; DropShadowEnabled = true;
            Items.Add(host);
        }
        protected override void OnOpened(EventArgs e) { base.OnOpened(e); panel.Focus(); }
    }

    public class AppearancePanel : Control
    {
        class Hit { public Rectangle R; public string Kind; public int Index; public string Name; }
        readonly MainForm form; readonly AppearanceFlyout owner;
        readonly List<Hit> hits = new List<Hit>();
        int hover = -1, focus = -1; bool keys;   // keys: the focus ring shows only after a key press
        readonly int W = Theme.S(392), Pad = Theme.S(16);
        Rectangle preview;
        readonly List<KeyValuePair<string, int>> heads = new List<KeyValuePair<string, int>>();   // section title, y
        int footerY;

        public AppearancePanel(MainForm f, AppearanceFlyout o)
        {
            form = f; owner = o;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true; AccessibleName = "Appearance"; AccessibleRole = AccessibleRole.Pane;
            Font = Theme.F; BackColor = Theme.Panel;
            DoLayout();
        }

        void Add(string kind, int index, string name, Rectangle r) { hits.Add(new Hit { Kind = kind, Index = index, Name = name, R = r }); }

        void DoLayout()
        {
            hits.Clear(); heads.Clear();
            int y = Pad, inner = W - 2 * Pad, gap = Theme.S(8), head = Theme.S(24), sect = Theme.S(14);
            preview = new Rectangle(Pad, y, inner, Theme.S(92)); y = preview.Bottom + sect;

            heads.Add(new KeyValuePair<string, int>("Colour scheme", y)); y += head;
            int cols = 4, cw = (inner - (cols - 1) * gap) / cols, ch = Theme.S(62);
            for (int i = 0; i < Theme.SchemeNames.Length; i++)
                Add("scheme", i, i == Theme.CustomScheme ? "Custom…" : Theme.SchemeNames[i], new Rectangle(Pad + (i % cols) * (cw + gap), y + (i / cols) * (ch + gap), cw, ch));
            y += ((Theme.SchemeNames.Length + cols - 1) / cols) * (ch + gap) - gap + sect;

            heads.Add(new KeyValuePair<string, int>("Accent colour", y)); y += head;
            int ds = Theme.S(28), dg = Theme.S(12);
            for (int i = 0; i < Theme.AccentNames.Length; i++)
                Add("accent", i, i == Theme.AccentNames.Length - 1 ? "Custom…" : Theme.AccentNames[i], new Rectangle(Pad + i * (ds + dg), y, ds, ds));
            y += ds + sect;

            heads.Add(new KeyValuePair<string, int>("Progress bars", y)); y += head;
            int pw = (inner - (Theme.ProgressNames.Length - 1) * Theme.S(6)) / Theme.ProgressNames.Length;
            for (int i = 0; i < Theme.ProgressNames.Length; i++)
                Add("progress", i, i == Theme.ProgressNames.Length - 1 ? "Custom…" : Theme.ProgressNames[i], new Rectangle(Pad + i * (pw + Theme.S(6)), y, pw, ds));
            y += ds + sect;

            heads.Add(new KeyValuePair<string, int>("Text size", y)); y += head;
            int sw = inner / Theme.TextSizeNames.Length;
            for (int i = 0; i < Theme.TextSizeNames.Length; i++)
                Add("size", i, Theme.TextSizeNames[i], new Rectangle(Pad + i * sw, y, i == Theme.TextSizeNames.Length - 1 ? inner - i * sw : sw, Theme.S(30)));
            y += Theme.S(30) + sect;

            heads.Add(new KeyValuePair<string, int>("Layout", y)); y += head;
            int rh = Theme.S(28);
            Add("compact", 0, "Compact rows", new Rectangle(Pad, y, inner, rh)); y += rh;
            Add("slim", 0, "Slim sidebar", new Rectangle(Pad, y, inner, rh)); y += rh;
            Add("follow", 0, "Follow Windows theme", new Rectangle(Pad, y, inner, rh)); y += rh + sect;

            heads.Add(new KeyValuePair<string, int>("Sort tweaks", y)); y += head;
            int hw = (inner - gap) / 2;
            for (int i = 0; i < TweakSort.Names.Length; i++)
                Add("sort", i, TweakSort.Names[i], new Rectangle(Pad + (i % 2) * (hw + gap), y + (i / 2) * (rh + Theme.S(6)), hw, rh));
            y += ((TweakSort.Names.Length + 1) / 2) * (rh + Theme.S(6)) - Theme.S(6) + sect;
            footerY = y; y += Theme.S(20) + Pad;
            Size = new Size(W, y);
        }

        bool Current(Hit h)
        {
            switch (h.Kind)
            {
                case "scheme": return Theme.Scheme == h.Index;
                case "accent": return Theme.AccentIndex == h.Index;
                case "progress": return Theme.ProgressIndex == h.Index;
                case "size": return Theme.TextSize == h.Index;
                case "compact": return Theme.Compact;
                case "slim": return Theme.Slim;
                case "follow": return Theme.FollowWindows;
                case "sort": return TweakSort.Mode == h.Index;
            }
            return false;
        }

        // the choice under the pointer (or keyboard focus) shows in the preview
        Theme.Look Look()
        {
            int s = Theme.Scheme, a = Theme.AccentIndex, p = Theme.ProgressIndex;
            var h = hover >= 0 ? hits[hover] : keys && focus >= 0 ? hits[focus] : null;
            if (h != null && !Theme.HighContrast)
            {
                if (h.Kind == "scheme") s = h.Index;
                else if (h.Kind == "accent") a = h.Index;
                else if (h.Kind == "progress") p = h.Index;
            }
            return Theme.Preview(s, a, p);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Panel); g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawPreview(g, preview, Look());
            var hh = hover >= 0 ? hits[hover] : keys && focus >= 0 ? hits[focus] : null;
            foreach (var kv in heads)
            {
                string title = L.T(kv.Key);
                TextRenderer.DrawText(g, title, Theme.FB, new Point(Pad, kv.Value), Theme.Strong, TextFormatFlags.NoPrefix);
                // the name of the swatch under the pointer, after its section title
                if (hh != null && Section(hh.Kind) == kv.Key && (hh.Kind == "scheme" || hh.Kind == "accent" || hh.Kind == "progress"))
                {
                    int tw = TextRenderer.MeasureText(title, Theme.FB).Width;
                    TextRenderer.DrawText(g, "·  " + L.T(hh.Name), Theme.F, new Point(Pad + tw + Theme.S(6), kv.Value + Theme.S(1)), Theme.Muted, TextFormatFlags.NoPrefix);
                }
            }
            for (int i = 0; i < hits.Count; i++) DrawHit(g, hits[i], i == hover, keys && i == focus && Focused);
            TextRenderer.DrawText(g, L.T("Switch dark / light: Ctrl+Shift+L"), Theme.FS, new Point(Pad, footerY), Theme.Muted, TextFormatFlags.NoPrefix);
        }

        static string Section(string kind)
        {
            switch (kind)
            {
                case "scheme": return "Colour scheme";
                case "accent": return "Accent colour";
                case "progress": return "Progress bars";
                case "size": return "Text size";
                case "sort": return "Sort tweaks";
                default: return "Layout";
            }
        }

        // a small window: sidebar with the selected page, a card with a title, a button and a progress bar
        static void DrawPreview(Graphics g, Rectangle r, Theme.Look l)
        {
            float rad = Theme.S(Theme.Radius);
            using (var p = Gfx.Round(r, rad)) using (var b = new SolidBrush(l.Bg)) g.FillPath(b, p);
            var side = new RectangleF(r.X, r.Y, r.Width * 0.27f, r.Height);
            using (var p = Gfx.Round(side, rad)) using (var b = new SolidBrush(l.Side)) g.FillPath(b, p);
            float u = Theme.S(1f), ly = r.Y + 14 * u;
            for (int i = 0; i < 4; i++)
            {
                if (i == 1)
                {
                    using (var b = new SolidBrush(l.Accent)) g.FillRectangle(b, side.X + 6 * u, ly - 3 * u, 3 * u, 12 * u);
                    using (var b = new SolidBrush(l.Text)) g.FillRectangle(b, side.X + 14 * u, ly, side.Width * 0.5f, 5 * u);
                }
                else using (var b = new SolidBrush(l.Muted)) g.FillRectangle(b, side.X + 14 * u, ly, side.Width * (0.35f + 0.1f * (i % 2)), 5 * u);
                ly += 18 * u;
            }
            var card = new RectangleF(side.Right + 12 * u, r.Y + 12 * u, r.Right - side.Right - 24 * u, r.Height - 24 * u);
            using (var p = Gfx.Round(card, Theme.S(Theme.Radius))) using (var b = new SolidBrush(l.Panel)) g.FillPath(b, p);
            using (var pen = new Pen(l.Border, 1f)) using (var p = Gfx.Round(card, Theme.S(Theme.Radius))) g.DrawPath(pen, p);
            float cx = card.X + 12 * u, cy = card.Y + 11 * u;
            using (var b = new SolidBrush(l.Text)) g.FillRectangle(b, cx, cy, card.Width * 0.42f, 6 * u);
            using (var b = new SolidBrush(l.Muted)) g.FillRectangle(b, cx, cy + 12 * u, card.Width * 0.6f, 4 * u);
            using (var b = new SolidBrush(l.Ok)) g.FillEllipse(b, card.Right - 22 * u, cy - 1 * u, 8 * u, 8 * u);
            var bar = new RectangleF(cx, card.Bottom - 14 * u, card.Width * 0.5f, 5 * u);
            using (var p = Gfx.Round(bar, 2.5f * u)) using (var b = new SolidBrush(l.AccentOff)) g.FillPath(b, p);
            using (var p = Gfx.Round(new RectangleF(bar.X, bar.Y, bar.Width * 0.6f, bar.Height), 2.5f * u)) using (var b = new SolidBrush(l.Progress)) g.FillPath(b, p);
            var btn = new RectangleF(card.Right - 12 * u - card.Width * 0.3f, card.Bottom - 22 * u, card.Width * 0.3f, 14 * u);
            using (var p = Gfx.Round(btn, 3 * u)) using (var b = new SolidBrush(l.Accent)) g.FillPath(b, p);
            using (var b = new SolidBrush(l.OnAccent)) g.FillRectangle(b, btn.X + btn.Width * 0.2f, btn.Y + btn.Height * 0.4f, btn.Width * 0.6f, 3 * u);
        }

        void DrawHit(Graphics g, Hit h, bool hot, bool focused)
        {
            bool cur = Current(h); var r = h.R; float u = Theme.S(1f);
            switch (h.Kind)
            {
                case "scheme":
                {
                    var cardR = new RectangleF(r.X, r.Y, r.Width, r.Height);
                    using (var p = Gfx.Round(cardR, Theme.S(Theme.Radius))) using (var b = new SolidBrush(hot ? Theme.PanelHover : Theme.Panel)) g.FillPath(b, p);
                    var sw = new RectangleF(r.X + 6 * u, r.Y + 6 * u, r.Width - 12 * u, r.Height - 28 * u);
                    if (h.Index == Theme.CustomScheme)
                    {
                        using (var p = Gfx.Round(sw, sw.Height * 0.18f)) using (var b = new SolidBrush(Theme.SchemeCustom)) g.FillPath(b, p);
                        float d = sw.Height * 0.8f; Swatches.DrawCustom(g, new RectangleF(sw.X + (sw.Width - d) / 2, sw.Y + (sw.Height - d) / 2, d, d), Theme.SchemeCustom);
                    }
                    else Swatches.DrawScheme(g, sw, Theme.Preview(h.Index, Theme.AccentIndex, Theme.ProgressIndex));
                    TextRenderer.DrawText(g, L.T(h.Name), Theme.FS, new Rectangle(r.X + 2, r.Bottom - Theme.S(21), r.Width - 4, Theme.S(18)), cur ? Theme.Strong : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    if (cur) Selected(g, cardR, true);
                    break;
                }
                case "accent":
                {
                    var d = new RectangleF(r.X + 3 * u, r.Y + 3 * u, r.Width - 6 * u, r.Height - 6 * u);
                    bool custom = h.Index == Theme.AccentNames.Length - 1;
                    if (custom) Swatches.DrawCustom(g, d, Theme.CustomColor);
                    else using (var b = new SolidBrush(Theme.AccentSwatch(h.Index))) g.FillEllipse(b, d);
                    if (hot && !cur) using (var pen = new Pen(Theme.Muted, 1.5f * u)) g.DrawEllipse(pen, RectangleF.Inflate(d, 2 * u, 2 * u));
                    if (cur)
                    {
                        using (var pen = new Pen(Theme.Strong, 2f * u)) g.DrawEllipse(pen, RectangleF.Inflate(d, 2.5f * u, 2.5f * u));
                        if (!custom) Tick(g, d, Theme.InkOn(Theme.AccentSwatch(h.Index), 3));
                    }
                    break;
                }
                case "progress":
                {
                    var cell = new RectangleF(r.X, r.Y, r.Width, r.Height);
                    using (var p = Gfx.Round(cell, Theme.S(Theme.Radius))) using (var b = new SolidBrush(hot ? Theme.PanelHover : Theme.Bg)) g.FillPath(b, p);
                    if (h.Index == Theme.ProgressNames.Length - 1)
                    {
                        float d = r.Height - 8 * u; Swatches.DrawCustom(g, new RectangleF(r.X + (r.Width - d) / 2, r.Y + 4 * u, d, d), Theme.ProgressCustom);
                    }
                    else
                    {
                        var bar = new RectangleF(r.X + 8 * u, r.Y + r.Height / 2f - 3 * u, r.Width - 16 * u, 6 * u);
                        using (var p = Gfx.Round(bar, 3 * u)) using (var b = new SolidBrush(Theme.ProgressSwatch(h.Index))) g.FillPath(b, p);
                    }
                    if (cur) Selected(g, cell, false);
                    break;
                }
                case "size":
                {
                    var seg = new RectangleF(r.X, r.Y, r.Width - (h.Index < Theme.TextSizeNames.Length - 1 ? 1 : 0), r.Height);
                    using (var b = new SolidBrush(cur ? Theme.Accent : hot ? Theme.PanelHover : Theme.Bg)) g.FillRectangle(b, seg);
                    using (var pen = new Pen(Theme.Border, 1f)) g.DrawRectangle(pen, seg.X, seg.Y, seg.Width - 1, seg.Height - 1);
                    TextRenderer.DrawText(g, h.Name, Theme.F, Rectangle.Round(seg), cur ? Theme.OnAccent : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    break;
                }
                case "compact": case "slim": case "follow":
                {
                    if (hot) using (var b = new SolidBrush(Theme.PanelHover)) g.FillRectangle(b, r);
                    TextRenderer.DrawText(g, L.T(h.Name), Theme.F, new Rectangle(r.X + Theme.S(4), r.Y, r.Width - Theme.S(60), r.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    var track = new RectangleF(r.Right - Theme.S(44), r.Y + r.Height / 2f - Theme.S(9), Theme.S(36), Theme.S(18));
                    using (var p = Gfx.Round(track, track.Height / 2)) using (var b = new SolidBrush(cur ? Theme.On : Theme.AccentOff)) g.FillPath(b, p);
                    float k = track.Height - 6 * u, kx = cur ? track.Right - k - 3 * u : track.X + 3 * u;
                    using (var b = new SolidBrush(cur ? Theme.OnInk : Theme.Text)) g.FillEllipse(b, kx, track.Y + 3 * u, k, k);
                    break;
                }
                case "sort":
                {
                    var cell = new RectangleF(r.X, r.Y, r.Width, r.Height);
                    using (var p = Gfx.Round(cell, Theme.S(Theme.Radius))) using (var b = new SolidBrush(cur ? Theme.Sel : hot ? Theme.PanelHover : Theme.Bg)) g.FillPath(b, p);
                    TextRenderer.DrawText(g, L.T(h.Name), Theme.FS, new Rectangle(r.X + Theme.S(8), r.Y, r.Width - Theme.S(16), r.Height), cur ? Theme.Strong : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    break;
                }
            }
            if (focused) using (var pen = new Pen(Theme.Text, 1.5f * u) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, r.X - 2, r.Y - 2, r.Width + 3, r.Height + 3);
        }

        // the current choice: accent outline and a tick badge in the corner
        static void Selected(Graphics g, RectangleF r, bool badge)
        {
            float u = Theme.S(1f);
            using (var pen = new Pen(Theme.Accent, 2f * u)) using (var p = Gfx.Round(RectangleF.Inflate(r, -1 * u, -1 * u), Theme.S(Theme.Radius))) g.DrawPath(pen, p);
            if (!badge) return;
            var c = new RectangleF(r.Right - 16 * u, r.Y + 3 * u, 13 * u, 13 * u);
            using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, c);
            Tick(g, c, Theme.OnAccent);
        }
        static void Tick(Graphics g, RectangleF c, Color ink)
        {
            using (var pen = new Pen(ink, Math.Max(1.5f, c.Width * 0.12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(c.X + c.Width * 0.27f, c.Y + c.Height * 0.52f), new PointF(c.X + c.Width * 0.44f, c.Y + c.Height * 0.68f), new PointF(c.X + c.Width * 0.74f, c.Y + c.Height * 0.34f) });
        }

        // ---- input
        int At(Point p) { for (int i = 0; i < hits.Count; i++) if (hits[i].R.Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e) { int h = At(e.Location); if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { if (hover >= 0) { hover = -1; Invalidate(); } base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { int h = At(e.Location); if (h >= 0 && e.Button == MouseButtons.Left) Act(hits[h]); base.OnMouseClick(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || k == Keys.Tab || k == (Keys.Tab | Keys.Shift) || base.IsInputKey(k); }
        protected override void OnGotFocus(EventArgs e) { if (focus < 0) focus = Math.Max(0, hits.FindIndex(Current)); Invalidate(); base.OnGotFocus(e); Announce(); }
        // 3.11: the panel is one control, so its name follows the keyboard focus and screen readers hear each choice
        void Announce()
        {
            if (focus < 0 || focus >= hits.Count) return;
            var h = hits[focus];
            AccessibleName = L.T(h.Name ?? "") + (Current(h) ? ", " + L.T("selected") : "");
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int n = hits.Count; keys = true;
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down || (e.KeyCode == Keys.Tab && !e.Shift)) { focus = (focus + 1) % n; e.Handled = true; }
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up || (e.KeyCode == Keys.Tab && e.Shift)) { focus = (focus - 1 + n) % n; e.Handled = true; }
            else if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && focus >= 0) { Act(hits[focus]); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { owner.Close(); e.Handled = true; }
            if (e.Handled) { Invalidate(); Announce(); }
            base.OnKeyDown(e);
        }

        void Act(Hit h)
        {
            if (Theme.HighContrast && (h.Kind == "scheme" || h.Kind == "accent" || h.Kind == "progress"))
            {
                form.Notify("A Windows high-contrast theme is on: its colours are used. Turn it off in Windows Settings → Accessibility → Contrast themes.", Toast.Kind.Warn);
                return;
            }
            if (h.Kind == "sort") { form.SetSort(h.Index); Invalidate(); return; }   // no rebuild: the flyout stays open
            bool custom = (h.Kind == "scheme" && h.Index == Theme.CustomScheme) || (h.Kind == "accent" && h.Index == Theme.AccentNames.Length - 1) || (h.Kind == "progress" && h.Index == Theme.ProgressNames.Length - 1);
            if (Current(h) && !custom && h.Kind != "compact" && h.Kind != "slim" && h.Kind != "follow") return;
            owner.Close();
            AppearanceFlyout.Reopen = !custom;   // a colour dialog is a moment of its own; plain choices reopen the flyout
            switch (h.Kind)
            {
                case "scheme": if (custom) form.PickCustomScheme(); else form.SetScheme(h.Index); break;
                case "accent": if (custom) form.PickCustomAccent(); else form.SetAccent(h.Index); break;
                case "progress": if (custom) form.PickCustomProgress(); else form.SetProgress(h.Index); break;
                case "size": form.SetTextSize(h.Index); break;
                case "compact": form.SetCompact(!Theme.Compact); break;
                case "slim": form.SetSlim(!Theme.Slim); break;
                case "follow": form.SetFollowWindows(!Theme.FollowWindows); break;
            }
        }
    }
}
