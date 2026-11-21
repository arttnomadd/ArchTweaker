// ArchTweaker 3.22: Lite and Pro mode in one app.
//  - Lite (the default) shows only tweaks with Safe risk that are not experimental, not in the Danger zone and
//    not Command tweaks, and hides the tools that change devices, drivers, services or Windows images.
//  - Pro shows everything. It is turned on only after the user reads the warning and ticks that they accept it.
//  - The mode only changes what the window shows. Applied tweaks stay applied in both modes, and Undo last,
//    Undo everything, Change history and the startup recovery work the same in both.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class Mode
    {
        public const int DisclaimerVersion = 1;
        const string Key = @"Software\ArchTweaker";

        // Pages that change devices, drivers, services, apps, power internals or Windows images: Pro only.
        static readonly HashSet<string> ProPages = new HashSet<string>(StringComparer.Ordinal)
        {
            "Tuning assistant", "Interrupt tuner", "Power tuner", "Tune everything", "A/B test", "Deep clean", "Services", "Apps (UWP)", "Scheduled tasks",
            "Power settings", "Hosts blocker", "Drivers", "Bake into ISO"
        };

        static bool? pro;
        public static bool Pro
        {
            get
            {
                if (!pro.HasValue)
                {
                    pro = false;
                    try { pro = string.Equals(Reg.Get(Hive.HKCU, Key, "Mode") as string, "Pro", StringComparison.OrdinalIgnoreCase) && Accepted; } catch { }
                }
                return pro.Value;
            }
        }
        public static bool Lite { get { return !Pro; } }

        public static bool Accepted
        {
            get { try { object o = Reg.Get(Hive.HKCU, Key, "ProAccepted"); return o is int && (int)o >= DisclaimerVersion; } catch { return false; } }
        }

        public static void Set(bool toPro)
        {
            pro = toPro;
            try
            {
                using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(Key))
                {
                    k.SetValue("Mode", toPro ? "Pro" : "Lite", RegistryValueKind.String);
                    if (toPro)
                    {
                        k.SetValue("ProAccepted", DisclaimerVersion, RegistryValueKind.DWord);
                        k.SetValue("ProAcceptedOn", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    }
                }
            }
            catch (Exception ex) { Log.W("Mode not saved: {0}", ex.Message); }
            Log.W(toPro ? "Pro mode is on." : "Lite mode is on.");
        }

        // ---------- pure rules (unit-tested) ----------
        public static bool LiteTweak(Tweak t)
        {
            return t != null && t.Risk == Risk.Safe && !t.Dangerous && !t.Experimental
                && t.Category != DangerZone.CAT && t.Category != Cat.Commands;
        }
        public static bool ProPage(string name) { return ProPages.Contains(name ?? ""); }

        // ---------- what the window shows ----------
        public static bool Allows(Tweak t) { return Pro || LiteTweak(t); }
        public static bool ShowsPage(string name) { return Pro || !ProPage(name); }
        // a tweak category with no Lite tweak gets no sidebar entry in Lite
        public static bool ShowsCategory(string cat) { return Pro || Catalog.All.Any(t => t.Category == cat && LiteTweak(t)); }

        // Applied tweaks that Lite hides (they stay applied; Undo everything still covers them).
        public static int HiddenApplied()
        {
            if (Pro) return 0;
            try { var ids = new HashSet<string>(AppliedSet.Read() ?? new List<string>()); return Catalog.All.Count(t => ids.Contains(t.Id) && !LiteTweak(t)); }
            catch { return 0; }
        }

        // Asks for the disclaimer when needed. True when the mode changed.
        public static bool Toggle(IWin32Window owner)
        {
            if (Pro) { Set(false); return true; }
            if (!ProPin.Ask(owner, "Pro mode")) return false;   // 3.23
            using (var d = new ProDisclaimer())
                if (d.ShowDialog(owner) != DialogResult.OK) return false;
            Set(true);
            return true;
        }
    }

    // The Pro mode warning. "Turn on Pro mode" stays off until the box is ticked.
    public class ProDisclaimer : Form
    {
        public static readonly string[] Points =
        {
            "Pro mode shows every tweak and tool. That includes advanced and experimental tweaks, the Danger zone and Command tweaks. It also includes the tuners, services, drivers, apps, power settings, the hosts blocker and Bake into ISO.",
            "These change deep Windows settings. A wrong choice can stop Windows from starting, or break the network, sound, Windows Update, security features, anti-cheat or other programs.",
            "Undo, restore points and backups help, but they do not always work. A Windows update, a driver update, a damaged system or a PC that does not start can leave you without ArchTweaker to undo anything.",
            "Use Pro mode only if you understand each change before you apply it. You must know how to repair Windows without ArchTweaker. That means Safe Mode, the recovery environment (WinRE) and System Restore. It also means importing a .reg backup, reinstalling drivers and, if needed, reinstalling Windows.",
            "Back up your files before you use Pro tweaks. Make a restore point, and keep a Windows USB installer at hand.",
            "You use Pro mode at your own risk. ArchTweaker comes as it is, with no warranty. Its author is not responsible for damage, data loss, a PC that does not start, lost game accounts or any other result of its use."
        };

        readonly CheckBox ok;
        readonly Button go;

        public ProDisclaimer()
        {
            Text = L.T("Pro mode"); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F;
            AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(Theme.S(620), Theme.S(560));
            var head = new Label { Text = L.T("Pro mode: read this first"), Font = Theme.FH, ForeColor = Theme.Bad, Dock = DockStyle.Top, Height = Theme.S(44), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Theme.S(16), 0, 0, 0), UseMnemonic = false };
            var body = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.F, DetectUrls = false, TabStop = false };
            foreach (var p in Points) { body.SelectionBullet = true; body.SelectionIndent = Theme.S(8); body.AppendText(L.T(p) + "\n\n"); }
            body.SelectionStart = 0;
            var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(16), Theme.S(4), Theme.S(16), Theme.S(8)), BackColor = Theme.Bg };
            wrap.Controls.Add(body);
            ok = new CheckBox { Text = L.T("I have read this. I understand the risks, I accept them, and I know how to repair Windows without ArchTweaker."), AutoSize = false, Dock = DockStyle.Bottom, Height = Theme.S(48), ForeColor = Theme.Text, Padding = new Padding(Theme.S(16), 0, Theme.S(16), 0), UseMnemonic = false };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(52), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Theme.S(12), Theme.S(8), Theme.S(12), 0), BackColor = Theme.Bg };
            var stay = Theme.Btn("Stay in Lite", delegate { DialogResult = DialogResult.Cancel; Close(); });
            go = Theme.Lead("Turn on Pro mode", delegate { if (ok.Checked) { DialogResult = DialogResult.OK; Close(); } });
            go.Enabled = false;
            ok.CheckedChanged += delegate { go.Enabled = ok.Checked; };
            bar.Controls.Add(stay); bar.Controls.Add(go);
            Controls.Add(wrap); Controls.Add(ok); Controls.Add(bar); Controls.Add(head);
            CancelButton = stay; AcceptButton = null;   // Enter never turns Pro mode on by accident
        }
    }

    // Lite / Pro switch for the bottom of the sidebar.
    public class ModeSwitch : Control
    {
        readonly Action changed;
        bool hot;
        public ModeSwitch(Action onChanged)
        {
            changed = onChanged;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Dock = DockStyle.Bottom; Height = 34; BackColor = Theme.Side; Cursor = Cursors.Hand; TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = L.T(Mode.Pro ? "Pro mode is on. Switch to Lite mode" : "Lite mode is on. Switch to Pro mode");
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            bool slim = Width < Theme.S(120);
            int pad = Theme.S(slim ? 6 : 12), h = Theme.S(24), y = (Height - h) / 2, w = Width - 2 * pad, half = w / 2;
            var r = new Rectangle(pad, y, w, h);
            using (var b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, r);
            using (var p = new Pen(hot ? Theme.Strong : Theme.Border)) g.DrawRectangle(p, r);
            var lite = new Rectangle(pad + 1, y + 1, half - 1, h - 1); var pro = new Rectangle(pad + half, y + 1, w - half, h - 1);
            using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, Mode.Pro ? pro : lite);   // 3.29: the accent shows the mode (neutral in Lite, the package accent in Pro); never the error colour
            Color on = Theme.OnAccent, off = Theme.Muted;
            TextRenderer.DrawText(g, slim ? "L" : L.T("Lite"), Theme.FS, lite, Mode.Pro ? off : on, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, slim ? "P" : L.T("Pro"), Theme.FS, pro, Mode.Pro ? on : off, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) Gfx.FocusRing(g, new RectangleF(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), 0);
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Flip(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { Flip(); e.Handled = true; } base.OnKeyDown(e); }
        void Flip() { var f = FindForm() as MainForm; if (f != null) f.SwitchMode(); else if (Mode.Toggle(FindForm())) changed(); }
    }
}
