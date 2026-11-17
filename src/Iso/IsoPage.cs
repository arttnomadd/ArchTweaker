// ArchTweaker: Bake into ISO page (3.32 split from IsoBake.cs).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    public class IsoPage : Panel, IPage
    {
        readonly TextBox isoBox = new TextBox { Width = 380, ReadOnly = true, Margin = new Padding(0, 6, 8, 0), AccessibleName = "Windows ISO" };
        readonly TextBox outBox = new TextBox { Width = 380, ReadOnly = true, Margin = new Padding(0, 6, 8, 0), AccessibleName = "Output folder" };
        readonly CheckedListBox eds = new CheckedListBox { Dock = DockStyle.Left, Width = 300, CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false, AccessibleName = "Editions" };
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, CheckBoxes = true };
        readonly Button bakeBtn;
        readonly Label status, oscState, skipped;
        List<KeyValuePair<Tweak, string>> left = new List<KeyValuePair<Tweak, string>>();
        bool running, loaded;

        public IsoPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("Tweak", 340); lv.Columns.Add("Category", 160); lv.Columns.Add("On this PC", 90);
            eds.BackColor = Theme.Panel; eds.ForeColor = Theme.Text; eds.Font = Theme.F;
            lv.ItemChecked += delegate { Ready(); }; eds.ItemCheck += delegate { if (IsHandleCreated) BeginInvoke((Action)Ready); };

            var bar = Theme.Bar();
            bar.Controls.Add(Lbl("Windows ISO:")); bar.Controls.Add(isoBox); bar.Controls.Add(Theme.Btn("Choose ISO…", delegate { ChooseIso(); }));
            var bar2 = Theme.Bar();
            bar2.Controls.Add(Lbl("Output folder:")); bar2.Controls.Add(outBox); bar2.Controls.Add(Theme.Btn("Choose folder…", delegate { ChooseOut(); }));
            var bar3 = Theme.Bar();
            bakeBtn = Theme.Lead("Bake into ISO", delegate { Bake(); }); bakeBtn.Enabled = false;
            bar3.Controls.Add(bakeBtn);
            bar3.Controls.Add(Theme.Btn("Tick applied", delegate { Tick(true); }));
            bar3.Controls.Add(Theme.Btn("Untick all", delegate { Tick(false); }));
            bar3.Controls.Add(Theme.Btn("Choose oscdimg.exe…", delegate { ChooseOsc(); }));
            status = Theme.StatusText();
            bar3.Controls.Add(status);

            oscState = new Label { Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Muted, Font = Theme.FS, UseMnemonic = false, Tag = "notr", Padding = new Padding(2, 4, 2, 0) };
            skipped = new Label { Dock = DockStyle.Bottom, Height = 24, ForeColor = Theme.Muted, Font = Theme.FS, UseMnemonic = false, Tag = "notr", Padding = new Padding(2, 4, 2, 0), Cursor = Cursors.Hand };
            skipped.Click += delegate { ShowLeftOut(); };

            var edHead = new Label { Text = "Editions", Font = Theme.FB, ForeColor = Theme.Text, Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false };
            var edPanel = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(0, 0, 12, 0), BackColor = Theme.Bg };
            edPanel.Controls.Add(eds); eds.Dock = DockStyle.Fill; edPanel.Controls.Add(edHead);
            var twHead = new Label { Text = "Tweaks to bake in", Font = Theme.FB, ForeColor = Theme.Text, Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false };
            var twPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            twPanel.Controls.Add(lv); twPanel.Controls.Add(twHead); twPanel.Controls.Add(skipped);
            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            body.Controls.Add(twPanel); body.Controls.Add(edPanel);

            Controls.Add(body); Controls.Add(oscState);
            Controls.Add(Theme.Note("Writes tweaks into a copy of a Windows setup ISO, so a fresh install starts tuned. Only registry values and service start types can go into an image. The original ISO and this PC stay as they are."));
            Controls.Add(bar3); Controls.Add(bar2); Controls.Add(bar);
            Skin.SetEmpty(lv, "Loading the tweaks…");
        }
        static Label Lbl(string t) { return new Label { Text = t, ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, Width = 100, Margin = new Padding(0, 9, 4, 0) }; }

        public void OnShow() { ShowOsc(); if (!loaded) LoadTweaks(); }
        public void Filter(string q) { }

        void ShowOsc()
        {
            string o = IsoBake.FindOscdimg();
            oscState.Text = o != null ? L.T("ISO builder:") + " " + o : L.T("oscdimg.exe not found: ArchTweaker writes a folder for Rufus or a USB stick instead of an ISO. Install the Windows ADK Deployment Tools, or click Choose oscdimg.exe….");
            oscState.ForeColor = o != null ? Theme.Muted : Theme.Warn;
        }

        void LoadTweaks()
        {
            loaded = true;
            var rows = new List<KeyValuePair<Tweak, bool>>(); var no = new List<KeyValuePair<Tweak, string>>();
            MainForm.Instance.Busy("Reading tweak states…", delegate
            {
                foreach (var t in Catalog.All)
                {
                    string why = IsoBake.Unbakeable(t);
                    bool on = false; try { on = t.IsApplied(); } catch { }
                    if (why == null) rows.Add(new KeyValuePair<Tweak, bool>(t, on));
                    else if (on) no.Add(new KeyValuePair<Tweak, string>(t, why));
                }
            }, delegate
            {
                left = no;
                lv.BeginUpdate(); lv.Items.Clear();
                foreach (var kv in rows)
                    lv.Items.Add(new ListViewItem(new[] { L.T(kv.Key.Title), L.T(kv.Key.Category), L.T(kv.Value ? "applied" : "—") }) { Tag = kv.Key, Checked = kv.Value && !kv.Key.Dangerous });
                lv.EndUpdate();
                Skin.Loaded(lv, "No tweak can be written into an image.");
                skipped.Text = no.Count == 0 ? "" : L.F("{0} applied tweak(s) can't go into an image (commands, tasks, power plans, devices). Click to see which.", no.Count);
                Ready();
            });
        }

        void ShowLeftOut()
        {
            if (left.Count == 0) return;
            Dlg.Info("Applied tweaks that stay out of the image", "An offline image can't run commands or change power plans, scheduled tasks or devices. Apply these after the install.",
                string.Join("\r\n", left.Select(kv => L.T(kv.Key.Title) + "  —  " + L.T(kv.Value)).ToArray()));
        }

        void Tick(bool applied)
        {
            lv.BeginUpdate();
            foreach (ListViewItem i in lv.Items) { var t = i.Tag as Tweak; i.Checked = applied && t != null && i.SubItems[2].Text != "—" && !t.Dangerous; }
            lv.EndUpdate(); Ready();
        }

        void Ready()
        {
            int n = lv.Items.Cast<ListViewItem>().Count(i => i.Checked);
            bakeBtn.Enabled = !running && n > 0 && eds.CheckedItems.Count > 0 && isoBox.Text.Length > 0 && outBox.Text.Length > 0;
            if (!running) status.Text = n == 0 ? "" : L.F("{0} tweak(s) · {1} edition(s)", n, eds.CheckedItems.Count);
        }

        void ChooseIso()
        {
            using (var d = new OpenFileDialog { Filter = "Windows ISO|*.iso", Title = L.T("Choose a Windows ISO") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string iso = d.FileName;
                if (!IsoBake.SafePath(iso)) { Dlg.Error("This path can't be used", "Rename the file or folder so the path has no quote marks."); return; }
                List<IsoEdition> list = null; string err = null;
                eds.Items.Clear(); isoBox.Text = ""; Ready();
                MainForm.Instance.Busy("Reading the editions in the ISO…", delegate { list = IsoBake.Inspect(iso, out err); }, delegate
                {
                    if (list == null || list.Count == 0) { Dlg.Error("Not a Windows setup ISO", err ?? "ArchTweaker found no editions in this ISO."); return; }
                    isoBox.Text = iso;
                    if (outBox.Text.Length == 0) outBox.Text = Path.GetDirectoryName(iso);
                    // the plain Pro edition by default ("Windows 11 Pro", not Pro N or Pro Education); else the first one
                    bool pro = list.Any(e => Regex.IsMatch(e.Name, @"\bPro$", RegexOptions.IgnoreCase));
                    for (int i = 0; i < list.Count; i++)
                        eds.Items.Add(list[i], pro ? Regex.IsMatch(list[i].Name, @"\bPro$", RegexOptions.IgnoreCase) : i == 0);
                    Ready();
                });
            }
        }

        void ChooseOut()
        {
            using (var d = new FolderBrowserDialog { Description = L.T("Folder for the new ISO (an NTFS drive with room for about three times the ISO size)") })
            {
                if (outBox.Text.Length > 0) d.SelectedPath = outBox.Text;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!IsoBake.SafePath(d.SelectedPath)) { Dlg.Error("This path can't be used", "Choose a folder whose path has no quote marks."); return; }
                outBox.Text = d.SelectedPath; Ready();
            }
        }

        void ChooseOsc()
        {
            using (var d = new OpenFileDialog { Filter = "oscdimg|oscdimg.exe|Programs|*.exe", Title = L.T("Choose oscdimg.exe") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string err = IsoBake.ImportOscdimg(d.FileName);
                if (err != null) Dlg.Error("Could not use this file", err); else Log.Say("oscdimg is ready: {0}", IsoBake.BinPath);
                ShowOsc();
            }
        }

        void Bake()
        {
            if (running) return;
            var job = new IsoJob { Iso = isoBox.Text, OutDir = outBox.Text };
            foreach (var o in eds.CheckedItems) job.Editions.Add((IsoEdition)o);
            job.Tweaks = lv.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (Tweak)i.Tag).OrderBy(t => Catalog.All.IndexOf(t)).ToList();
            bool osc = IsoBake.FindOscdimg() != null;
            if (!Dlg.Confirm("Bake the tweaks into the ISO?",
                L.F("ArchTweaker copies the ISO to {0}, writes {1} tweak(s) into {2} edition(s) and {3}. It takes 10 to 40 minutes. The original ISO and this PC stay as they are.",
                    job.OutDir, job.Tweaks.Count, job.Editions.Count, osc ? L.T("builds a new ISO") : L.T("leaves the files in a folder for Rufus (oscdimg.exe not found)")),
                "Bake into ISO", details: string.Join("\r\n", job.Editions.Select(e => e.Name).Concat(new[] { "" }).Concat(job.Tweaks.Select(t => "• " + L.T(t.Title))).ToArray()))) return;
            running = true; bakeBtn.Enabled = false;
            Action<string> say = Theme.SayTo(this, status);
            IsoBakeResult r = null;
            MainForm.Instance.Busy("Baking the tweaks into the ISO…", delegate { r = IsoBake.Run(job, say); }, delegate
            {
                running = false; status.Text = ""; Ready();
                if (r == null) { Dlg.Error("Bake into ISO stopped", "Details are in the log: " + Paths.LogFile); return; }
                string notes = string.Join("\r\n", r.Notes.ToArray());
                if (r.Error != null && r.Folder != null && Directory.Exists(r.Folder)) r.Notes.Add(L.F("The copied files are still in {0}. Delete that folder to free the space.", r.Folder));
                notes = string.Join("\r\n", r.Notes.ToArray());
                if (r.Error != null) { Log.Say("Bake into ISO failed: {0}", r.Error); Dlg.Error("Bake into ISO failed", r.Error, notes.Length > 0 ? notes : null); return; }
                string where = r.IsoPath ?? r.Folder;
                Log.Say("Bake into ISO done: {0} value(s) written. {1}", r.Values, where);
                Dlg.Info(r.IsoPath != null ? "New ISO ready" : "Patched files ready", L.F("{0} value(s) written. Result: {1}", r.Values, where), notes.Length > 0 ? notes : null);
                Shell.OpenVisible("explorer.exe", "/select,\"" + where + "\"");
            });
        }
    }
}
