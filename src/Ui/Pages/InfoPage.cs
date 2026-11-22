// ArchTweaker: System info page.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= INFO =================
    // 3.14: one card per area (Windows, CPU and memory, graphics, storage, network), each with its own Copy
    public class InfoPage : Panel, IPage
    {
        readonly FlowLayoutPanel list; string text = "";
        public InfoPage()
        {
            BackColor = Theme.Bg;
            list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(0, 4, 0, 8) };
            list.Resize += delegate { Fit(); };
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Btn("Refresh", delegate { Load2(); }));
            bar.Controls.Add(Theme.Btn("Copy all", delegate { if (text.Length > 0) try { Clipboard.SetText(text); MainForm.Instance.Notify("System info copied", Toast.Kind.Ok); } catch { } }));
            Controls.Add(list); Controls.Add(bar);
        }
        bool loaded;
        public void OnShow() { if (!loaded) Load2(); }
        void Fit()
        {
            int w = Math.Max(Theme.S(360), Math.Min(Theme.S(Theme.MaxContent), list.ClientSize.Width - list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - Theme.S(4)));
            foreach (var c in list.Controls.OfType<KvCard>()) if (c.Width != w) c.Fit(w);
        }
        void Load2()
        {
            loaded = true; string t = null;
            MainForm.Instance.Load("Reading hardware…", () => { t = SysInfo.Gather(); }, () =>
            {
                text = t ?? "";
                list.SuspendLayout();
                foreach (Control c in list.Controls.Cast<Control>().ToList()) { list.Controls.Remove(c); c.Dispose(); }
                foreach (var card in KvCard.Parse(text)) list.Controls.Add(card);
                list.ResumeLayout();
                Fit();
            });
        }
        public void Filter(string q) { }
    }
}
