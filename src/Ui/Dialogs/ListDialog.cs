// ArchTweaker: the shape every list dialog shares (3.31): an intro at the top, a dark list, a button bar at the bottom.
// Ghost devices, Leftover tweaks, Foreground boost and App settings use it.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArchTweaker
{
    public abstract class ListDialog : Form
    {
        protected readonly ListView List;
        protected readonly Label Intro;
        readonly FlowLayoutPanel bar;

        protected ListDialog(string title, Size size, Size minimum, string intro, int introHeight, bool checkBoxes)
        {
            Text = L.T(title); BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F;
            Size = size; MinimumSize = minimum; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            Chrome.Hook(this);
            List = new ListView { Dock = DockStyle.Fill, CheckBoxes = checkBoxes, FullRowSelect = true };
            Theme.DarkList(List);
            List.View = View.Details;
            Intro = new Label { Dock = DockStyle.Top, Height = introHeight, Padding = new Padding(16, 12, 16, 4), ForeColor = Theme.Text, UseMnemonic = false, Text = L.T(intro) };
            bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Side, Padding = new Padding(16, 10, 16, 10) };
            var close = Theme.Btn(L.T("Close"), delegate { Close(); });
            bar.Controls.Add(close);
            CancelButton = close;
            // docking runs from the last control added to the first: intro on top, bar at the bottom, list in the rest
            Controls.Add(List); Controls.Add(bar); Controls.Add(Intro);
            Chrome.Polish(this);
        }

        protected void Column(string name, int width) { List.Columns.Add(L.T(name), width); }
        // buttons line up right to left after Close: the first one added sits next to Close
        protected Button Main(string text, Action click) { var b = Theme.Primary(L.T(text), delegate { click(); }); bar.Controls.Add(b); return b; }
        protected Button Extra(string text, Action click) { var b = Theme.Btn(L.T(text), delegate { click(); }); bar.Controls.Add(b); return b; }
        // a strip under the intro (Top) or above the button bar (Bottom)
        protected void AddStrip(Control c)
        {
            Controls.Add(c);
            Controls.SetChildIndex(c, c.Dock == DockStyle.Top ? Controls.GetChildIndex(Intro) : Controls.GetChildIndex(List) + 1);
        }
    }
}
