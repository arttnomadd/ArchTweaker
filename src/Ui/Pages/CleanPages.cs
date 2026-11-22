// ArchTweaker 1.9 UI: Deep clean (Winapp2) and Uninstaller pages.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ===================== DEEP CLEAN (Winapp2) =====================
    public class Winapp2Page : Panel, IPage
    {
        List<WinappEntry> entries = new List<WinappEntry>();
        readonly Dictionary<WinappEntry, CheckRow> checks = new Dictionary<WinappEntry, CheckRow>();
        readonly CheckList list;
        readonly Label status, total;
        bool loaded;

        public Winapp2Page()
        {
            BackColor = Theme.Bg;
            list = new CheckList();
            status = new Label { Dock = DockStyle.Top, Height = 24, ForeColor = Theme.Muted, Font = Theme.FS, AutoEllipsis = true, UseMnemonic = false, Text = "Deep clean uses the community Winapp2 ruleset to find leftover files from thousands of applications." };
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Lead("Download rules and scan", delegate { Load(true); }));
            bar.Controls.Add(Theme.Btn("Scan from saved copy", delegate { Load(false); }));
            bar.Controls.Add(Theme.Btn("Clean selected", delegate { Clean(); }));
            bar.Controls.Add(Theme.Btn("Select all", delegate { foreach (var r in checks.Values) r.Checked = true; }));
            bar.Controls.Add(Theme.Btn("Select none", delegate { foreach (var r in checks.Values) r.Checked = false; }));
            total = new Label { AutoSize = true, ForeColor = Theme.Text, Font = Theme.FB, Margin = new Padding(12, 8, 0, 0), UseMnemonic = false };
            bar.Controls.Add(total);
            Controls.Add(list);
            Controls.Add(status);
            Controls.Add(Theme.Note("Cleanup removes only file leftovers (caches, logs, crash dumps), never registry keys. It protects system folders. Review the list before you clean. Deleted files do not go to the Recycle Bin."));
            Controls.Add(bar);
        }

        public void OnShow() { }
        public void Filter(string q) { }

        void Load(bool net)
        {
            string st = null; List<WinappEntry> det = null;
            MainForm.Instance.Busy(net ? "Downloading Winapp2 rules…" : "Scanning from saved copy…",
                delegate { det = Winapp.Load(net, out st); if (det != null) foreach (var e in det) Winapp.Measure(e); },
                delegate { entries = det ?? new List<WinappEntry>(); status.Text = st ?? "Scan failed."; Render(); loaded = true; });
        }

        void Render()
        {
            list.SuspendLayout(); foreach (Control old in list.Controls.Cast<Control>().ToList()) old.Dispose(); list.Controls.Clear(); checks.Clear();
            string cat = null;
            foreach (var e in entries.Where(x => x.Size > 0))
            {
                if (e.Section != cat)
                {
                    cat = e.Section;
                    list.Controls.Add(new Label { Text = cat, Font = Theme.FB, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, list.Controls.Count == 0 ? 0 : 12, 0, 6), UseMnemonic = false });
                }
                var row = new CheckRow(e.Name, e.Warning, e.Warning != null, false) { Margin = new Padding(0, 0, 0, Theme.S(4)), SizeText = e.Size < 0 ? "—" : Fmt(e.Size) };
                row.CheckedChanged += delegate { UpdateTotal(); };
                checks[e] = row; list.Controls.Add(row);
            }
            list.ResumeLayout();
            UpdateTotal();
            if (checks.Count == 0) list.Controls.Add(new Label { Text = "No leftovers found. Nothing to clean.", ForeColor = Theme.Muted, Font = Theme.F, AutoSize = true, Margin = new Padding(0, Theme.S(8), 0, 0), UseMnemonic = false });
        }

        void UpdateTotal()
        {
            long found = entries.Sum(x => Math.Max(0, x.Size)), sel = checks.Where(kv => kv.Value.Checked).Sum(kv => Math.Max(0, kv.Key.Size));
            total.Text = "Found: " + Fmt(found) + (sel > 0 ? "   Selected: " + Fmt(sel) : "");
        }

        void Clean()
        {
            if (!loaded) return;
            var sel = checks.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToList();
            if (sel.Count == 0) { MainForm.Instance.Notify("Nothing selected. Tick at least one item to clean.", Toast.Kind.Warn); return; }
            string nsel = sel.Count + (sel.Count == 1 ? " item" : " items");
            if (!Dlg.Confirm("Clean " + nsel + "?", "Cleanup deletes files permanently. They don't go to the Recycle Bin.", "Delete files", "Cancel", true)) return;
            long freed = 0; int skipped = 0;
            MainForm.Instance.Busy("Cleaning…",
                delegate { foreach (var e in sel) { try { int sk; freed += Winapp.Clean(e, out sk); skipped += sk; } catch (Exception ex) { Log.W("Clean failed for {0}: {1}", e.Name, ex.Message); } Winapp.Measure(e); } },
                delegate
                {
                    Log.W("Deep clean freed {0}{1}", Fmt(freed), skipped > 0 ? " (" + skipped + " file(s) in use or skipped)" : "");
                    string msg = skipped == 0 ? L.F("Freed {0}.", Fmt(freed)) : skipped == 1 ? L.F("Freed {0}. 1 file is in use, so Cleanup skipped it.", Fmt(freed)) : L.F("Freed {0}. {1} files are in use, so Cleanup skipped them.", Fmt(freed), skipped);
                    MainForm.Instance.Notify(msg, skipped > 0 ? Toast.Kind.Warn : Toast.Kind.Ok);
                    Render();
                });
        }

        static string Fmt(long b) { return Units.Bytes(b); }
    }

    // ===================== UNINSTALLER =====================
    public class UninstallPage : Panel, IPage
    {
        ListView lv;
        List<Program32> progs = new List<Program32>();
        public UninstallPage()
        {
            BackColor = Theme.Bg;
            lv = new ListView { Dock = DockStyle.Fill };
            Theme.DarkList(lv);
            lv.Columns.Add("Program", 330); lv.Columns.Add("Version", 110); lv.Columns.Add("Publisher", 220); lv.Columns.Add("Size", 90);
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Lead("Uninstall and clean leftovers", delegate { Uninstall(true); }));
            bar.Controls.Add(Theme.Btn("Uninstall only", delegate { Uninstall(false); }));
            bar.Controls.Add(Theme.Btn("Scan leftovers only", delegate { Leftovers(SelectedProg()); }));
            bar.Controls.Add(Theme.Btn("Refresh", delegate { OnShow(); }));
            Controls.Add(lv);
            Controls.Add(Theme.Note("Classic desktop programs (Store apps are under \"Apps (UWP)\"). ArchTweaker moves leftover folders to the backup folder and does not delete them. It exports registry branches to .reg first, so you can reverse everything."));
            Controls.Add(bar);
        }
        Program32 SelectedProg() { return lv.SelectedItems.Count > 0 ? (Program32)lv.SelectedItems[0].Tag : null; }

        public void OnShow()
        {
            Skin.Loading(lv);
            var icons = new Dictionary<Program32, Bitmap>();
            MainForm.Instance.Load("Reading installed programs…", delegate
            {
                progs = Uninstaller.Load();
                foreach (var p in progs) icons[p] = AppIcons.FromIconSpec(p.Icon);   // 3.3: DisplayIcon, read off the UI thread
            }, delegate
            {
                lv.Items.Clear();
                foreach (var p in progs)
                {
                    var it = new ListViewItem(new[] { p.Name, p.Version, p.Publisher, p.EstBytes > 0 ? Units.MbGb(p.EstBytes) : "" }) { Tag = p };
                    Bitmap ic; icons.TryGetValue(p, out ic);
                    AppIcons.Attach(lv, it, "un|" + (p.Icon ?? "") + "|" + p.Name, ic);
                    lv.Items.Add(it);
                }
                Skin.Loaded(lv, "No desktop programs found in the uninstall list.");
            });
        }
        public void Filter(string q) { }

        void Uninstall(bool thenLeftovers)
        {
            var p = SelectedProg(); if (p == null) return;
            if (!p.HasUninstall) { Dlg.Info("No uninstaller for this entry", "\"" + p.Name + "\" doesn't list an uninstall command. Use Scan leftovers only to remove what it left behind."); return; }
            if (!Dlg.Confirm("Uninstall \"" + p.Name + "\"?", "Runs the program's own uninstaller, then scans for leftover files and registry entries.", "Uninstall", "Cancel")) return;
            // 3.2.1: per-user entries can be written by any program the user runs, and ArchTweaker runs the command as administrator
            if (p.Hive == Hive.HKCU && !Dlg.Confirm("Run this per-user uninstaller as administrator?", "This entry is in your user registry, which any program you run can change. Check that the command below really belongs to \"" + p.Name + "\" before running it.", "Run it", "Cancel", true, (p.QuietUninstall.Length > 0 ? p.QuietUninstall : p.UninstallString))) return;
            int rc = 0;
            MainForm.Instance.Busy("Uninstalling " + p.Name + "…", delegate { rc = Uninstaller.Run(p, false); }, delegate
            {
                OnShow();
                if (!thenLeftovers) return;
                // Only scan/clean leftovers once the program is actually gone — otherwise we'd offer to move a live install.
                if (Uninstaller.StillInstalled(p))
                    Dlg.Info("\"" + p.Name + "\" is still installed", "The uninstaller can still be running or need a restart, or you cancelled it" + (rc != 0 ? " (exit code " + rc + ")" : "") + ". ArchTweaker skipped the leftover cleanup so it touches nothing in use. When the program is gone, select Scan leftovers only.");
                else Leftovers(p);
            });
        }

        void Leftovers(Program32 p)
        {
            if (p == null) return;
            List<Leftover> found = null;
            MainForm.Instance.Load("Scanning for leftovers…", delegate { found = Uninstaller.ScanLeftovers(p); }, delegate
            {
                if (found == null || found.Count == 0) { Dlg.Info("No leftovers found", p.Name + " left no files or registry entries behind."); return; }
                using (var d = new LeftoverDialog(p.Name, found))
                    if (d.ShowDialog() == DialogResult.OK && d.Chosen.Count > 0)
                    {
                        long freed = 0; int failed = 0;
                        MainForm.Instance.Busy("Removing leftovers…", delegate { freed = Uninstaller.RemoveLeftovers(d.Chosen, out failed); },
                            delegate { Log.W("Leftover cleanup recovered {0} (folders moved to backup){1}.", Units.MbGb(freed), failed > 0 ? "; " + failed + " item(s) failed — see log" : ""); });
                    }
            });
        }

    }

}
