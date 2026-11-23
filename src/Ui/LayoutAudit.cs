// 3.42: finds text that doesn't fit its control, for each interface language. German and Italian run about
// 30 % longer than English, so a fixed-size label or button that fits in English can cut its text off.
//
// Start ArchTweaker with ARCHTWEAKER_LAYOUT_AUDIT=<report file>: after start-up it opens every page once,
// measures each fixed-size label, button and check box, writes the report and goes back to Home.
// The e2e test "test_layout_fits" reads the report in each language. Owner-drawn controls (tweak rows,
// sidebar, toasts) are not checked: they draw their own text with ellipsis.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ArchTweaker
{
    public static class LayoutAudit
    {
        public const string EnvVar = "ARCHTWEAKER_LAYOUT_AUDIT";

        public sealed class Clip
        {
            public string Page, Kind, Name, Text;
            public Size Has, Needs;
            public override string ToString()
            {
                return string.Format("{0}\t{1}\t{2}\t{3}x{4} < {5}x{6}\t{7}", Page, Kind, Name, Has.Width, Has.Height, Needs.Width, Needs.Height, Text.Replace("\r", " ").Replace("\n", " "));
            }
        }

        public static string ReportFile { get { return Environment.GetEnvironmentVariable(EnvVar); } }

        // True when the control's text needs more room than the control has. Controls that size themselves,
        // or cut their text with "…" on purpose, never count. No window handle needed, so unit tests can call it.
        public static bool Clipped(Control c, out Size has, out Size needs)
        {
            has = needs = Size.Empty;
            string text = c.Text;
            if (string.IsNullOrEmpty(text) || !(c is Label || c is ButtonBase)) return false;
            if (c.Width <= 0 || c.Height <= 0) return false;
            var lbl = c as Label;
            if (lbl != null && lbl.AutoEllipsis) return false;
            if (c.AutoSize && c.MaximumSize.Width == 0) return false;
            var pad = c.Padding;
            int innerW = c.ClientSize.Width - pad.Horizontal;
            int innerH = c.ClientSize.Height - pad.Vertical;
            if (c is ButtonBase)
            {
                innerW -= c is CheckBox || c is RadioButton ? 20 : 8;    // box glyph or button border
                innerH -= 4;
            }
            if (innerW <= 0 || innerH <= 0) return false;
            bool wraps = lbl != null || innerH > c.Font.Height * 2;
            Size need = wraps
                ? TextRenderer.MeasureText(text, c.Font, new Size(innerW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl)
                : TextRenderer.MeasureText(text, c.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            has = new Size(innerW, innerH);
            needs = need;
            // a pixel or two of rounding is not a cut
            return wraps ? need.Height > innerH + 2 : need.Width > innerW + 2;
        }

        // skip: a part of the tree that is checked on its own (the page area, when checking the window chrome)
        public static List<Clip> Check(Control root, string page, Control skip = null)
        {
            var found = new List<Clip>();
            Walk(root, page, found, skip);
            return found;
        }

        // Number of text controls looked at by the last Check calls (the report shows it, so "0 cut" can't hide "0 checked")
        public static int Checked;

        // True when a self-sizing control runs past the right edge of the panel that holds it: the panel cuts it off.
        public static bool Overflows(Control c, out Size has, out Size needs)
        {
            has = needs = Size.Empty;
            var p = c.Parent;
            if (p == null || string.IsNullOrEmpty(c.Text) || !(c is Label || c is ButtonBase)) return false;
            var sp = p as ScrollableControl;
            if (sp != null && sp.AutoScroll) return false;
            int room = p.ClientSize.Width - (p.Padding.Right);
            if (c.Right <= room + 2 || c.Left >= room) return false;
            has = new Size(Math.Max(0, room - c.Left), c.Height);
            needs = c.Size;
            return true;
        }

        static void Walk(Control c, string page, List<Clip> found, Control skip)
        {
            if (!c.Visible || c == skip) return;
            Size has, needs;
            if (!string.IsNullOrEmpty(c.Text) && (c is Label || c is ButtonBase)) Checked++;
            if (Clipped(c, out has, out needs) || Overflows(c, out has, out needs))
                found.Add(new Clip { Page = page, Kind = c.GetType().Name, Name = c.AccessibleName ?? c.Name ?? "", Text = c.Text, Has = has, Needs = needs });
            foreach (Control k in c.Controls) Walk(k, page, found, skip);
        }

        public static string Format(string lang, IEnumerable<Clip> clips)
        {
            var list = clips.ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("# ArchTweaker {0} layout audit, language {1}: {2} cut-off text(s) in {3} checked", Program.Version, lang, list.Count, Checked));
            foreach (var c in list) sb.AppendLine(c.ToString());
            return sb.ToString();
        }

        public static void Write(string file, string lang, IEnumerable<Clip> clips)
        {
            try
            {
                var dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = file + ".tmp";
                File.WriteAllText(tmp, Format(lang, clips), new UTF8Encoding(false));
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);   // the e2e test waits for this file, so it appears only when complete
            }
            catch (Exception ex) { Log.W("Layout audit: can't write {0}: {1}", file, ex.Message); }
        }
    }
}
