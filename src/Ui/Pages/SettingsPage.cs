// ArchTweaker: Settings page (3.31): language, look, notification area icon, app mode, help and the command line.
// These used to sit at the top of Tools & backup.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class SettingsPage : CardPage, IPage
    {
        // 3.23: set or remove the PIN that Pro mode asks for
        void PinSetup()
        {
            if (ProPin.IsSet)
            {
                if (!ProPin.Ask(FindForm(), "Remove the Pro PIN")) return;
                ProPin.Set(null); MainForm.Instance.ModeSwitched(); return;
            }
            string a = PinDialog.Show(FindForm(), "Lock Pro mode with a PIN", "Choose a PIN of 4 to 12 digits.");
            if (a == null) return;
            if (!ProPin.Valid(a)) { Dlg.Error("This PIN can't be used", "Use 4 to 12 digits."); return; }
            string b = PinDialog.Show(FindForm(), "Lock Pro mode with a PIN", "Type the PIN again.");
            if (b != a) { Dlg.Error("The PINs are not the same", "Nothing changed."); return; }
            try { ProPin.Set(a); } catch (Exception ex) { Dlg.Error("ArchTweaker could not save the PIN", ex.Message); return; }
            if (Mode.Pro) Mode.Set(false);
            MainForm.Instance.ModeSwitched();
        }
        public SettingsPage()
        {
            Heading("Appearance", true);
            var scheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Font = Theme.F };
            scheme.AccessibleName = "Colour scheme";
            scheme.Items.AddRange(Theme.SchemeNames);
            scheme.Items[Theme.CustomScheme] = "Custom…";   // 3.10.2: opens a colour picker
            scheme.SelectedIndex = Theme.Scheme;
            // SelectionChangeCommitted = the user picked an item (not a programmatic change, not teardown)
            scheme.SelectionChangeCommitted += delegate
            {
                int i = scheme.SelectedIndex;
                if (i < 0 || IsDisposed || MainForm.Instance == null) return;
                if (i == Theme.CustomScheme) { MainForm.Instance.PickCustomScheme(); return; }
                MainForm.Instance.SetScheme(i);   // the rebuild itself is deferred to a fresh message
            };
            var lang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Font = Theme.F, Tag = "notr" };
            lang.AccessibleName = "Language";
            lang.Items.AddRange(L.Names);
            lang.SelectedIndex = Math.Max(0, Array.IndexOf(L.Codes, L.Lang));
            lang.SelectionChangeCommitted += delegate
            {
                int i = lang.SelectedIndex;
                if (i < 0 || i >= L.Codes.Length || IsDisposed || MainForm.Instance == null) return;
                MainForm.Instance.SetLanguage(L.Codes[i]);
            };
            Group("Language", lang,
                new Label { Text = "Changes every page, dialog and tweak description. The log file stays in English so support can read it.", ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, UseMnemonic = false, Tag = "note" });
            Group("Colour scheme", scheme,
                new Label { Text = "Carbon dark is the default. OLED black uses true black, which saves power on OLED screens. Custom… uses any background colour you pick, and ArchTweaker makes the panels, text and accent tints readable on it. The accent colour is set separately.", ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, UseMnemonic = false, Tag = "note" });
            // 3.10: progress bar colour
            var prog = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Font = Theme.F };
            prog.AccessibleName = "Progress bar colour";
            prog.Items.AddRange(Theme.ProgressNames);
            prog.SelectedIndex = Theme.ProgressIndex;
            prog.SelectionChangeCommitted += delegate
            {
                int i = prog.SelectedIndex;
                if (i < 0 || IsDisposed || MainForm.Instance == null) return;
                if (i == Theme.ProgressNames.Length - 1) MainForm.Instance.PickCustomProgress(); else MainForm.Instance.SetProgress(i);
            };
            Group("Progress bars", prog,
                new Label { Text = "Colour of the progress bars in the sidebar and on Home. Green is the default. Accent follows the accent colour. Custom opens a colour picker. ArchTweaker adjusts a colour that would be hard to see.", ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, UseMnemonic = false, Tag = "note" });

            var trayOn = new SwitchBox { Text = "Show the ArchTweaker icon in the notification area", Checked = Tray.Enabled, AutoSize = true, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 0, 2) };
            var trayClose = new SwitchBox { Text = "Closing the window keeps ArchTweaker running in the notification area", Checked = Tray.CloseToTray, Enabled = Tray.Enabled, AutoSize = true, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 0, 2) };
            trayOn.CheckedChanged += delegate { Tray.Enabled = trayOn.Checked; trayClose.Enabled = trayOn.Checked; AppShell.Current.Notify("Takes effect the next time ArchTweaker starts.", Toast.Kind.Ok); };
            trayClose.CheckedChanged += delegate { Tray.CloseToTray = trayClose.Checked; };
            Group("Notification area icon", trayOn, trayClose,
                new Label { Text = "The icon's menu switches power plans, re-asserts your active profile and cleans temp files without opening the window.", ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, UseMnemonic = false, Tag = "note" });

            Heading("App mode", false);
            Group("App mode", Note(Mode.Pro ? "Pro mode is on: every tweak and tool is shown, including advanced, experimental and Danger zone tweaks." : "Lite mode is on: only safe tweaks and tools are shown. Pro mode shows everything, after a warning you must accept."),
                Theme.Btn(Mode.Pro ? "Switch to Lite mode" : "Switch to Pro mode…", delegate { MainForm.Instance.SwitchMode(); }),   // 3.22
                Theme.Btn(ProPin.IsSet ? "Remove the Pro PIN…" : "Lock Pro mode with a PIN…", delegate { PinSetup(); }));   // 3.23
            Heading("Help", false);
            Group("Help", Note("What's new lists the changes of the latest versions. The tour shows in five steps where things are."),
                Theme.Btn("What's new", delegate { WhatsNew.Show(FindForm()); }),
                Theme.Btn("Show the tour", delegate { Tour.Show(FindForm()); }));
            Group("Command line", Note("Run ArchTweaker from a script or a shortcut. Add /silent to run without a window."),
                new Label { Text = "ArchTweaker.exe /preset Gaming  |  /preset Auto  |  /apply profile.atp  |  /clean  |  /undo  |  /selftest [read]  |  /trace", Font = Theme.Mono, ForeColor = Theme.Text, AutoSize = true, UseMnemonic = false, Tag = "notr" });
        }
        public void OnShow() { }
        public void Filter(string q) { }
    }
}
