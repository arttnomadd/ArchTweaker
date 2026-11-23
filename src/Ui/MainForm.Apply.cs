// ArchTweaker main window: Pending changes, presets, apply, undo, restart, the details pane and the busy overlay.
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
        public void UpdateCounts()
        {
            int seq = Interlocked.Increment(ref countSeq);
            ThreadPool.QueueUserWorkItem(delegate
            {
                var counts = new Dictionary<string, string>();
                try
                {
                    foreach (var g in Catalog.All.Where(t => t.Applicable && Mode.Allows(t)).GroupBy(t => t.Category))
                        counts[g.Key] = g.Count(t => StateCache.Applied(t)) + "/" + g.Count();
                }
                catch { return; }
                try
                {
                    if (IsDisposed || seq != countSeq) return;   // window rebuilt, or a newer count is on its way
                    BeginInvoke((Action)(() =>
                    {
                        if (seq != countSeq) return;
                        lastCounts = counts; UpdateSubtitle();
                        if (filterChips.Visible) filterChips.Recount(Catalog.Categories.Contains(current) ? current : null);
                        UpdateOutsideBar();
                        foreach (var n in navItems) { string c; if (counts.TryGetValue(n.Page, out c)) n.SetCount(c); }
                        Control h; if (pages.TryGetValue("Home", out h)) ((HomePage)h).Refresh2();
                    }));
                }
                catch { }
            });
        }


        public void UpdatePending()
        {
            int n = Pending.Count;
            // 3.33: a greyed "Apply changes" looked broken; it now says why it can't be clicked
            applyBtn.Text = n > 0 ? "Apply " + n + " change" + (n == 1 ? "" : "s") : "Nothing to apply";
            applyBtn.Enabled = n > 0; discardBtn.Enabled = n > 0;
            undoLastBtn.Enabled = History.HasLast && busyCount == 0;
            // 3.2: buttons that can't do anything right now are hidden, not greyed out (Apply always stays: it's the main action)
            discardBtn.Visible = n > 0;
            undoLastBtn.Visible = History.HasLast;
            // 3.12: pending count per page in the sidebar, and per section while the section is collapsed
            var byCat = Pending.Keys.GroupBy(t => t.Category).ToDictionary(g => g.Key, g => g.Count());
            foreach (var ni in navItems) { int c; ni.SetPend(byCat.TryGetValue(ni.Page, out c) ? c : 0); }
            foreach (var kv in headers) kv.Value.SetPend(sections[kv.Key].Sum(ni => ni.Pend));
            if (filterChips != null && filterChips.Visible) filterChips.Recount(Catalog.Categories.Contains(current) ? current : null);
            Control hp; if (pages.TryGetValue("Home", out hp) && hp.Visible) ((HomePage)hp).RefreshTiles();
            if (detTweak == null && current != null && Catalog.Categories.Contains(current) && details.Visible) ShowCategorySummary(current);
        }


        public void SetPending(Tweak t, bool want)
        {
            if (StateCache.Applied(t) == want) Pending.Remove(t); else Pending[t] = want;
            if (want && Exclude(t) > 0) { RefreshAll(); return; }   // 3.6.1: the other half of a contradicting pair is switched off
            UpdatePending();
        }


        // 3.6.1: when t is switched on, every tweak it excludes is switched off (pending), with a log line saying why
        int Exclude(Tweak t)
        {
            int n = 0;
            foreach (var x in Catalog.All.Where(a => t.Excludes.Contains(a.Id)))
            {
                bool p; bool pendingOn = Pending.TryGetValue(x, out p) && p;
                bool applied = StateCache.Applied(x);
                if (!pendingOn && !applied) continue;
                if (x.Dangerous && !DangerZone.Unlocked && applied)
                { Log.Say("'{0}' can't be on together with '{1}', which is on in the Danger zone. Unlock the Danger zone and switch it off first.", L.T(t.Title), L.T(x.Title)); continue; }
                if (applied) Pending[x] = false; else Pending.Remove(x);
                Log.Say("'{0}' is off: it can't be on together with '{1}'.", L.T(x.Title), L.T(t.Title));
                n++;
            }
            return n;
        }


        public void RefreshTweaks() { RefreshAll(); }

        public void RefreshAll()
        {
            foreach (var p in pages.Values.OfType<TweakPage>()) p.Reload();
            Control sp; if (pages.TryGetValue(SEARCH, out sp)) ((SearchPage)sp).Reload();
            UpdatePending(); UpdateCounts();
        }


        public void SelectAutoPreset() { SelectPreset(Catalog.PRESET_AUTO); }


        // 3.23 Lite: the recommended safe tweaks for this PC, selected and applied in one go.
        public void OptimizeSafely()
        {
            if (busyCount > 0) return;
            List<string> ids = null; List<Tweak> noGain = null;
            Busy("Checking this PC…", () => { ids = LiteActions.SafePicks(); noGain = LiteActions.NoGainCandidates(); }, () =>
            {
                SelectIds(ids, (added, already) =>
                {
                    // 3.39: applied tweaks that measured no gain or a loss on this PC are undone in the same go
                    StateCache.ReadAsync(noGain, this, () =>
                    {
                        var undo = noGain.Where(t => StateCache.Get(t) == true).ToList();
                        foreach (var t in undo) { Pending[t] = false; Log.W("  - {0}   ({1})", L.T(t.Title), L.T("measured no gain here")); }
                        if (undo.Count > 0) RefreshAll();
                        int n = added.Count + already.Count;
                        if (n == 0 && undo.Count == 0) { Log.Say("Optimize safely: everything recommended for this PC is already applied."); return; }
                        foreach (var t in added.Concat(already)) Log.W("  + {0}   ({1})", L.T(t.Title), L.T(t.Category));
                        string body = n > 0 ? L.F("ArchTweaker applies {0} safe tweak(s) picked for this PC. The log lists them. A restore point comes first.", n) : "";
                        if (undo.Count > 0) body = (body.Length > 0 ? body + "\n\n" : "") + L.F("It also undoes {0} applied tweak(s) that measured no gain or a loss on this PC.", undo.Count);
                        if (!Dlg.Confirm("Optimize safely?", body + "\n\n" + L.T("Tip: run a Benchmark before and after to see the difference."), "Apply now", "Review first"))
                        { if (!logOpen) ToggleLog(); return; }
                        ApplyPending();
                    });
                });
            });
        }


        void SelectPreset(string name)
        {
            if (name == Catalog.PRESET_AUTO)
            {
                List<string> ids = null;
                Busy("Checking this PC…", () => { ids = Advisor.RecommendedIds().ToList(); }, () =>
                {
                    SelectIds(ids, (added, already) =>
                    {
                        // the log lists exactly what will change, so nothing is selected out of sight
                        // shown in the interface language: these lines are for the person deciding what to apply
                        foreach (var t in added) Log.W("  + {0}   ({1}, {2})", L.T(t.Title), L.T(t.Category), L.T(ImpactMeter.Word(t.Impact)));
                        foreach (var t in already) Log.W("  = {0}   ({1})", L.T(t.Title), L.T("already selected"));
                        if (added.Count == 0 && already.Count == 0)
                            Log.Say("Preset '{0}': nothing to select — everything recommended for this PC is already applied.", name);
                        else if (added.Count == 0)
                            Log.Say("Preset '{0}': no new picks — the {1} recommended tweak(s) above are already selected. Review, then click Apply.", name, already.Count);
                        else
                            Log.Say("Preset '{0}': {1} tweak(s) picked for this hardware, listed above (see PC advisor for why). Review, then click Apply.", name, added.Count + already.Count);
                        if (added.Count + already.Count > 0 && !logOpen) ToggleLog();   // show the list
                    });
                });
                return;
            }
            var picks = Catalog.Uncovered(Catalog.All.Where(x => x.Applicable && Mode.Allows(x) && x.Presets.Contains(name)).ToList());   // 3.7: no tweak another pick already covers
            status.Text = "Checking current states…";
            StateCache.ReadAsync(picks, this, () =>   // states are read off the UI thread, then the preset is selected
            {
                int n = 0;
                foreach (var t in picks) if (StateCache.Get(t) == false) { Pending[t] = true; n++; }
                status.Text = "";
                RefreshAll();
                Log.Say("Preset '{0}': {1} tweak(s) selected. Review the checkmarks, then click Apply.", name, n);
            });
        }


        public void SelectIds(IEnumerable<string> ids) { SelectIds(ids, null); }


        // done(added, alreadySelected) runs once the states are known: tweaks applied already are skipped.
        public void SelectIds(IEnumerable<string> ids, Action<List<Tweak>, List<Tweak>> done)
        {
            var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            var picks = Catalog.Uncovered(Catalog.All.Where(x => x.Applicable && Mode.Allows(x) && set.Contains(x.Id)).ToList());
            // 3.4: a profile or list never switches Danger zone tweaks while the page is locked
            int locked = DangerZone.Unlocked ? 0 : picks.RemoveAll(x => x.Dangerous);
            if (locked > 0) Log.Say("ArchTweaker skipped {0} Danger zone tweak(s) in the list. Unlock the Danger zone page first, then select them there.", locked);
            StateCache.ReadAsync(picks, this, () =>
            {
                var added = new List<Tweak>(); var already = new List<Tweak>();
                foreach (var t in picks)
                {
                    if (StateCache.Get(t) != false) continue;          // already applied
                    bool v; if (Pending.TryGetValue(t, out v) && v) { already.Add(t); continue; }
                    Pending[t] = true; added.Add(t); Exclude(t);
                }
                RefreshAll();
                if (done != null) done(added, already);
            });
        }


        void ApplyPending()
        {
            // reverts first (reverse catalog order), then applies in catalog order (e.g. power plan before its settings)
            var work = Pending.Where(kv => !kv.Value).OrderByDescending(kv => Catalog.All.IndexOf(kv.Key))
                .Concat(Pending.Where(kv => kv.Value).OrderBy(kv => Catalog.All.IndexOf(kv.Key))).ToList();
            if (work.Count == 0) return;
            // 3.4: a Danger zone apply always gets a restore point, whatever the checkbox says
            bool doRp = (rpCheck.Checked || work.Any(kv => kv.Value && kv.Key.Dangerous)) && !restorePointDone;
            if (!ReviewDialog.Confirm(this, work, doRp)) return;
            // 3.29: experimental tweaks get one more question: their effect is not proven
            var exp = work.Where(kv => kv.Value && kv.Key.Experimental).Select(kv => "• " + L.T(kv.Key.Title)).ToList();
            if (exp.Count > 0 && !Dlg.Confirm(L.F("Apply {0} experimental tweak(s)?", exp.Count),
                    L.T("Nobody has proven that these tweaks help. Measure each one on the A/B test page before you keep it.") + "\r\n\r\n" + string.Join("\r\n", exp.Take(12).ToArray()) + (exp.Count > 12 ? "\r\n…" : ""),
                    L.T("Apply them"), L.T("Cancel"))) return;
            bool fixRp = false;   // 3.24: a restore point that can't work is worse than none: say so first
            if (doRp)
            {
                string why = RestoreCheck.Problem();
                if (why != null)
                {
                    if (Dlg.Confirm("Restore points don't work on this PC", why + "\n\n" + L.T("ArchTweaker can set the Volume Shadow Copy services to Manual and turn on System Protection for the system drive (up to 5 % of it). Then it makes the restore point."), "Turn it on", "Apply without"))
                        fixRp = true;
                    else { doRp = false; Log.Say("Applying without a restore point: {0}", why); }
                }
            }
            int rebootN = 0; bool relog = false; int doneN = 0; List<KeyValuePair<Tweak, bool>> doneList = new List<KeyValuePair<Tweak, bool>>();
            Busy("Applying " + work.Count + " change(s)…", () =>
            {
                if (fixRp) { Log.Say("Turning on System Protection…"); string why; if (!RestoreCheck.Fix(out why)) Log.Say("System Protection is still off: {0}", why ?? L.T("check the log")); }
                if (doRp) { Log.Say("Creating restore point (can take a minute)…"); Tools.RestorePoint(); restorePointDone = true; }
                try { RegSnapshot.Capture(work, "apply"); } catch (Exception ex) { Log.W("Snapshot: {0}", ex.Message); }
                var done = new List<KeyValuePair<Tweak, bool>>(); doneList = done;
                PowerCache.Batch(() =>   // power-plan changes are activated once, after the whole batch
                {
                    foreach (var kv in work)
                    {
                        try
                        {
                            if (kv.Value) { if (RescueBackfill.Covers(kv.Key) && !RecoveryKit.Capture(kv.Key) && kv.Key.Dangerous) throw new InvalidOperationException(L.T("the recovery kit could not save the original values, so it was not applied")); kv.Key.Apply(); } else { kv.Key.Revert(); if (RescueBackfill.Covers(kv.Key)) RecoveryKit.Release(kv.Key.Id); }   // 3.8: recovery kit
                            Log.W("{0} {1}", kv.Value ? "Applied:" : "Reverted:", kv.Key.Title);
                            done.Add(kv); AppliedSet.Mark(kv.Key, kv.Value);
                            if (kv.Key.Reboot) rebootN++; relog |= kv.Key.Relogon;
                        }
                        catch (Exception ex) { Log.Say("FAILED {0}: {1}", kv.Key.Title, ex.Message); }
                    }
                });
                History.Push(done); doneN = done.Count;
                Snapshot.Capture();                    // 3.0: "What changed" compares against this
                if (rebootN > 0) BootGuard.Arm();      // 3.0: undo automatically if Windows then fails to start twice
                NetDpcCheck.Arm(done.Where(x => x.Value).Select(x => x.Key));   // 3.9: scan network-driver DPCs after the restart
                Native.BroadcastSettingChange();
                PowerCache.Invalidate(); Probe.Invalidate(); Probe.Warm();
                try { int bad = Verify.Report(Verify.Check(done.Where(x => !x.Key.Reboot).ToList())); if (bad > 0) Log.W("Note: items needing a restart are not counted as failures."); } catch { }
                if (rebootN > 0) Log.Say("{0} change(s) need a restart to take effect.", rebootN);
                else if (relog) Log.Say("Some changes take effect after sign-out or Explorer restart (Tools → Restart Explorer).");
            }, () =>
            {
                int failed = work.Count - doneN;
                string msg = (doneN == 1 ? "1 change applied" : doneN + " changes applied") +
                    (failed > 0 ? " · " + failed + " failed (see log)" : "") + (rebootN > 0 ? " · " + rebootN + " need" + (rebootN == 1 ? "s" : "") + " a restart" : relog ? " · sign out to finish" : "");
                var kind = failed > 0 ? Toast.Kind.Bad : rebootN > 0 || relog ? Toast.Kind.Warn : Toast.Kind.Ok;
                // 3.2: Undo right on the result, for 12 s — no confirmation needed, it is the change just made
                Log.Say("{0}", msg);
                if (doneN > 0) toast.Show(L.T(msg), kind, L.T("Undo"), () => UndoLast(false)); else toast.Show(L.T(msg), kind);
                Pending.Clear(); RefreshAll(); if (detTweak != null) ShowDetails(detTweak);
                // only tweaks that were actually changed need the restart (a failed one changed nothing)
                foreach (var t in doneList.Where(x => x.Key.Reboot).Select(x => L.T(x.Key.Title))) if (!rebootTitles.Contains(t)) rebootTitles.Add(t);
                if (rebootN > 0) { restartDismissed = false; ShowRestartSummary(); }
                // 3.14: optional 30 s latency scan. After a restart-only apply the change is not live yet, so a scan would mislead.
                if (doneN > 0 && AfterApplyScan.Enabled)
                {
                    if (rebootN > 0 || relog) Log.Say("Latency scan after apply skipped: some changes are not live until a restart or sign-out.");
                    else AfterApplyScan.Run(this);
                }
            });
        }


        public void RestartNowPublic() { RestartNow(); }


        void RestartNow()
        {
            if (!Dlg.Confirm("Restart Windows now?", "Other apps will close. Save your work in them first.", "Restart now", "Later")) return;
            Shell.Run("shutdown.exe", "/r /t 3 /c \"ArchTweaker: restarting to finish applying tweaks\"", false);
        }


        public void ReviewPending() { if (applyBtn.Enabled) ApplyPending(); }

        // 3.42: true while the details panel is open over the list on a window too narrow to dock it
        bool detailsFloat;
        bool DetailsDockable { get { return ClientSize.Width - (side != null ? side.Width : 0) - Theme.S(560) >= Theme.S(260); } }

        void FitDetails()
        {
            if (details == null) return;
            // 3.40: the panel narrows (370 → 260 px) before it hides, so 1,100–1,150 px windows keep it
            int room = ClientSize.Width - (side != null ? side.Width : 0) - Theme.S(560);
            bool show = detailsWanted && room >= Theme.S(260);
            if (show) { detailsFloat = false; details.Dock = DockStyle.Right; details.Width = Math.Min(Theme.S(370), room); }
            else if (detailsWanted && detailsFloat)
            {   // 3.42: too narrow to dock: the panel slides in over the right side of the page, under the header
                details.Dock = DockStyle.None;
                int w = Math.Min(Theme.S(340), Math.Max(Theme.S(240), content.Width - Theme.S(80)));
                details.Bounds = new Rectangle(content.Right - w, content.Top, w, content.Height);
                details.BringToFront();
                show = true;
            }
            if (detClose != null) detClose.Visible = show && detailsFloat;
            details.Visible = show;   // always set: before the form is shown, Visible reads false for every child
        }

        // 3.42: a click on a row or Enter: show the details, and open the panel over the list if it can't dock.
        // Focus moves (arrow keys) and switch flips only fill the panel, so they never cover the list.
        public void OpenDetails(Tweak t)
        {
            ShowDetails(t);
            if (!DetailsDockable && detailsWanted && !detailsFloat)
            {
                detailsFloat = true; FitDetails();
            }
        }

        public bool CloseFloatingDetails()
        {
            if (!detailsFloat) return false;
            detailsFloat = false; FitDetails();
            return true;
        }

        public void RefreshHomeTiles() { Control hp; if (pages.TryGetValue("Home", out hp)) ((HomePage)hp).RefreshTiles(); }


        // 3.13: undo any saved apply session. An older session skips a tweak that a later apply changed again,
        // so undoing it never fights the newer choice.
        public void UndoSession(HistEntry e)
        {
            if (e == null) return;
            if (e.IsLast) { UndoLast(true); return; }
            if (busyCount > 0) { Notify("Wait until the current task finishes, then Undo.", Toast.Kind.Warn); return; }
            var later = new HashSet<string>(History.Entries().Where(x => x.Line > e.Line).SelectMany(x => x.Items).Select(x => x.Key.Id));
            var work = e.Items.Where(kv => !later.Contains(kv.Key.Id)).ToList();
            int skipped = e.Items.Count - work.Count;
            string when = e.When.ToString("dd MMM HH:mm");
            if (work.Count == 0)
            {
                Notify(L.T("A later apply changed every tweak in this one. Undo the later apply first."), Toast.Kind.Warn);
                return;
            }
            string list = string.Join("\n", work.Take(12).Select(x => (x.Value ? "• Revert: " : "• Re-apply: ") + L.T(x.Key.Title)).ToArray()) + (work.Count > 12 ? "\n…" : "");
            string body = L.T("Registry tweaks go back to the Windows default. Device and power-plan values go back to exactly what they were.")
                + (skipped == 1 ? " " + L.T("1 tweak changed again later stays as it is.") : skipped > 1 ? " " + L.F("{0} tweaks changed again later stay as they are.", skipped) : "") + "\n\n" + list;
            if (!Dlg.Confirm(L.F("Undo the apply of {0}?", when), body, "Undo", "Cancel")) return;
            if (work.Any(kv => !kv.Value && kv.Key.Dangerous) && !DangerZone.Unlocked) DangerZone.Ensure(this);
            Busy("Undoing the apply of " + when + "…", () =>
            {
                PowerCache.Batch(() =>
                {
                    foreach (var kv in Enumerable.Reverse(work))
                    {
                        try { if (kv.Value) kv.Key.Revert(); else kv.Key.Apply(); AppliedSet.Mark(kv.Key, !kv.Value); Log.W("{0} {1}", kv.Value ? "Reverted:" : "Re-applied:", kv.Key.Title); }
                        catch (Exception ex) { Log.Say("FAILED {0}: {1}", kv.Key.Title, ex.Message); }
                    }
                });
                History.RemoveLine(e.Line);
                Native.BroadcastSettingChange(); PowerCache.Invalidate(); Probe.Invalidate();
            }, () =>
            {
                Notify(work.Count == 1 ? L.F("Apply of {0} undone (1 change)", when) : L.F("Apply of {0} undone ({1} changes)", when, work.Count), Toast.Kind.Ok); Pending.Clear(); RefreshAll();
            });
        }


        void UndoLast() { UndoLast(true); }

        // returns false when it can't run now (busy), so the toast keeps its Undo button
        bool UndoLast(bool confirm)
        {
            // from the toast: stay quiet, so the toast keeps its Undo button for when the task ends
            if (busyCount > 0) { if (confirm) Notify("Wait until the current task finishes, then Undo.", Toast.Kind.Warn); return false; }
            var last = History.Last();
            if (last == null || last.Count == 0) return true;
            string list = string.Join("\n", last.Take(12).Select(x => (x.Value ? "• Revert: " : "• Re-apply: ") + x.Key.Title).ToArray()) + (last.Count > 12 ? "\n… and " + (last.Count - 12) + " more" : "");
            string n = last.Count + " change" + (last.Count == 1 ? "" : "s");
            if (confirm && !Dlg.Confirm("Undo the last apply?", "Reverts " + n + ". Registry tweaks go back to the Windows default. Device and power-plan values go back to exactly what they were. To restore everything from before ArchTweaker, use Undo everything.", "Undo " + n, "Keep changes", false, list)) return true;
            Busy("Undoing last apply…", () =>
            {
                PowerCache.Batch(() =>
                {
                    foreach (var kv in Enumerable.Reverse(last))
                    {
                        try { if (kv.Value) kv.Key.Revert(); else kv.Key.Apply(); AppliedSet.Mark(kv.Key, !kv.Value); Log.W("{0} {1}", kv.Value ? "Reverted:" : "Re-applied:", kv.Key.Title); }
                        catch (Exception ex) { Log.Say("FAILED {0}: {1}", kv.Key.Title, ex.Message); }
                    }
                });
                History.Pop();
                Native.BroadcastSettingChange(); PowerCache.Invalidate(); Probe.Invalidate();
            }, () =>
            {
                Notify("Last apply undone (" + last.Count + " change" + (last.Count == 1 ? "" : "s") + ")", Toast.Kind.Ok); Pending.Clear(); RefreshAll();
                // a restart is still listed only for changes that stay applied
                foreach (var kv in last.Where(x => x.Key.Reboot)) rebootTitles.Remove(L.T(kv.Key.Title));
                if (rebootTitles.Count == 0 && !BootGuard.Armed) restartBar.Visible = false; else if (restartBar.Visible) ShowRestartSummary();
            });
            return true;
        }


        // ---- 3.2: page summary in the details panel (no tweak selected)
        public void PageStatesReady(string cat) { if (detTweak == null && current == cat) ShowCategorySummary(cat); }

        // 3.35: drop a stored measurement (a bad run, something busy in the background). The tweak itself is not changed.
        void ForgetProof(Tweak t)
        {
            if (!Dlg.Confirm(L.T("Forget this result?"), L.F("ArchTweaker removes the measured result of '{0}' from this PC. The tweak itself stays as it is. You can measure it again on the A/B test page.", L.T(t.Title)), L.T("Forget"))) return;
            if (ProofStore.Forget(t.Id)) Log.Say("The measured result of '{0}' was forgotten.", L.T(t.Title));
            ShowDetails(t);
            Invalidate(true);   // repaint the rows, so the Proven chip goes away
        }

        public void ShowDetails(Tweak t)
        {
            detTweak = t; detAction.Visible = false; detForget.Visible = false;
            detTitle.Text = L.T(t.Title);
            var tags = new List<string> { L.T(t.Category).ToUpperInvariant() };
            tags.Add(L.T(t.Risk == Risk.Safe ? "SAFE" : t.Risk == Risk.Moderate ? "MODERATE" : "ADVANCED"));
            if (!Mode.LiteTweak(t)) tags.Add("PRO");   // 3.33: moved here from the row
            if (t.Undocumented) tags.Add(L.T("UNDOCUMENTED"));
            if (t.Reboot) tags.Add(L.T("RESTART")); else if (t.Relogon) tags.Add(L.T("SIGN-OUT"));
            if (t.Presets.Length > 0) tags.Add(L.T("PRESETS:") + " " + string.Join(", ", t.Presets.Select(L.T).ToArray()));
            detBadge.Text = string.Join("  ·  ", tags);
            detStats.Visible = false;
            detBadge.ForeColor = t.Risk == Risk.Advanced ? Theme.Risk : t.Risk == Risk.Moderate ? Theme.Warn : Theme.Accent;
            var sb = new System.Text.StringBuilder();
            sb.Append(L.T("WHAT IT DOES")).Append("\r\n").Append(L.T(t.Desc)).Append("\r\n");
            if (!string.IsNullOrEmpty(t.Off))   // 3.6
                sb.Append("\r\n").Append(L.T("WHAT IT TURNS OFF")).Append("\r\n").Append(L.T(t.Off)).Append("\r\n");
            if (!string.IsNullOrEmpty(t.Breaks))   // 3.4
            {
                sb.Append("\r\n").Append(L.T("WHAT IT CAN BREAK")).Append("\r\n").Append(L.T(t.Breaks)).Append("\r\n");
                if (t.Dangerous) sb.Append(L.T(DangerZone.Unlocked ? "You unlocked the Danger zone for this session." : "Locked: unlock the Danger zone page to switch it.")).Append("\r\n");
            }
            if (t.Undocumented) sb.Append("\r\n").Append(L.T("UNDOCUMENTED: no Microsoft or vendor documentation. The effect comes from tweak guides and can change between Windows builds and drivers. Not in the Recommended preset.")).Append("\r\n");   // 3.7
            if (t.EditionNote != null) sb.Append("\r\n").Append(L.T("EDITION")).Append(": ").Append(L.T(t.EditionNote)).Append(t.EditionOk ? "" : " " + L.F("This PC runs {0}.", OSInfo.Edition)).Append("\r\n");
            if (t.BuildWhy != null && t.Applicable) sb.Append("\r\n").Append(L.T("WINDOWS VERSIONS")).Append(": ").Append(L.T(t.BuildWhy)).Append("\r\n");
            if (t.CoveredBy.Length > 0)
            {
                sb.Append("\r\n").Append(L.T("COVERED BY")).Append("\r\n");
                foreach (var x in Catalog.All.Where(a => t.CoveredBy.Contains(a.Id))) sb.Append("• ").Append(L.T(x.Title)).Append("\r\n");
                sb.Append(Catalog.CoveringNow(t, x => StateCache.Get(x) == true) != null ? L.T("That tweak is on, so this one adds nothing right now.") : L.T("While that tweak is on, this one adds nothing.")).Append("\r\n");
            }
            if (t.UndoNote == "defaults") sb.Append("\r\n").Append(L.T("NOTHING TO UNDO: this puts Windows defaults back. Undo leaves the setting as it is.")).Append("\r\n");   // 3.6.2
            else if (t.UndoNote == "permanent") sb.Append("\r\n").Append(L.T("CAN'T BE UNDONE: this is a one-time cleanup. Undo only clears the mark.")).Append("\r\n");
            if (t.Excludes.Length > 0)   // 3.6.1
            {
                sb.Append("\r\n").Append(L.T("CAN'T BE ON TOGETHER WITH")).Append("\r\n");
                foreach (var x in Catalog.All.Where(a => t.Excludes.Contains(a.Id))) sb.Append("• ").Append(L.T(x.Title)).Append("\r\n");
                sb.Append(L.T("Switching this on switches the other one off.")).Append("\r\n");
            }
            if (t.Experimental) sb.Append("\r\n").Append(L.T("EXPERIMENTAL: the effect is not proven. No preset has it. Measure it on the A/B test page before you keep it.")).Append("\r\n");
            if (!string.IsNullOrEmpty(t.More)) sb.Append("\r\n").Append(L.T(t.More)).Append("\r\n");
            sb.Append("\r\n").Append(L.T("EXPECTED GAIN  —  impact:")).Append(" ").Append(L.T(t.ImpactName)).Append("\r\n").Append(L.T(t.Gain)).Append("\r\n");
            sb.Append(L.T("(Typical range from public tests and guides. Your result depends on hardware, game and what was already set. Measure with CapFrameX / PresentMon / LatencyMon.)")).Append("\r\n");
            // 3.34: what was actually measured on THIS PC (A/B test / Tuning assistant), kept honest with a stale check
            sb.Append("\r\n").Append(L.T("MEASURED ON THIS PC")).Append("\r\n");
            var pf = ProofStore.Get(t.Id);
            bool abOk = ABTest.Unsupported(t) == null && t.Impact > 0;
            if (pf != null)
            {
                var pv = pf.Value; string sw;
                if (pv.Stale(out sw)) sb.Append(L.F("{0} — stale, re-test ({1})", pv.Line(), sw));
                else if (pv.Helped) sb.Append(L.T("Proven:")).Append(" ").Append(pv.Line());
                else if (pv.Worse) sb.Append(L.T("Measured worse here:")).Append(" ").Append(pv.Line());
                else sb.Append(L.T("Within run-to-run noise here:")).Append(" ").Append(pv.Line());
                sb.Append("\r\n");
                detForget.Text = L.T("Forget this result");   // 3.35
                detForget.Tag = (Action)(() => ForgetProof(t));
                detForget.Visible = true;
            }
            else if (abOk) sb.Append(L.T("Not measured on this PC yet. Run an A/B test to prove it with your own numbers.")).Append("\r\n");
            else sb.Append(L.T("Can't be A/B tested: it needs a restart or has no measurable speed effect.")).Append("\r\n");
            if (abOk)
            {
                detAction.Text = L.T(pf != null ? "Re-test on the A/B page" : "Run an A/B test");
                detAction.Tag = (Action)(() => OpenAbTest(t));
                detAction.Visible = true;
            }
            var ev = Evidence.For(t);   // 3.0: source and strength of evidence
            sb.Append("\r\n").Append(L.T("SOURCE AND EVIDENCE")).Append("\r\n").Append(L.T(ev[0])).Append(": ").Append(L.T(ev[1])).Append("\r\n");
            detLink.Visible = ev[2] != null; detLink.Tag = ev[2];
            detLink.Text = ev[2] == null ? "" : L.T(ev[2].Contains("github.com") ? "Open the source guide" : "Search Microsoft documentation for this setting");
            sb.Append("\r\n").Append(L.T("STATUS ON THIS PC")).Append("\r\n");
            if (!t.Applicable)
                sb.Append(L.T("Not available:")).Append(" ").Append(t.BuildOk && !t.RequirementMet ? L.T("needs") + " " + L.T(t.RequirementText) : L.T(t.UnavailableReason)).Append("\r\n");
            else
            {
                bool applied = t.IsApplied();
                sb.Append(L.T(applied ? "✔ Applied now" : "○ Not applied (Windows default or your own setting)")).Append("\r\n");
                bool p;
                if (Pending.TryGetValue(t, out p)) sb.Append(L.T(p ? "→ Will be APPLIED when you click Apply" : "→ Will be REVERTED when you click Apply")).Append("\r\n");
            }
            if (t.Reboot) sb.Append(L.T("Takes effect after a restart.")).Append("\r\n");
            else if (t.Relogon) sb.Append(L.T("Takes effect after sign-out or Explorer restart.")).Append("\r\n");
            sb.Append("\r\n").Append(L.T("HOW ARCHTWEAKER DETECTS THE STATE")).Append("\r\n").Append(L.T(t.DetectSource)).Append("\r\n");
            sb.Append("\r\n").Append(L.T("EXACT CHANGES  (applied value → what unticking restores)")).Append("\r\n\r\n");
            string det = "";
            try { det = t.Details(); } catch (Exception ex) { det = "(" + L.T("could not read:") + " " + ex.Message + ")"; }
            sb.Append(det.Length > 0 ? det : L.T("• Uses an internal action — see description.") + "\r\n");
            sb.Append("\r\n").Append(L.T("ArchTweaker saves every value above before it changes it. 'Undo everything' puts back exactly what you had before ArchTweaker."));
            detBox.Text = sb.ToString();
            detBox.SelectionStart = 0; detBox.ScrollToCaret();
        }


        void UndoAll()
        {
            if (!Dlg.Confirm("Undo everything ArchTweaker changed?", "Puts back registry values, services, power plan settings, boot options (bcdedit), network settings, scheduled tasks, the timer service and the hosts block. Removed apps and cleaned files can't be restored. Restart Windows afterwards.", "Undo everything", "Keep changes", true)) return;
            Busy("Undoing all changes…", () => { FullUndo.Everything(); Native.BroadcastSettingChange(); }, () => { Notify("ArchTweaker put back everything it changed. Restart Windows to finish.", Toast.Kind.Warn); Pending.Clear(); RefreshAll(); });
        }

        // 3.30: a long task can say which step it is on (any thread)
        public void BusyStatus(string msg)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)(() => { if (busyCount > 0) { status.Text = msg; ShutdownBlock.Set(this, msg); } })); } catch (InvalidOperationException) { }
        }

        public void Busy(string msg, Action work, Action done) { Busy(msg, work, done, true); }
        // 3.40: read-only background work (page lists, scans): the sidebar stays usable, Apply and Undo wait
        public void LoadData(string msg, Action work, Action done) { Busy(msg, work, done, false); }
        // 3.38.4: lockPages false keeps the sidebar usable (start-up backup): only Apply and Undo wait for the work
        public void Busy(string msg, Action work, Action done, bool lockPages)
        {
            busyCount++; status.Text = msg; UseWaitCursor = true; if (lockPages) side.Enabled = false; applyBtn.Enabled = false; undoLastBtn.Enabled = false; tailLine.Visible = false;
            ShutdownBlock.Set(this, msg);   // 3.30: Windows asks before it shuts down in the middle of a change
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { work(); }
                catch (Exception ex)
                {
                    Log.Say("Error: {0}", ex.Message);
                    // 3.35: the type and the place go to the log file, so a bug report says where it failed
                    string at = ex.StackTrace == null ? "" : ex.StackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    Log.W("Background work failed: {0}: {1} {2}", ex.GetType().Name, ex.Message, at.Trim());
                }
                // a page can start work before the window handle exists; wait for it instead of throwing
                for (int i = 0; i < 500 && !IsHandleCreated && !IsDisposed; i++) Thread.Sleep(20);
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)(() =>
                {
                    busyCount--;
                    if (busyCount == 0) { status.Text = ""; UseWaitCursor = false; side.Enabled = true; ShutdownBlock.Clear(this); }
                    tailLine.Visible = !logOpen && busyCount == 0;
                    if (done != null) try { done(); } catch (Exception ex) { Log.Say("Error: {0}", ex.Message); }
                    UpdatePending();
                })); }
                catch (InvalidOperationException) { }   // window closed (e.g. rebuilt) while the work ran
            });
        }
    }

    // 3.30: while ArchTweaker changes something, Windows shows "ArchTweaker is preventing shutdown" with the task's name
    // instead of closing it half-way. The person can still press "Shut down anyway".
    public static class ShutdownBlock
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool ShutdownBlockReasonCreate(IntPtr hWnd, string reason);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShutdownBlockReasonDestroy(IntPtr hWnd);
        public static bool Active { get; private set; }
        public static void Set(Form f, string reason)
        {
            try { if (f.IsHandleCreated && ShutdownBlockReasonCreate(f.Handle, L.T(reason))) Active = true; } catch { }
        }
        public static void Clear(Form f)
        {
            try { if (f.IsHandleCreated) ShutdownBlockReasonDestroy(f.Handle); } catch { }
            Active = false;
        }
    }
}
