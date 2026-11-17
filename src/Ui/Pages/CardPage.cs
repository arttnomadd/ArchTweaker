// ArchTweaker: a page made of section headings and setting cards, with a jump bar at the top (3.10.5).
// Shared by Tools & backup and Settings (3.31).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    public abstract class CardPage : FlowLayoutPanel
    {
        protected CardPage()
        {
            FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true; BackColor = Theme.Bg;
        }

        readonly List<KeyValuePair<string, Label>> sections = new List<KeyValuePair<string, Label>>();
        FlowLayoutPanel jump;
        protected void Heading(string text, bool first)
        {
            if (first)
            {
                jump = new FlowLayoutPanel { AutoSize = true, WrapContents = true, BackColor = Theme.Bg, Margin = new Padding(0, 0, 0, 6) };
                Controls.Add(jump);
            }
            var h = new Label { Text = text, Font = Theme.FH, ForeColor = Theme.Text, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, first ? 6 : 26, 0, 10) };
            Controls.Add(h);
            sections.Add(new KeyValuePair<string, Label>(text, h));
            var b = Theme.Btn(text, delegate { AutoScrollPosition = new Point(0, h.Top - AutoScrollPosition.Y - Theme.S(8)); });
            b.Margin = new Padding(0, 0, 6, 4);
            jump.Controls.Add(b);
        }
        protected static Label Note(string t) { return new Label { Text = t, ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, UseMnemonic = false, Tag = "note" }; }
        // 3.13: Home's "Last apply" tile opens the page at a section
        public void JumpTo(string heading)
        {
            foreach (var kv in sections) if (kv.Key == heading) { var h = kv.Value; AutoScrollPosition = new Point(0, h.Top - AutoScrollPosition.Y - Theme.S(8)); return; }
        }
        // cards fill the page up to a readable width
        protected override void OnLayout(LayoutEventArgs e)
        {
            int w = Math.Max(Theme.S(360), Math.Min(Theme.S(Theme.MaxContent), ClientSize.Width - Padding.Horizontal - Theme.S(8)));
            foreach (var c in Controls.OfType<SettingsCard>()) if (c.MinimumSize.Width != w) c.SetWidth(w);
            if (jump != null) jump.MaximumSize = new Size(w, 0);
            base.OnLayout(e);
        }
        protected static bool Safe(Func<bool> f) { try { return f(); } catch { return false; } }
        protected void Group(string title, params Control[] items)
        {
            var card = new SettingsCard(title);
            foreach (var c in items) card.Add(c);
            Controls.Add(card);
        }
    }
}
