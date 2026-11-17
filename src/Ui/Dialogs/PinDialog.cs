// ArchTweaker: PIN dialog for Pro mode (3.23; moved here in 3.31).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    public class PinDialog : Form
    {
        readonly TextBox box = new TextBox { UseSystemPasswordChar = true, MaxLength = 12, Width = 200 };
        public static string Show(IWin32Window owner, string title, string prompt)
        {
            using (var d = new PinDialog(title, prompt)) return d.ShowDialog(owner) == DialogResult.OK ? d.box.Text : null;
        }
        PinDialog(string title, string prompt)
        {
            Text = L.T(title); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F; AutoScaleMode = AutoScaleMode.None;
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(Theme.S(16)), WrapContents = false, AutoSize = true };
            flow.Controls.Add(new Label { Text = L.T(prompt), AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, Theme.S(8)), UseMnemonic = false });
            box.BackColor = Theme.Panel; box.ForeColor = Theme.Text; box.Width = Theme.S(220);
            box.KeyPress += (s, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            flow.Controls.Add(box);
            var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, Theme.S(12), 0, 0) };
            var cancel = Theme.Btn("Cancel", delegate { DialogResult = DialogResult.Cancel; Close(); });
            var ok = Theme.Lead("OK", delegate { DialogResult = DialogResult.OK; Close(); });
            bar.Controls.Add(cancel); bar.Controls.Add(ok);
            flow.Controls.Add(bar);
            Controls.Add(flow);
            AcceptButton = ok; CancelButton = cancel;
            ClientSize = new Size(Theme.S(300), Theme.S(150));
        }
    }
}
