// ArchTweaker main window: Notice bars: update, drift guard, changes made outside ArchTweaker, restart and category summaries.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    public partial class MainForm
    {
        void ShowUpdateNotice(UpdateWatch.Result r)
        {
            updReset = r.Reset; updIsDrift = false;
            updText.Text = "Windows updated (build " + r.OldBuild + " → " + r.NewBuild + ") and reset " + r.Reset.Count + (r.Reset.Count == 1 ? " tweak" : " tweaks") + " you had applied.";
            updBar.Visible = true;
        }

        // 3.26: checks again while the app runs; a new set shows the bar and a toast.
        // 3.27.1: always the current window (a rebuild makes a new one); false when the notice could not be shown.
        void StartDriftWatch()
        {
            DriftWatch.Start(() => { var f = Instance; return f == null || f.busyCount > 0; }, d =>
            {
                var f = Instance;
                if (f == null || f.IsDisposed || !f.IsHandleCreated || f.busyCount > 0) return false;
                try
                {
                    f.BeginInvoke((Action)(() => { if (!f.IsDisposed) { f.ShowDriftNotice(d); f.toast.Show(L.F("{0} tweak(s) you applied went back to their default.", d.Count), Toast.Kind.Warn); } }));
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            });
        }

        void ShowDriftNotice(List<DriftItem> d)
        {
            updReset = d; updIsDrift = true;
            updText.Text = L.F("{0} tweak(s) you applied went back to their default. A driver update, a Windows update or another program can do this.", d.Count);
            updBar.Visible = true;
            Log.W("Drift guard: {0}", string.Join(", ", d.Select(x => x.Tweak.Title).ToArray()));
        }

        List<Tweak> OutsideHere()
        {
            if (current == null || !Catalog.Categories.Contains(current)) return new List<Tweak>();
            return Catalog.All.Where(t => t.Category == current && Mode.Allows(t) && TweakFilter.ChangedOutside(t)).ToList();
        }

        void UpdateOutsideBar()
        {
            if (outBar == null) return;
            var list = OutsideHere();
            if (list.Count == 0 || outDismissed.Contains(current)) { outBar.Visible = false; return; }
            outText.Text = L.T(list.Count == 1 ? "1 tweak that ArchTweaker applied on this page was turned back by Windows or another tool." : list.Count + " tweaks that ArchTweaker applied on this page were turned back by Windows or another tool.");
            outBar.Visible = true;
        }

        void ReapplyOutside()
        {
            var here = OutsideHere(); var ids = here.Select(t => t.Id).ToList(); if (ids.Count == 0) return;
            if (here.Any(t => t.Dangerous) && !DangerZone.Unlocked) DangerZone.Ensure(this);   // otherwise they are skipped
            List<DriftItem> list = null; int n = 0;
            outBar.Visible = false;
            Busy("Re-applying " + ids.Count + " tweak(s)…", delegate { list = Drift.Against(ids); n = Drift.Reassert(list); }, delegate
            {
                Log.Say("Re-applied {0} of {1} tweak(s) changed outside ArchTweaker.", n, list == null ? 0 : list.Count);
                RefreshAfterExternalChange();
            });
        }

        void ReapplyReset()
        {
            if (updReset == null) return;
            var list = updReset; int n = 0;
            if (list.Any(d => d.Tweak.Dangerous) && !DangerZone.Unlocked) DangerZone.Ensure(this);   // 3.5: otherwise they are skipped
            updBar.Visible = false;
            Busy("Re-applying " + list.Count + " tweak(s)…", delegate { n = Drift.Reassert(list); }, delegate
            {
                Log.Say(updIsDrift ? "Re-applied {0} of {1} tweak(s) that went back." : "Re-applied {0} of {1} tweak(s) reset by the Windows update.", n, list.Count);
                updReset = null; RefreshAfterExternalChange();
            });
        }


        // after work outside the apply loop (A/B test, re-assert): forget cached states and redraw
        public void RefreshAfterExternalChange() { StateCache.Clear(); RefreshAll(); UpdateCounts(); }

        public void ShowRestartSummaryPublic() { ShowRestartSummary(); }   // 3.44: the Home score's "+10 restart" link
        void ShowRestartSummary()
        {
            int n = rebootTitles.Count;
            if (n == 0) { restartBar.Visible = false; return; }
            string names = string.Join(", ", rebootTitles.Take(3).ToArray()) + (n > 3 ? " " + L.T("and " + (n - 3) + " more") : "");
            restartText.Text = L.T(n == 1 ? "1 change takes effect after a restart:" : n + " changes take effect after a restart:") + " " + names;
            FitRestartText();
            restartTip.SetToolTip(restartText, string.Join("\n", rebootTitles.ToArray()));
            restartBar.Visible = true;
        }


        // one line, cut with "…" when long (the tooltip has the whole list); width follows the bar
        void FitRestartText()
        {
            int need = TextRenderer.MeasureText(restartText.Text, restartText.Font).Width + Theme.S(8);
            int room = restartBar.ClientSize.Width - restartBar.Padding.Horizontal - Theme.S(300);   // leave room for the two buttons
            restartText.Width = Math.Max(Theme.S(200), Math.Min(need, room));
            restartText.Height = restartText.Font.Height + Theme.S(4);
        }

        void ShowCategorySummary(string cat)
        {
            detTweak = null;
            var all = Catalog.All.Where(t => t.Category == cat && Mode.Allows(t)).ToList();
            var avail = all.Where(t => t.Applicable).ToList();
            int applied = avail.Count(t => StateCache.Get(t) == true), known = avail.Count(t => StateCache.Get(t).HasValue);
            int pend = Pending.Keys.Count(t => t.Category == cat);
            var rec = avail.Where(t => Recommend.Has(t) && StateCache.Get(t) == false).ToList();
            var high = avail.Where(t => t.Impact >= 3 && !t.Dangerous && !t.Experimental && StateCache.Get(t) == false).ToList();   // 3.4: never suggested
            int risky = avail.Count(t => t.Risk != Risk.Safe);
            int restart = avail.Count(t => t.Reboot);
            detTitle.Text = L.T(cat);
            detBadge.Text = L.T("PAGE SUMMARY"); detBadge.ForeColor = Theme.Muted;
            // 3.33: the numbers as tiles; the text below keeps the lists
            var tiles = new List<StatTiles.Tile> { new StatTiles.Tile { Label = L.T("Applied"), Value = known < avail.Count ? "…" : applied + " / " + avail.Count, Tone = applied > 0 ? Theme.Ok : Theme.Strong } };
            if (pend > 0) tiles.Add(new StatTiles.Tile { Label = L.T("Waiting for Apply"), Value = pend.ToString(), Tone = Theme.Warn });
            if (restart > 0) tiles.Add(new StatTiles.Tile { Label = L.T("Need a restart"), Value = restart.ToString(), Tone = Theme.Strong });
            if (risky > 0) tiles.Add(new StatTiles.Tile { Label = L.T("Moderate or advanced"), Value = risky.ToString(), Tone = Theme.Warn });
            if (all.Count > avail.Count) tiles.Add(new StatTiles.Tile { Label = L.T("Not on this PC"), Value = (all.Count - avail.Count).ToString(), Tone = Theme.Muted });
            detStats.Set(tiles); detStats.Visible = true;
            var sb = new System.Text.StringBuilder();
            if (known < avail.Count) sb.Append(L.T("Reading the state of every tweak…")).Append("\r\n");
            sb.Append(L.T("RECOMMENDED FOR THIS PC, NOT APPLIED")).Append("\r\n");
            if (!Recommend.Ready) sb.Append(L.T("Checking this PC…")).Append("\r\n");
            else if (rec.Count == 0) sb.Append(L.T("Nothing left: every pick for this PC on this page is applied.")).Append("\r\n");
            else foreach (var t in rec) sb.Append("• ").Append(L.T(t.Title)).Append("\r\n");
            if (high.Count > 0)
            {
                sb.Append("\r\n").Append(L.T("HIGH GAIN, NOT APPLIED")).Append("\r\n");
                foreach (var t in high) sb.Append("• ").Append(L.T(t.Title)).Append(t.Risk != Risk.Safe ? "  (" + L.T(t.Risk == Risk.Moderate ? "Moderate risk" : "Advanced") + ")" : "").Append("\r\n");
            }
            sb.Append("\r\n").Append(L.T("Click a tweak to see exactly what it changes and how it is undone."));
            string txt = sb.ToString();
            if (detBox.Text != txt) { detBox.Text = txt; detBox.SelectionStart = 0; detBox.ScrollToCaret(); }   // a toggle doesn't scroll the summary back up
            var ids = rec.Where(t => { bool v; return !Pending.TryGetValue(t, out v); }).Select(t => t.Id).ToList();
            detLink.Visible = false; detForget.Visible = false;
            detAction.Visible = ids.Count > 0;
            detAction.Text = ids.Count == 0 ? "" : ids.Count == 1 ? L.T("Select the recommended tweak") : L.T("Select the recommended tweaks") + " (" + ids.Count + ")";
            detAction.Tag = ids.Count == 0 ? null : (Action)(() => SelectIds(ids, (added, already) => { if (added.Count > 0) Log.Say("{0} recommended tweak(s) selected on '{1}'. Review, then click Apply.", added.Count, L.T(cat)); }));
        }
    }
}
