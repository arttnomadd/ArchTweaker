// ArchTweaker: Drawing helpers, motion timing, window chrome (dark title bar) and accessibility helpers.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArchTweaker
{
    public static class Gfx
    {
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            if (rad <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        // One focus indicator for every custom control: 2px solid, Strong ink (≥ 3:1 on every surface, WCAG 2.4.7 / 1.4.11)
        public static void FocusRing(Graphics g, RectangleF r, float radius)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Round(r, radius)) using (var pen = new Pen(Theme.Strong, Math.Max(2f, 2f * Theme.Scale))) g.DrawPath(pen, p);   // 2 px at 96 dpi, scaled
            g.SmoothingMode = old;
        }
        // 3.36: one raised surface for every card, tile and panel: a soft shadow (offset down, blurred by stacking),
        // the fill, a 1 px light top edge in dark schemes, and a hairline border. Depth comes from this, never from colour.
        public static void Surface(Graphics g, RectangleF r, Color fill, bool shadow, Color? border = null, float radius = -1)
        {
            if (radius < 0) radius = Theme.S((float)Theme.Radius);
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (shadow && Theme.ShadowA > 0)
            {
                float[] dy = { 1f, 2f, 4f }; int[] a = { 38, 22, 10 };
                for (int i = 0; i < dy.Length; i++)
                {
                    float o = Theme.S(dy[i]), grow = Theme.S(i * 1f);
                    using (var p = Round(new RectangleF(r.X - grow, r.Y + o - grow * 0.5f, r.Width + grow * 2, r.Height + grow), radius + grow))
                    using (var b = new SolidBrush(Color.FromArgb(a[i] * Theme.ShadowA / 100, 0, 0, 0))) g.FillPath(b, p);
                }
            }
            using (var p = Round(r, radius)) using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            if (Theme.Dark && Theme.ShadowA > 0)
            {   // the light top edge, fading out before the corners
                float y = r.Y + 0.5f, x0 = r.X + radius, x1 = r.Right - radius;
                if (x1 > x0) using (var br = new LinearGradientBrush(new PointF(x0 - 1, y), new PointF(x1 + 1, y), Color.Transparent, Color.Transparent))
                {
                    var cb = new ColorBlend { Colors = new[] { Color.FromArgb(0, Theme.Hi), Theme.Hi, Theme.Hi, Color.FromArgb(0, Theme.Hi) }, Positions = new[] { 0f, 0.12f, 0.88f, 1f } };
                    br.InterpolationColors = cb;
                    using (var pen = new Pen(br, 1f)) g.DrawLine(pen, x0, y, x1, y);
                }
            }
            using (var p = Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), radius)) using (var pen = new Pen(border ?? Theme.Line)) g.DrawPath(pen, p);
            g.SmoothingMode = old;
        }
        // 3.36: a horizontal or vertical hairline (dividers inside a surface)
        public static void Hair(Graphics g, float x0, float y0, float x1, float y1) { using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, x0, y0, x1, y1); }
        // 3.36: a segmented meter (n cells, the first `filled` lit), read like an instrument scale
        public static void Segments(Graphics g, RectangleF r, int n, double share, Color on, Color off)
        {
            float gap = Math.Max(2f, Theme.S(3f)), w = (r.Width - gap * (n - 1)) / n;
            int lit = (int)Math.Round(Math.Max(0, Math.Min(1, share)) * n);
            for (int i = 0; i < n; i++)
                using (var b = new SolidBrush(i < lit ? on : off)) g.FillRectangle(b, r.X + i * (w + gap), r.Y, w, r.Height);
        }
        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    // On/off switch. On = applied. Space or click toggles; focus ring follows keyboard focus.
    // 3.3: short state transitions (switch knobs). Off when Windows says "Show animations" is off.
    public static class Motion
    {
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(int action, int param, ref bool value, int ini);
        public static bool On
        {
            get
            {
                try
                {
                    if (!SystemInformation.UIEffectsEnabled) return false;
                    bool anim = true;
                    if (Environment.OSVersion.Platform == PlatformID.Win32NT) SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, ref anim, 0);
                    return anim;
                }
                catch { return true; }
            }
        }
        static readonly Dictionary<Control, Func<double, bool>> items = new Dictionary<Control, Func<double, bool>>();
        static Timer timer; static DateTime last;
        // step(ms since last frame) returns true when finished. One running transition per control.
        public static void Run(Control c, Func<double, bool> step)
        {
            items[c] = step;
            if (timer == null) { timer = new Timer { Interval = 15 }; timer.Tick += Tick; }
            if (!timer.Enabled) { last = DateTime.UtcNow; timer.Start(); }
        }
        public static bool Running(Control c) { return items.ContainsKey(c); }
        static void Tick(object s, EventArgs e)
        {
            var now = DateTime.UtcNow; double dt = Math.Min(50, (now - last).TotalMilliseconds); last = now;
            foreach (var kv in items.ToList())
            {
                bool done; try { done = kv.Key.IsDisposed || kv.Value(dt); } catch { done = true; }
                if (done) items.Remove(kv.Key);
            }
            if (items.Count == 0) timer.Stop();
        }
        // knob position 0 (off) … 1 (on), 140 ms with ease-out, repainting only the switch
        public static void Slide(Control c, Func<float> get, Action<float> set, bool to, Rectangle area)
        {
            float target = to ? 1f : 0f;
            if (!On || !c.IsHandleCreated || !c.Visible) { items.Remove(c); set(target); c.Invalidate(area); return; }
            float from = get(); double t = 0, len = Theme.Ms.Small * Math.Abs(target - from);
            if (len < 1) { set(target); c.Invalidate(area); return; }
            Run(c, dt =>
            {
                t += dt; double k = Math.Min(1, t / len); k = 1 - (1 - k) * (1 - k) * (1 - k);   // ease-out cubic
                set((float)(from + (target - from) * k)); c.Invalidate(area);
                return t >= len;
            });
        }
    }

    // Window chrome and per-control polish.
    public static class Chrome
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        // Undocumented uxtheme ordinals (Win10 1903+). Without them DarkMode_Explorer still draws light scrollbars.
        [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#133")] static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);
        [DllImport("uxtheme.dll", EntryPoint = "#104")] static extern void RefreshImmersiveColorPolicyState();
        [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();
        static bool darkApi = Environment.OSVersion.Platform == PlatformID.Win32NT && OSInfo.Build >= 18362;

        // 3.1: tell uxtheme the app follows our theme (2 = force dark, 3 = force light), so native scrollbars match.
        public static void AppMode()
        {
            if (!darkApi) return;
            try { SetPreferredAppMode(Theme.Dark ? 2 : 3); RefreshImmersiveColorPolicyState(); FlushMenuThemes(); }
            catch { darkApi = false; }   // entry point missing on this build
        }

        public static void AllowDark(IntPtr h)
        {
            if (!darkApi) return;
            try { AllowDarkModeForWindow(h, Theme.Dark); } catch { darkApi = false; }
        }

        static int Ref(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        // Dark title bar (Win10 1809+), caption + border colours (Win11 22000+). Silently ignored where unsupported.
        public static void Window(Form f) { Window(f, Form.ActiveForm == f); }
        // 3.11: the border shows the accent only while the window is active, as Windows does; small rounded corners.
        // High contrast keeps the system colours: the app must not override them (Microsoft contrast-theme guidance).
        public static void Window(Form f, bool active)
        {
            try
            {
                if (!f.IsHandleCreated) return;
                int dark = Theme.Dark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref dark, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref dark, 4);
                if (OSInfo.IsWin11)
                {
                    int corner = 3;   // DWMWCP_ROUNDSMALL: closest to Carbon's square corners that Windows 11 still rounds
                    DwmSetWindowAttribute(f.Handle, 33, ref corner, 4);
                    const int Default = unchecked((int)0xFFFFFFFF);
                    bool hc = Theme.HighContrast;
                    int border = hc ? Default : Ref(active ? Theme.Accent : Theme.Border);
                    int cap = hc ? Default : Ref(Theme.Bg), txt = hc ? Default : Ref(active ? Theme.Text : Theme.Muted);   // caption = top-bar colour, so the title bar blends in
                    DwmSetWindowAttribute(f.Handle, 34, ref border, 4);
                    DwmSetWindowAttribute(f.Handle, 35, ref cap, 4);
                    DwmSetWindowAttribute(f.Handle, 36, ref txt, 4);
                }
            }
            catch { }
        }

        // One call per form: colours on every new handle (WinForms can recreate it), border follows activation.
        public static void Hook(Form f)
        {
            f.HandleCreated += delegate { Window(f, Form.ActiveForm == f); };
            f.Activated += delegate { Window(f, true); };
            f.Deactivate += delegate { Window(f, false); };
        }

        static void Scroll(Control c)
        {
            try
            {
                AllowDark(c.Handle);
                if (c is ComboBox) SetWindowTheme(c.Handle, Theme.Dark ? "DarkMode_CFD" : "CFD", null);
                else SetWindowTheme(c.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch { }
        }

        static bool NeedsScrollTheme(Control c)
        {
            var sc = c as ScrollableControl;
            return (sc != null && sc.AutoScroll) || c is DataGridView || c is ListView || c is ComboBox || (c is TextBoxBase && ((TextBoxBase)c).Multiline) || c is TreeView;
        }

        // Applies to a control tree now and to anything added to it later.
        public static void Polish(Control root)
        {
            var l = root as Label; if (l != null) l.UseMnemonic = false;
            var b = root as ButtonBase; if (b != null) b.UseMnemonic = false;
            Skin.Apply(root);
            if (NeedsScrollTheme(root))
            {
                if (root.IsHandleCreated) Scroll(root);
                root.HandleCreated += delegate { Scroll(root); };
            }
            foreach (Control c in root.Controls) Polish(c);
            root.ControlAdded += (s, e) => Polish(e.Control);
        }
    }

    // Screen-reader names for controls that pages create without one (WCAG 4.1.2 / 3.3.2):
    // inputs take the label just before them, lists and grids take the page name.
    public static class A11y
    {
        public static void Fill(Control root, string page)
        {
            Label last = null;
            foreach (Control c in root.Controls)
            {
                if (c is Label && !(c is Chip) && c.Text.Length > 0 && c.Text.Length < 60) last = (Label)c;
                if (string.IsNullOrEmpty(c.AccessibleName))
                {
                    if ((c is TextBox && !((TextBox)c).ReadOnly) || c is ComboBox || c is NumericUpDown)
                    { if (last != null) c.AccessibleName = last.Text.TrimEnd(':', ' '); }
                    else if (c is ListView || c is DataGridView || c is ListBox || c is TreeView) c.AccessibleName = page + " list";
                    else if (c is TextBox) c.AccessibleName = page + " output";
                }
                if (c.HasChildren) Fill(c, page);
            }
        }
    }
}
