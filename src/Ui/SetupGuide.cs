// ArchTweaker: First-run setup guide.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= FIRST-RUN GUIDE =================
    // Three short steps the first time ArchTweaker opens: safety, a starting point, what happens next.
    // Every step has Skip. Nothing is applied here: the guide only ticks tweaks, the person still reviews and applies.
    public class SetupGuide : Form
    {
        public string Preset;          // null = choose myself
        public bool RestorePoint = true;
        int step;
        readonly Panel body;
        readonly Label stepLabel, h1;
        readonly Button next, back;
        readonly CheckBox rp;
        readonly List<RadioButton> picks = new List<RadioButton>();
        static readonly string[] PresetKeys = { Catalog.PRESET_AUTO, Catalog.PRESET_SAFE, Catalog.PRESET_GAMING, Catalog.PRESET_PRIVACY, null };
        static readonly string[] PresetText =
        {
            "The picks for this PC (recommended)|Chosen for your CPU, GPU, storage and Windows build by the PC advisor.",
            "Safe|Only tweaks with no trade-off. A good first step on a work PC.",
            "Gaming|Latency, power and background work for games. Includes a few moderate tweaks.",
            "Privacy|Telemetry, ads and tracking. Little effect on speed.",
            "Nothing yet — I'll choose myself|Browse the pages and tick tweaks one by one."
        };

        public static bool Due
        {
            get
            {
                if (Environment.GetEnvironmentVariable("ARCHTWEAKER_NO_WIZARD") == "1") return false;
                return Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "SetupDone") == null;
            }
        }
        public static void MarkDone()
        {
            try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\ArchTweaker")) k.SetValue("SetupDone", 1, Microsoft.Win32.RegistryValueKind.DWord); } catch { }
        }

        public SetupGuide(bool restorePoint)
        {
            RestorePoint = restorePoint;
            Text = "Set up ArchTweaker"; BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(640), Theme.S(470)); FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false; KeyPreview = true;
            Chrome.Hook(this);

            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(92), BackColor = Theme.Bg };
            stepLabel = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.FS, Location = new Point(Theme.S(26), Theme.S(18)), UseMnemonic = false };
            h1 = new Label { AutoSize = true, Font = Theme.FH, ForeColor = Theme.Strong, Location = new Point(Theme.S(26), Theme.S(38)), UseMnemonic = false };
            head.Controls.Add(stepLabel); head.Controls.Add(h1);
            body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = Theme.S(new Padding(26, 0, 26, 8)) };

            rp = new TickBox { Text = L.T("Create a restore point before the first apply (recommended)"), Checked = restorePoint, AutoSize = true, ForeColor = Theme.Text, Font = Theme.FB, Margin = Theme.S(new Padding(0, 12, 0, 0)) };
            var foot = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(62), BackColor = Theme.Side, Padding = Theme.S(new Padding(22, 14, 22, 12)) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Theme.Side };
            next = Theme.Primary("Next", delegate { Go(step + 1); });
            back = Theme.Btn("Back", delegate { Go(step - 1); });
            var skip = Theme.Btn("Skip setup", delegate { Preset = null; RestorePoint = rp.Checked; DialogResult = DialogResult.Cancel; Close(); });
            flow.Controls.Add(next); flow.Controls.Add(back); flow.Controls.Add(new Panel { Width = Theme.S(10), Height = 1, BackColor = Theme.Side });
            var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Theme.Side };
            left.Controls.Add(skip);
            foot.Controls.Add(flow); foot.Controls.Add(left);
            CancelButton = skip;

            for (int i = 0; i < PresetText.Length; i++)
            {
                var parts = PresetText[i].Split('|');
                var r = new RadioButton { Text = L.T(parts[0]), Checked = i == 0, AutoSize = true, ForeColor = Theme.Text, Font = Theme.FB, Margin = Theme.S(new Padding(0, 8, 0, 0)), Tag = parts[1], AccessibleDescription = L.T(parts[1]) };
                picks.Add(r);
            }
            Controls.Add(body); Controls.Add(foot); Controls.Add(head);
            L.Apply(foot);
            Go(0);
            Chrome.Polish(this);
            Shown += delegate { next.Focus(); };
        }

        Label Para(string text, Color fg, Font f, int top)
        {
            return new Label { Text = L.T(text), ForeColor = fg, Font = f, AutoSize = true, MaximumSize = new Size(Theme.S(580), 0), Margin = Theme.S(new Padding(0, top, 0, 0)), UseMnemonic = false };
        }

        void Go(int s)
        {
            if (s < 0) return;
            if (s > 2)
            {
                int i = picks.FindIndex(r => r.Checked);
                Preset = i >= 0 ? PresetKeys[i] : null; RestorePoint = rp.Checked;
                DialogResult = DialogResult.OK; Close(); return;
            }
            step = s;
            body.SuspendLayout();
            // the check box and radio buttons keep their state between steps: detach them before the old step is disposed
            if (rp.Parent != null) rp.Parent.Controls.Remove(rp);
            foreach (var r in picks) if (r.Parent != null) r.Parent.Controls.Remove(r);
            foreach (Control c in body.Controls.Cast<Control>().ToList()) { body.Controls.Remove(c); c.Dispose(); }
            stepLabel.Text = L.T("Step " + (s + 1) + " of 3");
            back.Visible = s > 0;
            next.Text = L.T(s == 2 ? "Finish" : "Next");
            var f = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg };
            if (s == 0)
            {
                h1.Text = L.T("Welcome to ArchTweaker");
                f.Controls.Add(Para("ArchTweaker changes Windows settings for speed and privacy. Before anything changes, you see the exact list. ArchTweaker saves every original value, so you can put it back.", Theme.Text, Theme.F, 0));
                f.Controls.Add(rp);
                f.Controls.Add(Para("A restore point takes about a minute. It lets Windows go back to today's state even if ArchTweaker can't start.", Theme.Muted, Theme.FS, 2));
            }
            else if (s == 1)
            {
                h1.Text = L.T("Pick a starting point");
                f.Controls.Add(Para("This only ticks tweaks. You can untick any of them, and nothing changes until you click Apply.", Theme.Text, Theme.F, 0));
                foreach (var r in picks)
                {
                    f.Controls.Add(r);
                    var d = Para((string)r.Tag, Theme.Muted, Theme.FS, 0); d.Margin = Theme.S(new Padding(18, 0, 0, 0));
                    f.Controls.Add(d);
                }
            }
            else
            {
                h1.Text = L.T("What happens next");
                int i = picks.FindIndex(r => r.Checked);
                string chosen = i >= 0 && PresetKeys[i] != null ? L.T(PresetText[i].Split('|')[0]) : null;
                var lead = Para(chosen != null ? "ArchTweaker ticks the tweaks for your choice:" : "Nothing is ticked. Open any page in the sidebar to start.", Theme.Text, Theme.F, 0);
                if (chosen != null) lead.Text += " " + chosen + ".";
                f.Controls.Add(lead);
                f.Controls.Add(Para("1.  Check the ticked tweaks. The log lists each one, and the details panel says what it changes.", Theme.Text, Theme.F, 14));
                f.Controls.Add(Para("2.  Click Apply. A review shows every change, the restore point and what needs a restart.", Theme.Text, Theme.F, 8));
                f.Controls.Add(Para("3.  Changed your mind? Undo appears right after Apply, and 'Undo everything' puts back what you had before ArchTweaker.", Theme.Text, Theme.F, 8));
                f.Controls.Add(Para("You can run this guide again from the command palette (Ctrl+K).", Theme.Muted, Theme.FS, 14));
            }
            body.Controls.Add(f);
            body.ResumeLayout();
            Chrome.Polish(f);
        }
    }
}
