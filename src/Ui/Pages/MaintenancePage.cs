// ArchTweaker: Maintenance page: scheduled update checks, keep-applied and health reports.
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    public class MaintenancePage : FlowLayoutPanel, IPage
    {
        Label active, driftLbl, verLbl;
        bool _suppress;   // guards programmatic checkbox reverts from re-triggering the handler
        public MaintenancePage()
        {
            FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true; BackColor = Theme.Bg;

            verLbl = new Label { Text = "Installed: ArchTweaker " + Program.Version, ForeColor = Theme.Text, Font = Theme.FB, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
            Group("Updates", verLbl,
                Theme.Btn("Check for updates", delegate { CheckUpdate(); }),
                Note("Checks a version manifest online, verifies the download's SHA-256, then swaps the exe and relaunches. Set a custom manifest URL under HKCU\\Software\\ArchTweaker\\Update if you self-host."));

            active = new Label { Text = "", ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
            driftLbl = new Label { Text = "", ForeColor = Theme.Warn, Font = Theme.FS, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            Group("Keep tweaks applied", active, driftLbl,
                Theme.Btn("Set active profile…", delegate { PickActive(); }),
                Theme.Btn("Save current state as active profile…", delegate { SaveActive(); }),
                Theme.Btn("Check drift now", delegate { CheckDrift(); }),
                Theme.Btn("Re-assert profile now", delegate { Reassert(); }),
                Note("Windows feature updates can reset many tweaks without a message. \"Re-assert\" applies again each tweak from your active profile that is not in effect now."));

            var taskRows = Maint.Tasks.Select(t =>
            {
                var cb = new SwitchBox { Text = t.Name + " — " + t.Desc, Checked = Safe(() => Maint.Exists(t)), ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, AutoSize = true, MaximumSize = new Size(820, 0), Margin = new Padding(0, 3, 0, 3) };
                var tt = t;
                cb.CheckedChanged += delegate
                {
                    if (_suppress) return;
                    bool want = cb.Checked; bool ok = true;
                    AppShell.Current.Busy(want ? "Scheduling…" : "Removing task…",
                        delegate { if (want) ok = Maint.Install(tt); else Maint.Remove(tt); },
                        delegate { if (want && !ok) { _suppress = true; cb.Checked = false; _suppress = false; Dlg.Error("Couldn't create the scheduled task", "Scheduling needs administrator rights. Details are in the log: " + Paths.LogFile); } });
                };
                return (Control)cb;
            }).ToArray();
            Group("Scheduled maintenance", taskRows);

            Group("Support and tweak updates",
                Theme.Btn("Export diagnostics", delegate
                {
                    string path = null, err = null;
                    AppShell.Current.Busy("Collecting diagnostics…", delegate { try { path = Diagnostics.Export(); } catch (Exception ex) { err = ex.Message; } },
                        delegate { if (path != null) Shell.OpenVisible("explorer.exe", "/select,\"" + path + "\""); else Dlg.Error("Couldn't export diagnostics", "Details are in the log: " + Paths.LogFile, err); });
                }),
                Theme.Btn("Download tweak updates", delegate
                {
                    string err = null;
                    AppShell.Current.Busy("Downloading the tweak overlay…", delegate { err = Overlay.Fetch(); },
                        delegate { if (err == null) Dlg.Info("Tweak updates downloaded", "Restart ArchTweaker to load them."); else Dlg.Error("Couldn't download tweak updates", "Your tweak list is unchanged.", err); });
                }),
                Note("Export diagnostics saves one zip to the desktop: the log, the undo journal, a health report and system details. ArchTweaker uploads nothing. " +
                    "Tweak updates are new or corrected registry tweaks from the update manifest. ArchTweaker checks them with SHA-256, and they load at the next start." +
                    (Overlay.Loaded > 0 ? "  " + Overlay.Loaded + " downloaded tweak(s) are loaded now." : "")));

            Group("Reports",
                Theme.Btn("Generate health report", delegate { _report = null; AppShell.Current.Busy("Building report…", delegate { _report = HealthReport.Generate(true); }, delegate { if (_report != null) Shell.OpenVisible("explorer.exe", "\"" + _report + "\""); else Dlg.Error("Couldn't build the health report", "Details are in the log: " + Paths.LogFile); }); }),
                Note("A self-contained HTML page: specs, every applied tweak, profile drift, and cleanup potential. Good for sharing or support."));
        }
        string _report;

        public void OnShow() { RefreshActive(); }
        public void Filter(string q) { }

        void RefreshActive()
        {
            string ap = HealthReport.ActiveProfile();
            active.Text = string.IsNullOrEmpty(ap) ? "No active profile set." : "Active profile: " + ap;
            driftLbl.Text = "";
        }

        void PickActive()
        {
            using (var d = new OpenFileDialog { Filter = "ArchTweaker profile|*.atp|All files|*.*" })
                if (d.ShowDialog() == DialogResult.OK) { HealthReport.SetActiveProfile(d.FileName); RefreshActive(); }
        }
        void SaveActive()
        {
            using (var d = new SaveFileDialog { Filter = "ArchTweaker profile|*.atp", FileName = "active.atp" })
                if (d.ShowDialog() == DialogResult.OK)
                {
                    var ids = Catalog.All.Where(t => t.Applicable && t.IsApplied()).Select(t => t.Id).ToList();
                    File.WriteAllLines(d.FileName, new[] { "# ArchTweaker profile " + DateTime.Now.ToString("yyyy-MM-dd"), "#hw: " + HwFingerprint.Current() }.Concat(ids));
                    HealthReport.SetActiveProfile(d.FileName);
                    Log.W("Saved {0} applied tweaks as active profile {1}", ids.Count, d.FileName);
                    RefreshActive();
                }
        }
        void CheckDrift()
        {
            string ap = HealthReport.ActiveProfile();
            if (string.IsNullOrEmpty(ap) || !File.Exists(ap)) { driftLbl.Text = "Set an active profile first."; return; }
            AppShell.Current.LoadData("Checking drift…", delegate { _drift = Drift.Against(Profile.Read(ap).ToList()).Count; },
                delegate { driftLbl.Text = _drift == 0 ? "In sync — all profile tweaks applied." : _drift + " tweak(s) from the profile are NOT applied."; driftLbl.ForeColor = _drift == 0 ? Theme.Ok : Theme.Warn; });
        }
        int _drift;
        void Reassert()
        {
            string ap = HealthReport.ActiveProfile();
            if (string.IsNullOrEmpty(ap) || !File.Exists(ap)) { driftLbl.Text = "Set an active profile first."; return; }
            int n = 0;
            AppShell.Current.Busy("Re-asserting profile…", delegate { n = Drift.Reassert(Drift.Against(Profile.Read(ap).ToList())); },
                delegate { Log.W("Re-asserted {0} tweak(s).", n); CheckDrift(); });
        }

        void CheckUpdate()
        {
            UpdateInfo u = null; string err = null;
            AppShell.Current.LoadData("Checking for updates…", delegate { u = Updater.Check(out err); }, delegate
            {
                if (u == null) { Dlg.Error("Couldn't check for updates", "Check your internet connection and try again.", err); return; }
                if (!u.Newer) { Dlg.Info("ArchTweaker is up to date", "You have the latest version, " + Program.Version + "."); return; }
                if (!Dlg.Confirm("Update to " + u.Version + "?", "ArchTweaker downloads the update, checks its SHA-256 hash and relaunches. Apply or discard pending changes first.", "Update now", "Not now", false, string.IsNullOrEmpty(u.Notes) ? null : u.Notes)) return;
                AppShell.Current.Busy("Downloading " + u.Version + "…", delegate { _upErr = Updater.Download(u); }, delegate
                {
                    if (_upErr != null) Dlg.Error("Update failed", "Your current version is unchanged. Try again later.", _upErr);
                    else Application.Exit();
                });
            });
        }
        string _upErr;

        static bool Safe(Func<bool> f) { try { return f(); } catch { return false; } }
        // 3.10.5: each group is a settings card (the Tools & backup page moves them in)
        void Group(string title, params Control[] items)
        {
            var card = new SettingsCard(title);
            foreach (var c in items) card.Add(c);
            Controls.Add(card);
        }
        static Label Note(string t) { return new Label { Text = t, ForeColor = Theme.Muted, Font = Theme.FS, AutoSize = true, UseMnemonic = false, Tag = "note" }; }
    }
}
