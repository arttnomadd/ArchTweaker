// Carbon-styled dialogs (2.5). Replaces bare MessageBox Yes/No with buttons named after the action,
// a title that states the question, and an optional scrollable details box for long lists.
// Enter = the action, Esc = cancel. Destructive confirms put the focus on cancel.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArchTweaker
{
    public static class Dlg
    {
        public enum Kind { Info, Warn, Error, Question }

        static IWin32Window Owner()
        {
            var f = Form.ActiveForm;
            if (f != null) return f;
            return MainForm.Instance != null && MainForm.Instance.IsHandleCreated ? MainForm.Instance : null;
        }

        // Returns true when the user picks the action button.
        public static bool Confirm(string title, string body, string action, string cancel = "Cancel", bool destructive = false, string details = null)
        {
            return Show(destructive ? Kind.Warn : Kind.Question, title, body, details, action, cancel, destructive) == DialogResult.OK;
        }
        public static void Info(string title, string body, string details = null) { Show(Kind.Info, title, body, details, "OK", null, false); }
        public static void Warn(string title, string body, string details = null) { Show(Kind.Warn, title, body, details, "OK", null, false); }
        // Errors: what happened (title), why / how to fix (body), raw error text in details.
        public static void Error(string title, string body, string details = null) { Show(Kind.Error, title, body, details, "OK", null, false); }

        static DialogResult Show(Kind kind, string title, string body, string details, string action, string cancel, bool destructive)
        {
            // translate before layout: label heights are measured from the final text
            title = L.T(title); body = L.T(body); action = L.T(action); cancel = L.T(cancel);
            try
            {
                using (var f = Build(kind, title, body, details, action, cancel, destructive))
                {
                    var o = Owner();
                    return o != null ? f.ShowDialog(o) : f.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                // a broken theme must never hide a question; fall back to the system box
                Log.W("Dialog fallback: {0}", ex.Message);
                string text = body + (string.IsNullOrEmpty(details) ? "" : "\n\n" + details);
                if (cancel == null) { MessageBox.Show(text, title); return DialogResult.OK; }
                return MessageBox.Show(text, title, MessageBoxButtons.OKCancel) == DialogResult.OK ? DialogResult.OK : DialogResult.Cancel;
            }
        }

        // measured up front: AutoSize labels only wrap once they have a handle, which is too late for layout
        static Label Text(string text, Font font, Color fg, int x, int y, int w)
        {
            var sz = TextRenderer.MeasureText(text.Length == 0 ? " " : text, font, new Size(w, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return new Label { Text = text, Font = font, ForeColor = fg, Location = new Point(x, y), Size = new Size(w, text.Length == 0 ? 0 : sz.Height + 2), AutoSize = false, UseMnemonic = false };
        }
        static Form Build(Kind kind, string title, string body, string details, string action, string cancel, bool destructive)
        {
            var f = new Form
            {
                Text = "ArchTweaker", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.F,
                AutoScaleMode = AutoScaleMode.None, KeyPreview = true, AccessibleName = title
            };
            Chrome.Hook(f);
            int W = Theme.S(480), pad = Theme.S(22);
            Color tone = kind == Kind.Error ? Theme.Bad : kind == Kind.Warn ? Theme.Warn : Theme.Accent;
            var edge = new Panel { Location = new Point(0, 0), Size = new Size(W + pad * 2, Theme.S(3)), BackColor = tone };
            // 3.16: a severity glyph before the title (warning, error, info); title and body share one text column
            int gx = 0; var icf = NavItem.IconFont;
            if (icf != null)
            {
                var gl = new Label { Text = kind == Kind.Error ? "\uEA39" : kind == Kind.Warn ? "\uE7BA" : "\uE946", Font = new Font(icf.FontFamily, Theme.S(20f), GraphicsUnit.Pixel),
                    ForeColor = tone, AutoSize = true, Location = new Point(pad, pad), UseMnemonic = false, AccessibleRole = AccessibleRole.Graphic };
                f.Controls.Add(gl); gx = Theme.S(32);
            }
            var t = Text(title, Theme.Semi(12f), Theme.Strong, pad + gx, pad, W - gx);
            var b = Text(body ?? "", Theme.F, Theme.Text, pad + gx, t.Bottom + Theme.S(10), W - gx);
            f.Controls.Add(edge); f.Controls.Add(t); f.Controls.Add(b);
            int y = b.Text.Length > 0 ? b.Bottom + Theme.S(14) : t.Bottom + Theme.S(14);
            if (!string.IsNullOrEmpty(details))
            {
                int lines = details.Split('\n').Length;
                var d = new TextBox
                {
                    Text = details.Replace("\r\n", "\n").Replace("\n", "\r\n"), Multiline = true, ReadOnly = true, WordWrap = true,
                    ScrollBars = lines > 8 ? ScrollBars.Vertical : ScrollBars.None, BorderStyle = BorderStyle.None,
                    BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.Mono, Location = new Point(pad, y),
                    Size = new Size(W, Math.Min(Theme.S(180), Theme.S(8) + Math.Max(1, lines) * (Theme.Mono.Height + 1))), AccessibleName = "Details", TabStop = true
                };
                f.Controls.Add(d); y = d.Bottom + Theme.S(16);
            }
            var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Location = new Point(pad, y), Size = new Size(W, Theme.S(36)), WrapContents = false, BackColor = Theme.Bg };
            Button ok = Theme.Primary(action, null); ok.DialogResult = DialogResult.OK; ok.Margin = new Padding(0); ok.Padding = Theme.S(ok.Padding); ok.MinimumSize = new Size(0, Theme.S(30));
            bar.Controls.Add(ok);
            Button no = null;
            if (cancel != null) { no = Theme.Btn(cancel, null); no.DialogResult = DialogResult.Cancel; no.Margin = new Padding(0, 0, Theme.S(8), 0); no.Padding = Theme.S(no.Padding); no.MinimumSize = new Size(0, Theme.S(30)); bar.Controls.Add(no); }
            f.Controls.Add(bar);
            f.AcceptButton = destructive && no != null ? (IButtonControl)no : ok;
            f.CancelButton = no ?? ok;
            f.ClientSize = new Size(W + pad * 2, bar.Bottom + pad - Theme.S(4));
            f.Shown += delegate { (destructive && no != null ? no : ok).Focus(); };
            if (kind == Kind.Error) System.Media.SystemSounds.Hand.Play();
            else if (kind == Kind.Warn) System.Media.SystemSounds.Exclamation.Play();
            return f;
        }
    }
}
