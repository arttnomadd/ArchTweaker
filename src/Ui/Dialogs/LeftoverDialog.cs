// ArchTweaker: uninstaller leftovers dialog (moved here in 3.31).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // Checklist dialog for leftover folders/registry branches.
    public class LeftoverDialog : Form
    {
        public readonly List<Leftover> Chosen = new List<Leftover>();
        readonly List<Leftover> items; readonly List<CheckBox> boxes = new List<CheckBox>();
        public LeftoverDialog(string name, List<Leftover> found)
        {
            items = found;
            Text = "Leftovers — " + name; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F; Chrome.Hook(this);
            Width = 760; Height = 460; StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false;
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
            foreach (var lo in found)
            {
                // Only high-confidence items (the recorded install folder) are pre-checked; fuzzy data/registry
                // matches start unchecked so the user opts in deliberately.
                var cb = new TickBox { Checked = lo.Confident, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, AutoSize = true, UseMnemonic = false,
                    Text = (lo.IsReg ? "[registry] " + lo.Hive + "\\" + lo.RegPath : lo.Path + "   (" + Units.MbGb(lo.Size) + ")") + "   — " + lo.Why };
                boxes.Add(cb); list.Controls.Add(cb);
            }
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Panel, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(10, 9, 10, 9) };
            var ok = Theme.Primary("Remove checked", delegate { for (int i = 0; i < boxes.Count; i++) if (boxes[i].Checked) Chosen.Add(items[i]); DialogResult = DialogResult.OK; Close(); });
            var cancel = Theme.Btn("Cancel", delegate { DialogResult = DialogResult.Cancel; Close(); });
            bar.Controls.Add(ok); bar.Controls.Add(cancel);
            Controls.Add(list); Controls.Add(bar);
        }
    }
}
