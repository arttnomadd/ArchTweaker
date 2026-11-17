// ArchTweaker: one-line text dialog, for names (3.30; moved here in 3.31).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ArchTweaker
{
    // one line of text, for names
    public class TextDialog : Form
    {
        readonly TextBox box = new TextBox { MaxLength = 64 };
        public static string Show(IWin32Window owner, string title, string prompt, string initial)
        {
            using (var d = new TextDialog(title, prompt, initial)) return d.ShowDialog(owner) == DialogResult.OK ? d.box.Text : null;
        }
        TextDialog(string title, string prompt, string initial)
        {
            Text = L.T(title); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F; AutoScaleMode = AutoScaleMode.None;
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(Theme.S(16)), WrapContents = false, AutoSize = true };
            flow.Controls.Add(new Label { Text = L.T(prompt), AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, Theme.S(8)), UseMnemonic = false });
            box.BackColor = Theme.Panel; box.ForeColor = Theme.Text; box.Width = Theme.S(380); box.Text = initial ?? "";
            box.AccessibleName = L.T(title);
            flow.Controls.Add(box);
            var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, Theme.S(12), 0, 0) };
            var cancel = Theme.Btn("Cancel", delegate { DialogResult = DialogResult.Cancel; Close(); });
            var ok = Theme.Lead("OK", delegate { DialogResult = DialogResult.OK; Close(); });
            bar.Controls.Add(cancel); bar.Controls.Add(ok);
            flow.Controls.Add(bar);
            Controls.Add(flow);
            AcceptButton = ok; CancelButton = cancel;
            ClientSize = new Size(Theme.S(420), Theme.S(170));
            Shown += delegate { box.SelectAll(); box.Focus(); };
        }
    }
}
