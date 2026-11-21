// ArchTweaker: Customize page: everyday Windows settings as choices and numbers (3.39).
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // Each setting is a card with a drop-down or a number box. A change is a journaled registry write,
    // so "Put back all" here and "Undo everything" restore the value Windows had.
    public class CustomizePage : CardPage, IPage
    {
        bool loading;
        public CustomizePage()
        {
            Controls.Add(Note("Everyday Windows settings in one place. ArchTweaker records each change in the undo journal: 'Put back all' or 'Undo everything' restores what Windows had."));
            var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 6) };
            bar.Controls.Add(Theme.Btn("Put back all", delegate
            {
                int n = CustomSettings.PutBackAll();
                Log.Say(n > 0 ? "Put back {0} setting(s)." : "Nothing on this page was changed.", n);
                Reload();
            }));
            bar.Controls.Add(Theme.Btn("Restart Explorer", delegate { Tools.RestartExplorer(); }));
            Controls.Add(bar);
            string group = null;
            foreach (var s in CustomSettings.All.Where(x => x.Shown))
            {
                if (s.Group != group) { Heading(s.Group, group == null); group = s.Group; }
                Group(s.Title, Editor(s), Note(L.F("Takes effect {0}.", L.T(s.AppliesWhen))));
            }
        }

        Control Editor(CustomSetting s)
        {
            if (s.IsChoice)
            {
                var c = Theme.Drop(220, s.Title);
                c.BackColor = Theme.Panel; c.ForeColor = Theme.Text; c.FlatStyle = FlatStyle.Flat; c.Font = Theme.F;
                foreach (var l in s.Labels) c.Items.Add(L.T(l));
                c.Tag = s;
                c.SelectionChangeCommitted += delegate { if (!loading && c.SelectedIndex >= 0) Change(s, s.Values[c.SelectedIndex]); };
                return c;
            }
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var n = new NumericUpDown { Width = 110, Minimum = s.Min, Maximum = s.Max, BackColor = Theme.Bg, ForeColor = Theme.Text, Margin = new Padding(0, 5, 8, 0), AccessibleName = s.Title, Tag = s };
            n.ValueChanged += delegate { if (!loading) Change(s, (int)n.Value); };
            row.Controls.Add(n);
            if (s.Unit.Length > 0) row.Controls.Add(new Label { Text = L.T(s.Unit), AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 0), UseMnemonic = false });
            return row;
        }

        static void Change(CustomSetting s, int v)
        {
            try { s.Set(v); Native.BroadcastSettingChange(); Log.Say("{0}: changed. Takes effect {1}.", L.T(s.Title), L.T(s.AppliesWhen)); }
            catch (Exception ex) { Log.Say("{0}: could not be changed ({1}).", L.T(s.Title), ex.Message); }
        }

        // reads every value again and shows it without writing
        void Reload()
        {
            loading = true;
            try
            {
                foreach (var c in AllControls(this))
                {
                    var s = c.Tag as CustomSetting; if (s == null) continue;
                    int v = s.Current();
                    var cb = c as ComboBox; if (cb != null) cb.SelectedIndex = Array.IndexOf(s.Values, v);
                    var nu = c as NumericUpDown; if (nu != null) nu.Value = v;
                }
            }
            finally { loading = false; }
        }
        static System.Collections.Generic.IEnumerable<Control> AllControls(Control root)
        {
            foreach (Control c in root.Controls) { yield return c; foreach (var d in AllControls(c)) yield return d; }
        }

        public void OnShow() { Reload(); }
        public void Filter(string q) { }
    }
}
