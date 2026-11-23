// ArchTweaker: Danger zone banner and the warning gate before the first Danger zone tweak (moved out of Catalog in 3.31).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // The banner at the top of the Danger zone page: what this page is, and the lock.
    public class DangerBanner : Panel
    {
        readonly Label title, body, state; readonly Button act;
        public DangerBanner()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Margin = new Padding(0, 0, 0, Theme.S(12)); BackColor = Gfx.Mix(Theme.Panel, Theme.Risk, Theme.Dark ? 0.10 : 0.06);   // 3.29: risk magenta, not the brand or error red
            AccessibleRole = AccessibleRole.Alert; AccessibleName = L.T("Danger zone");
            title = new Label { Text = L.T("Danger zone"), Font = Theme.FH, ForeColor = Theme.Strong, AutoSize = true, UseMnemonic = false, BackColor = Color.Transparent };
            body = new Label
            {
                Text = L.T("Each tweak here turns off a protection or a feature Windows relies on, and says what it can break. None of them is in a preset or an automatic pick. Applying one always creates a restore point first, and asks you to confirm what it breaks."),
                ForeColor = Theme.Text, AutoSize = false, UseMnemonic = false, BackColor = Color.Transparent
            };
            state = new Label { Font = Theme.FT, AutoSize = true, UseMnemonic = false, BackColor = Color.Transparent };
            act = Theme.Btn("", delegate
            {
                if (DangerZone.Unlocked) DangerZone.Unlocked = false;
                else DangerZone.Ensure(FindForm());
            });
            Controls.Add(title); Controls.Add(body); Controls.Add(state); Controls.Add(act);
            DangerZone.Changed += Sync;
            Sync();
            Resize += delegate { Lay(); };
        }
        protected override void Dispose(bool disposing) { if (disposing) DangerZone.Changed -= Sync; base.Dispose(disposing); }
        void Sync()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((Action)Sync); return; }
            bool u = DangerZone.Unlocked;
            state.Text = L.T(u ? "Unlocked for this session" : "Locked: you can read every tweak, but not switch it");
            state.ForeColor = u ? Theme.Risk : Theme.Muted;
            act.Text = L.T(u ? "Lock again" : "Unlock for this session…");
            act.AccessibleName = act.Text;
            Lay(); Invalidate();
        }
        void Lay()
        {
            int pad = Theme.S(18), x = pad + Theme.S(34);
            title.Location = new Point(x, Theme.S(14));
            int w = Math.Max(Theme.S(200), Width - x - pad);
            body.Width = w;
            body.Height = TextRenderer.MeasureText(body.Text, body.Font, new Size(w, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + Theme.S(2);
            body.Location = new Point(x, title.Bottom + Theme.S(6));
            int y = body.Bottom + Theme.S(12);
            act.Location = new Point(x, y);
            state.Location = new Point(act.Right + Theme.S(12), y + (act.Height - state.Height) / 2);
            int h = act.Bottom + Theme.S(16);
            if (Height != h) Height = h;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Gfx.Round(r, Theme.S(Theme.Radius)))
            {
                using (var b = new SolidBrush(BackColor)) g.FillPath(b, p);
                using (var pen = new Pen(Gfx.Mix(BackColor, Theme.Bad, 0.45))) g.DrawPath(pen, p);
            }
            g.SmoothingMode = SmoothingMode.None;
            using (var b = new SolidBrush(Theme.Risk)) g.FillRectangle(b, 0, Theme.S(10), Theme.S(3), Height - Theme.S(20));
            var f = NavItem.IconFont;
            var gr = new Rectangle(Theme.S(18), Theme.S(14), Theme.S(24), title.Height);
            if (f != null) using (var big = new Font(f.FontFamily, f.Size * 1.35f, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, "", big, gr, Theme.Risk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            else TextRenderer.DrawText(g, "!", Theme.FH, gr, Theme.Risk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // Unlock confirmation: names what this page can do, and needs an explicit tick.
    public class DangerGate : Form
    {
        public DangerGate()
        {
            Text = L.T("Unlock the Danger zone"); BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F;
            Size = new Size(560, 410); MinimumSize = new Size(480, 360); StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedDialog;
            Chrome.Hook(this);

            var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(24, 18, 24, 6), BackColor = Theme.Bg };
            int w = 500;
            body.Controls.Add(new Label { Text = L.T("Unlock the Danger zone for this session?"), Font = Theme.FH, ForeColor = Theme.Strong, AutoSize = true, MaximumSize = new Size(w, 0), UseMnemonic = false, Margin = new Padding(0, 0, 0, 10) });
            body.Controls.Add(new Label { Text = L.T("These tweaks are here because they can:"), AutoSize = true, MaximumSize = new Size(w, 0), UseMnemonic = false, Margin = new Padding(0, 0, 0, 6) });
            foreach (var s in new[] { "turn off antivirus, firewall, UAC or exploit protection", "stop apps, anti-cheats, sign-in or virtualization from working", "risk data loss on a power cut, or a PC that doesn't sleep or boot normally" })
                body.Controls.Add(new Label { Text = "•  " + L.T(s), AutoSize = true, MaximumSize = new Size(w, 0), UseMnemonic = false, ForeColor = Theme.Text, Margin = new Padding(8, 0, 0, 4) });
            body.Controls.Add(new Label { Text = L.T("Unlocking only lets you switch them. Nothing changes until you click Apply, and every change can be undone. ArchTweaker locks the page again when it closes."), ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(w, 0), UseMnemonic = false, Margin = new Padding(0, 10, 0, 12) });
            // a long label wraps only with AutoSize off, so its height is measured here
            string ackText = L.T("I understand these can break things, and I'll read what each one breaks before I apply it.");
            var ack = new TickBox { Text = ackText, AutoSize = false, ForeColor = Theme.Strong, UseMnemonic = false, TextAlign = ContentAlignment.TopLeft, CheckAlign = ContentAlignment.TopLeft };
            ack.Size = new Size(w, TextRenderer.MeasureText(ackText, Theme.F, new Size(w - 24, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 6);
            body.Controls.Add(ack);

            var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Theme.Side, Padding = new Padding(24, 12, 16, 12) };
            var ok = Theme.Btn(L.T("Unlock for this session"), delegate { DialogResult = DialogResult.OK; Close(); });
            // the unlock button is red, so it never looks like the safe default
            EventHandler paint = delegate
            {
                ok.BackColor = ok.Enabled ? Theme.Bad : Theme.Panel; ok.ForeColor = ok.Enabled ? Theme.InkOn(Theme.Bad) : Theme.Muted;
                ok.FlatAppearance.BorderColor = ok.Enabled ? Theme.Bad : Theme.Border;
                ok.FlatAppearance.MouseOverBackColor = ok.Enabled ? Gfx.Mix(Theme.Bad, Color.Black, 0.15) : Theme.Panel;
            };
            ok.Enabled = false; ok.Font = Theme.FB; paint(ok, EventArgs.Empty); ok.EnabledChanged += paint;
            var cancel = Theme.Btn(L.T("Keep locked"), delegate { DialogResult = DialogResult.Cancel; Close(); });
            ack.CheckedChanged += delegate { ok.Enabled = ack.Checked; };
            foot.Controls.Add(ok); foot.Controls.Add(cancel);
            CancelButton = cancel;
            Controls.Add(body); Controls.Add(foot);
            Chrome.Polish(this);
            Shown += delegate { cancel.Focus(); };
        }
    }
}
