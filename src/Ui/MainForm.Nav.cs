// ArchTweaker main window: Sidebar navigation, pages, search, keyboard shortcuts and the command palette.
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
        void UpdateSubtitle()
        {
            string name = current ?? "", info, c;
            PageInfo.TryGetValue(name, out info);
            string cnt = null; int a, n;
            if (lastCounts.TryGetValue(name, out c))
            {
                var p = c.Split('/');
                if (p.Length == 2 && int.TryParse(p[0], out a) && int.TryParse(p[1], out n)) cnt = n + " tweaks · " + a + " applied";
            }
            pageSub.Text = cnt ?? info ?? "";
            subTip.SetToolTip(pageSub, L.T(info ?? ""));
        }


        static void SetCue(TextBox tb, string cue)
        {
            cue = L.T(cue);   // 3.36.1: the hint went to Windows untranslated
            try { SendMessage(tb.Handle, 0x1501, (IntPtr)1, cue); } catch { }
            tb.HandleCreated += delegate { try { SendMessage(tb.Handle, 0x1501, (IntPtr)1, cue); } catch { } };
        }


        void AddSection(FlowLayoutPanel nav, string t)
        {
            var h = new NavHeader(t) { AccessibleName = t + " section" };
            h.Click += delegate { SetSectionOpen(t, !h.Open); SaveNavState(t, h.Open); };
            headers[t] = h; sections[t] = new List<NavItem>();
            nav.Controls.Add(h);
        }


        // 3.26: page filter. A match shows its page even in a closed section; an empty filter puts the saved
        // open/closed state back. Sections with no match (and Lite's all-Pro sections) stay hidden.
        public static bool NavMatch(string page, string q)
        {
            if (string.IsNullOrEmpty(q) || q.Trim().Length == 0) return true;
            q = q.Trim();
            return page.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || L.T(page).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        void FilterNav(string q)
        {
            bool all = string.IsNullOrEmpty(q) || q.Trim().Length == 0;
            foreach (var i in navItems) if (!itemSection.ContainsKey(i)) i.Visible = NavMatch(i.Page, q);   // Home
            foreach (var kv in headers)
            {
                var items = sections[kv.Key];
                if (items.Count == 0) { kv.Value.Visible = false; continue; }
                if (all) { kv.Value.Visible = true; SetSectionOpen(kv.Key, NavState(kv.Key)); continue; }
                int n = 0;
                foreach (var i in items) { bool m = NavMatch(i.Page, q); i.Visible = m; if (m) n++; }
                kv.Value.Visible = n > 0;
            }
        }

        void SetSectionOpen(string t, bool open)
        {
            NavHeader h; if (!headers.TryGetValue(t, out h)) return;
            h.Open = open; h.Invalidate();
            var items = sections[t];
            // 3.16: opening shows the pages one after another over 150 ms; closing is instant
            if (!open || !Motion.On || !IsHandleCreated) { foreach (var i in items) i.Visible = open; return; }
            double el = 0;
            Motion.Run(h, dt =>
            {
                el += dt; int n = (int)Math.Ceiling(items.Count * Math.Min(1, el / Theme.Ms.Small));
                for (int k = 0; k < items.Count; k++) items[k].Visible = k < n && h.Open;
                return n >= items.Count || !h.Open;
            });
        }


        static bool NavState(string t)
        {
            object o = Reg.Get(Hive.HKCU, @"Software\ArchTweaker\Nav", t);
            if (t == "__log") return o is int && (int)o == 1;
            if (!(o is int)) return DefaultOpen(t, Mode.Pro);
            return (int)o != 0;
        }

        // 3.27: first run, before you open or close a section: in Pro the 14 tweak categories start closed, so the
        // tools below them show without scrolling. Lite and every other section start open.
        public static bool DefaultOpen(string section, bool pro) { return !(pro && section == "Tweaks"); }

        static void SaveNavState(string t, bool open)
        {
            try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\ArchTweaker\Nav")) k.SetValue(t, open ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord); } catch { }
        }

   // 3.22: survives the rebuild, so the new window can say what changed
        public void ModeSwitched() { modeChanged = true; Theme.SetScheme(Theme.Scheme); Rebuild(); }   // 3.29: Lite and Pro use different accents

        public void SwitchMode() { if (busyCount > 0) return; if (Mode.Toggle(this)) ModeSwitched(); }

        void AddNav(FlowLayoutPanel nav, string section, string name, Func<Control> factory)
        {
            if (!Mode.ShowsPage(name) || (section == "Tweaks" && !Mode.ShowsCategory(name))) return;   // 3.22: Lite hides Pro pages
            factories[name] = factory;
            var b = new NavItem(name);
            b.Click += delegate { ShowPage(name); };
            navItems.Add(b); nav.Controls.Add(b);
            if (section != null) { sections[section].Add(b); itemSection[b] = section; }
        }


        void OnSearch()
        {
            if (suppressSearch) return;
            string q = search.Text.Trim();
            Control c; pages.TryGetValue(current ?? "", out c);
            bool tweakCtx = c is TweakPage || c is SearchPage || c is HomePage;
            if (tweakCtx && q.Length >= 2)
            {
                if (current != SEARCH) { lastPage = current; ShowPage(SEARCH, true); }
                ((SearchPage)pages[SEARCH]).Query(q);
                pageTitle.Text = SEARCH; SetPageGlyph(null);
                return;
            }
            if (current == SEARCH && q.Length < 2) { ShowPage(lastPage ?? "Home", true); return; }
            var p = c as IPage; if (p != null) p.Filter(q);
        }


        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
            if (keyData == (Keys.Control | Keys.K)) { OpenPalette(); return true; }
            if (keyData == (Keys.Alt | Keys.Left) || keyData == Keys.BrowserBack) { GoBack(); return true; }      // 3.40
            if (keyData == (Keys.Alt | Keys.Right) || keyData == Keys.BrowserForward) { GoForward(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.L)) { SwitchTheme(); return true; }
            if (keyData == (Keys.Control | Keys.Z) && !(ActiveControl is TextBoxBase) && toast.TryAction()) return true;   // 3.2: Undo from the toast
            if (keyData == (Keys.Control | Keys.Enter)) { if (applyBtn.Enabled) ApplyPending(); return true; }
            if (keyData == Keys.Escape && search.Text.Length > 0) { search.Text = ""; return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }


        public void ShowPage(string name) { ShowPage(name, false); }

        // 3.42: opens every page once and writes the text that doesn't fit (see LayoutAudit)
        public void RunLayoutAudit(string file)
        {
            Log.W("Layout audit ({0}): {1} pages", L.Lang, factories.Count);
            var clips = new List<LayoutAudit.Clip>();
            clips.AddRange(LayoutAudit.Check(this, "(window)", content));   // sidebar, header, status bar; pages below
            foreach (var name in factories.Keys.ToList())
            {
                try
                {
                    Log.W("Layout audit: {0}", name);
                    ShowPage(name);
                    Application.DoEvents();
                    Control pg;
                    if (pages.TryGetValue(name, out pg)) clips.AddRange(LayoutAudit.Check(pg, name));
                }
                catch (Exception ex) { Log.W("Layout audit: page {0}: {1}", name, ex.Message); }
            }
            ShowPage("Home");
            LayoutAudit.Write(file, L.Lang, clips);
            Log.W("Layout audit ({0}): {1} cut-off text(s), report {2}", L.Lang, clips.Count, file);
        }

        // 3.40: back / forward (Alt+Left / Alt+Right, mouse back / forward buttons, palette)
        NavHistory history;
        bool historyMove;
        NavHistory PageHistory { get { return history ?? (history = new NavHistory(p => p != SEARCH)); } }
        public bool GoBack() { return Move(PageHistory.Back(current)); }
        public bool GoForward() { return Move(PageHistory.Forward(current)); }
        bool Move(string to)
        {
            if (to == null || busyCount > 0 && !side.Enabled) return false;
            historyMove = true;
            try { ShowPage(to); } finally { historyMove = false; }
            return true;
        }

        public void ShowPage(string name, bool keepSearch)
        {
            if (!factories.ContainsKey(name ?? "") && name != SEARCH) name = "Home";   // 3.22: a Pro page asked for in Lite
            if (!pages.ContainsKey(name))
            {
                Control page;
                try { page = factories[name](); }
                catch (Exception ex)
                {
                    Log.W("Page '{0}' failed to load: {1}", name, ex);
                    page = new Label { Text = "This page couldn't load.\r\n\r\n" + ex.Message + "\r\n\r\nOther pages still work. Restart ArchTweaker to try again. The log has the details: " + Paths.LogFile, ForeColor = Theme.Bad, Font = Theme.F, UseMnemonic = false };
                }
                page.Dock = DockStyle.Fill; page.Visible = false;
                content.Controls.Add(page);
                filterChips.SendToBack();   // docked before the Fill pages
                pages[name] = page;
            }
            if (!historyMove) PageHistory.Visit(current, name);
            current = name;
            // 3.33.1: every bar above the page (Pro notice, filter chips) and the page itself change in one layout pass,
            // and the page is laid out again after its first-show DPI scaling. Before, the first visit to a page could
            // keep the page at the top of the area while the Pro notice was drawn over its buttons, until a later visit.
            content.SuspendLayout();
            try { ShowPageCore(name, keepSearch); }
            finally
            {
                content.ResumeLayout(true);
                Control pg;
                if (pages.TryGetValue(name, out pg))
                {
                    if (filterChips.Visible && pg.Top < filterChips.Bottom)
                    {   // should not happen any more; the log line shows if it still does on some PC
                        Log.W("Layout: page '{0}' at y={1} under a bar ending at y={2}; laid out again.", name, pg.Top, filterChips.Bottom);
                        content.PerformLayout();
                    }
                    pg.PerformLayout(); pg.Invalidate(true);
                }
            }
        }

        void ShowPageCore(string name, bool keepSearch)
        {
            foreach (var kv in pages) kv.Value.Visible = kv.Key == name;
            foreach (var b in navItems)
            {
                b.Active = b.Page == name;
                string sec; if (b.Active && itemSection.TryGetValue(b, out sec)) { NavHeader h; if (headers.TryGetValue(sec, out h) && !h.Open) SetSectionOpen(sec, true); }
            }
            pageTitle.Text = name; SetPageGlyph(name); UpdateSubtitle();
            pages[name].AccessibleName = name; A11y.Fill(pages[name], name); Theme.NativeTheme(pages[name]);
            bool tweaky = pages[name] is TweakPage || pages[name] is SearchPage;
            detailsWanted = tweaky; FitDetails();
            // 3.2: a tweak page opens with its summary in the details panel instead of the last tweak from another page
            if (pages[name] is TweakPage && (detTweak == null || detTweak.Category != name)) ShowCategorySummary(name);
            // 3.14: presets tick tweaks, so they show only where a tweak list or its summary is in view
            bool presets = tweaky || pages[name] is HomePage || pages[name] is AdvisorPage;
            preset.Visible = selectPreset.Visible = presets;
            filterChips.Visible = tweaky; UpdateOutsideBar();
            proBar.Visible = Mode.Pro && (Mode.ProPage(name) || name == Cat.Danger || (pages[name] is TweakPage && !Catalog.All.Any(t => t.Category == name && Mode.LiteTweak(t))));   // 3.29
            if (tweaky) filterChips.Recount(pages[name] is TweakPage ? name : null);
            if (!keepSearch) { suppressSearch = true; search.Text = ""; suppressSearch = false; }
            var p = pages[name] as IPage;
            if (p != null) { try { p.OnShow(); } catch (Exception ex) { Log.W("Page '{0}' error: {1}", name, ex); } }
            if (scaledPages.Add(name)) Theme.ScaleTree(pages[name]);   // once, after the first build (tweak rows scale themselves)
        }


        // 3.34: open the A/B test page with a tweak already chosen (from its details pane)
        public void OpenAbTest(Tweak t)
        {
            ShowPage("A/B test");
            Control pg; if (pages.TryGetValue("A/B test", out pg) && pg is ABTestPage) ((ABTestPage)pg).Preselect(t);
        }

        // ---- 3.0: command palette (Ctrl+K) ----
        public void OpenPalette()
        {
            var cmds = new List<CommandPalette.Cmd>();
            // 3.40: recently used pages first (an empty palette lists them at the top, in this order)
            var recent = PageHistory.Recent(current, 5).Where(p => factories.ContainsKey(p)).ToList();
            foreach (var name in recent.Concat(factories.Keys.Where(k => k != SEARCH && !recent.Contains(k))))
            {
                string n = name;
                cmds.Add(new CommandPalette.Cmd { Kind = "Page", Text = L.T(n), Search = n + " " + L.T(n), Run = () => ShowPage(n) });
            }
            foreach (var t in Catalog.All)
            {
                var tt = t;
                cmds.Add(new CommandPalette.Cmd { Kind = "Tweak", Text = L.T(t.Title), Search = t.Title + " " + L.T(t.Title) + " " + t.Category + " " + L.T(t.Category), Run = () => { ShowPage(tt.Category); search.Text = L.T(tt.Title); } });
            }
            Action<string, Action> act = (text, run) => cmds.Add(new CommandPalette.Cmd { Kind = "Action", Text = L.T(text), Search = text + " " + L.T(text), Run = run });
            act("Switch dark / light", () => SwitchTheme());
            for (int i = 0; i < Theme.SchemeNames.Length; i++) { int si = i; act("Colour scheme: " + Theme.SchemeNames[i], () => { if (si == Theme.CustomScheme) PickCustomScheme(); else SetScheme(si); }); }
            for (int i = 0; i < L.Codes.Length; i++) { string c = L.Codes[i]; act("Language: " + L.Names[i], () => SetLanguage(c)); }
            act("Go back (Alt+Left)", () => GoBack());          // 3.40
            act("Go forward (Alt+Right)", () => GoForward());
            act("Apply pending changes", () => { if (applyBtn.Enabled) ApplyPending(); });
            act("Undo last apply", () => UndoLast());
            act("Select the picks for this PC", () => SelectAutoPreset());
            act("Run the setup guide again", () => RunSetupGuide());
            act("Slim sidebar on / off", () => SetSlim(!Theme.Slim));
            act("Follow Windows theme on / off", () => SetFollowWindows(!Theme.FollowWindows));
            act("Copy log for support", () => CopySupportLog());
            for (int i = 0; i < TweakSort.Names.Length; i++) { int si3 = i; act("Sort tweaks: " + TweakSort.Names[i], () => SetSort(si3)); }
            for (int i = 0; i < Theme.TextSizeNames.Length; i++) { int ti3 = i; act("Text size: " + Theme.TextSizeNames[i], () => SetTextSize(ti3)); }
            // every button on Tools & backup and Settings
            foreach (var pageName in new[] { "Tools & backup", "Settings" })
            {
                var tools = EnsurePage(pageName);
                if (tools == null) continue;
                string pn = pageName;
                foreach (var b in All(tools).OfType<Button>().Where(x => x.Text.Length > 0))
                {
                    var bb = b;
                    cmds.Add(new CommandPalette.Cmd { Kind = "Tool", Text = bb.Text, Search = bb.Text + " " + (bb.AccessibleName ?? ""), Run = () => { ShowPage(pn); bb.PerformClick(); } });
                }
            }
            CommandPalette.Open(this, cmds);
        }

        Control EnsurePage(string name)
        {
            if (!pages.ContainsKey(name))
            {
                string keep = current; ShowPage(name); if (keep != null) ShowPage(keep);
            }
            Control p; return pages.TryGetValue(name, out p) ? p : null;
        }

        void SetPageGlyph(string page)
        {
            char c; bool has = page != null && pageGlyph.Font != null && NavItem.IconFont != null && NavItem.TryGlyph(page, out c);
            if (has) { NavItem.TryGlyph(page, out c); pageGlyph.Text = c.ToString(); }
            pageGlyph.Visible = has;
            pageTitle.Left = has ? pageGlyph.Right + Theme.S(10) : 0;
        }

        public Control PageOf(string name) { Control c; return pages.TryGetValue(name, out c) ? c : null; }
    }
}
