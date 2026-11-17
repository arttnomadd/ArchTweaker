using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{

    public interface IPage { void OnShow(); void Filter(string q); }

    // ================= MAIN FORM =================
    public partial class MainForm : Form
    {
        Panel side, content, bottom;
        Label pageTitle, pageGlyph, pageSub, status; float underline; readonly ToolTip subTip = Tips.New();
        Dictionary<string, string> lastCounts = new Dictionary<string, string>();
        // one line under the page title: what the page is for (tweak pages also show applied / total)
        static readonly Dictionary<string, string> PageInfo = new Dictionary<string, string>
        {
            { "Home", "Presets, quick actions and what's applied on this PC" },
            { Cat.Privacy, "Telemetry, advertising ID, activity history and data sharing" },
            { Cat.Interface, "Visual effects, animations, taskbar and window behaviour" },
            { Cat.Explorer, "File Explorer defaults and right-click menu clutter" },
            { Cat.System, "Core Windows behaviour, updates and background features" },
            { Cat.Responsiveness, "Scheduler, memory and I/O settings for a snappier desktop" },
            { Cat.Gaming, "Game Mode, fullscreen, Game Bar and frame pacing" },
            { Cat.PowerPlan, "Settings of the active power plan" },
            { Cat.Input, "Mouse, keyboard and timer settings" },
            { Cat.Gpu, "Graphics driver, NVIDIA profile and device power saving" },
            { Cat.Network, "Adapter, TCP and DNS settings" },
            { Cat.TasksServices, "Scheduled tasks and background services" },
            { Cat.Commands, "bcdedit, netsh, fsutil and powercfg settings" },
            { Catalog.HARDENING, "Closes old protocols and remote entry points. Makes the PC safer, not faster" },
            { DangerZone.CAT, "Tweaks that can break features or lower security. Locked until you unlock it" },
            { "PC advisor", "What fits this PC's hardware, and why" },
            { "System scan", "Current state of the system, read live" },
            { "Services", "Start type of every Windows service" },
            { "Apps (UWP)", "Built-in Store apps you can remove" },
            { "Startup", "Programs that start with Windows" },
            { "Cleanup", "Temporary files, caches and logs" },
            { "Deep clean", "Per-app cleaning rules (Winapp2)" },
            { "Uninstaller", "Desktop programs, with leftover cleanup" },
            { "Power settings", "Every power setting of every plan, including hidden ones" },
            { "Context menu", "Turn right-click menu entries on or off" },
            { "Hosts blocker", "Block ad and tracking domains system-wide" },
            { "Measure", "Frame times and latency with PresentMon" },
            { "A/B test", "Prove whether one tweak changes your fps" },
            { "Tools & backup", "Maintenance, backups, recovery kit and Windows fixes" },
            { "Settings", "Language, colour scheme, notification icon, app mode and help" },
            { "Latency", "Live DPC and interrupt load per core, and the drivers that cause it" },
            { "Benchmark", "One click: frame times, driver latency and timer precision in one report" },
            { "Tuning assistant", "Tests tweaks one by one on your game and keeps only the ones that helped" },
            { "Live overlay", "FPS, DPC and interrupt load live over your game" },
            { "Interrupt tuner", "Tests MSI mode, priority and CPU for each device and keeps what makes its driver faster" },
            { "Power tuner", "Tries processor power plan values and keeps the ones that measurably help" },
            { "Tune everything", "Runs the three tuners one after another and keeps only what helps" },
            { "Results", "Every saved run of every test and tuner, newest first" },
            { "Mouse polling", "Real polling rate and report jitter of your mouse" },
            { "Hardware audit", "PCIe links, Resizable BAR, RAM, refresh rates and firmware, with how to fix each" },
            { "Boot times", "How long each start takes, and what slowed the last one" },
            { "Bake into ISO", "Writes tweaks into a Windows setup ISO, so a fresh install starts tuned" },
            { "Drivers", "Old driver versions left in the driver store" },
            { "What changed", "Tweaks, services and startup entries changed since your last apply" },
            { "Disk space", "What takes space on your drives" },
            { "System info", "Hardware and Windows details" },
        };
        TextBox search, logBox;
        ComboBox preset; Button selectPreset; FilterChips filterChips; Label proBar;
        Button applyBtn, discardBtn, undoLastBtn, logToggle;
        CheckBox rpCheck;
        Label tailLine; Action<string> noticeHandler;
        InfoBar restartBar; Label restartText; SideFooter sideFooter;
        bool logOpen;
        static bool sessionLog;
        string lastPage;
        readonly Dictionary<string, Control> pages = new Dictionary<string, Control>();
        readonly List<NavItem> navItems = new List<NavItem>();
        readonly Dictionary<string, List<NavItem>> sections = new Dictionary<string, List<NavItem>>();
        readonly Dictionary<string, NavHeader> headers = new Dictionary<string, NavHeader>();
        readonly Dictionary<NavItem, string> itemSection = new Dictionary<NavItem, string>();
        public static readonly Dictionary<Tweak, bool> PendingStore = new Dictionary<Tweak, bool>();
        public Dictionary<Tweak, bool> Pending { get { return PendingStore; } }
        public static MainForm Instance;
        static bool restorePointDone, backupDone;
        string current;
        Panel details; Label detTitle, detBadge; TextBox detBox; Tweak detTweak; LinkLabel detLink, detForget; StatTiles detStats;
        Action<string> logHandler;

        public MainForm() : this(null) { }

        public MainForm(string startPage)
        {
            Instance = this;
            Chrome.AppMode();   // before any window exists, so scrollbars and menus come up in the right mode
            Text = "ArchTweaker " + Program.Version;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F;
            // 2.6: sizes are 96-dpi pixels scaled by Theme.S; WinForms auto-scaling is off so nothing scales twice
            AutoScaleMode = AutoScaleMode.None;
            var wa = Screen.PrimaryScreen.WorkingArea;
            MinimumSize = new Size(Math.Min(Theme.S(1100), wa.Width), Math.Min(Theme.S(640), wa.Height));
            Size = new Size(Math.Min(Theme.S(1380), wa.Width), Math.Min(Theme.S(840), wa.Height)); StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(20, 4, 20, 8) };

            // ---- sidebar
            // 3.2: slim sidebar = icons only (names and counts in the tooltip), for more room on smaller screens
            side = new Panel { Dock = DockStyle.Left, Width = Theme.Slim ? 72 : 226 + Theme.TextSize * 22, BackColor = Theme.Side, Padding = Theme.Slim ? new Padding(4, 14, 0, 10) : new Padding(8, 14, 0, 10) };
            var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Side };
            // vertical scrolling only: items are sized to leave room for the scrollbar
            nav.HorizontalScroll.Maximum = 0; nav.HorizontalScroll.Visible = false;
            int navW = side.Width - side.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - (Theme.Slim ? 2 : 6);
            nav.ControlAdded += (s2, e2) => { if (e2.Control is NavItem || e2.Control is NavHeader) e2.Control.Width = navW; };
            var logo = new Label { Text = Theme.Slim ? "A" : "ARCH", Font = Theme.LogoFont, ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(4, 0, 0, 0), Tag = "notr" };
            var logo2 = new Label { Text = Theme.Slim ? "T" : "TWEAKER", Font = Theme.LogoFont, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, 0, 0, 0), Tag = "notr" };
            var logoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 2) };
            logoRow.Controls.Add(logo); logoRow.Controls.Add(logo2);
            var sub = new Label { Text = (OSInfo.IsWin11 ? "Windows 11 · build " : "Windows 10 · build ") + OSInfo.Build + "  ·  v" + Program.Version, Font = Theme.FS, ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(6, 0, 0, 10), Visible = !Theme.Slim };
            nav.Controls.Add(logoRow); nav.Controls.Add(sub);
            if (!Theme.Slim)   // 3.26: filter the sidebar by page name
            {
                var pageBox = new TextBox { AccessibleName = "Find a page" };
                var pageFind = new SearchField(pageBox, navW) { Margin = new Padding(0, 8, 0, 6) };
                SetCue(pageBox, "Find a page");
                pageBox.TextChanged += delegate { FilterNav(pageBox.Text); };
                pageBox.KeyDown += (s4, e4) =>
                {
                    if (e4.KeyCode == Keys.Escape) { pageBox.Text = ""; e4.SuppressKeyPress = true; }
                    else if (e4.KeyCode == Keys.Enter) { var first = navItems.FirstOrDefault(i => i.Visible); if (first != null) ShowPage(first.Page); e4.SuppressKeyPress = true; }
                };
                nav.Controls.Add(pageFind);
            }
            AddNav(nav, null, "Home", () => new HomePage());
            AddSection(nav, "Tweaks");
            foreach (var c in Catalog.Categories) { string cc = c; AddNav(nav, "Tweaks", cc, () => new TweakPage(cc)); }
            // 3.12: four short groups instead of two long ones (Windows guidance: two levels, under ten items a group)
            AddSection(nav, "Diagnostics");   // 3.25: Analyse split in four
            AddNav(nav, "Diagnostics", "PC advisor", () => new AdvisorPage());
            AddNav(nav, "Diagnostics", "System scan", () => new ScanPage());
            AddNav(nav, "Diagnostics", "Hardware audit", () => new HwAuditPage());   // 3.21
            AddNav(nav, "Diagnostics", "System info", () => new InfoPage());
            AddNav(nav, "Diagnostics", "Boot times", () => new BootTimesPage());   // 3.24
            AddSection(nav, "Measure & test");   // 3.25: Analyse split in four
            AddNav(nav, "Measure & test", "Latency", () => new LatencyPage());
            AddNav(nav, "Measure & test", "Benchmark", () => new BenchmarkPage());   // 3.18
            AddNav(nav, "Measure & test", "Measure", () => new MeasurePage());
            AddNav(nav, "Measure & test", "A/B test", () => new ABTestPage());
            AddNav(nav, "Measure & test", "Live overlay", () => new OverlayPage());   // 3.19
            AddNav(nav, "Measure & test", "Mouse polling", () => new MousePollPage());   // 3.20
            AddSection(nav, "Tune");   // 3.25: Analyse split in four
            AddNav(nav, "Tune", "Tuning assistant", () => new TunerPage());   // 3.19
            AddNav(nav, "Tune", "Interrupt tuner", () => new IrqTunerPage());   // 3.20
            AddNav(nav, "Tune", "Power tuner", () => new PowerTunerPage());   // 3.24
            AddNav(nav, "Tune", "Tune everything", () => new TuneAllPage());   // 3.26
            AddSection(nav, "History");   // 3.25: Analyse split in four
            AddNav(nav, "History", "What changed", () => new ChangesPage());
            AddNav(nav, "History", "Results", () => new ResultsPage());   // 3.26
            AddNav(nav, "History", "Change history", () => new ChangeHistoryPage());   // 3.17
            AddSection(nav, "Clean & apps");
            AddNav(nav, "Clean & apps", "Cleanup", () => new CleanupPage());
            AddNav(nav, "Clean & apps", "Deep clean", () => new Winapp2Page());
            AddNav(nav, "Clean & apps", "Disk space", () => new DiskPage());
            AddNav(nav, "Clean & apps", "Uninstaller", () => new UninstallPage());
            AddNav(nav, "Clean & apps", "Apps (UWP)", () => new AppsPage());
            AddNav(nav, "Clean & apps", "Startup", () => new AutorunPage());
            AddNav(nav, "Clean & apps", "Services", () => new ServicesPage());
            AddSection(nav, "Windows tools");
            AddNav(nav, "Windows tools", "Power settings", () => new PowerSettingsPage());
            AddNav(nav, "Windows tools", "Context menu", () => new ContextMenuPage());
            AddNav(nav, "Windows tools", "Hosts blocker", () => new HostsPage());
            AddNav(nav, "Windows tools", "Drivers", () => new DriversPage());
            AddNav(nav, "Windows tools", "Bake into ISO", () => new IsoPage());   // 3.19
            foreach (var kv in headers) if (sections[kv.Key].Count == 0) kv.Value.Visible = false;   // 3.25: Lite hides a section whose pages are all Pro
            // 3.12: settings sit pinned under the scrolling list, as in Windows Settings and PowerToys
            var pin = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Side, Padding = new Padding(0, 6, 0, 0) };
            pin.Paint += (s3, e3) => { using (var pen = new Pen(Theme.Border)) e3.Graphics.DrawLine(pen, 0, 0, pin.Width, 0); };
            AddNav(pin, null, "Tools & backup", () => new ToolsPage());
            AddNav(pin, null, "Settings", () => new SettingsPage());   // 3.31
            foreach (Control pc in pin.Controls) pc.Width = navW;
            side.Controls.Add(pin);
            // 3.16: version, and a restart badge visible on every page
            sideFooter = new SideFooter(() => RestartPending, () => { restartDismissed = false; if (rebootTitles.Count > 0) ShowRestartSummary(); else restartBar.Visible = true; });
            side.Controls.Add(sideFooter);
            side.Controls.Add(new ModeSwitch(ModeSwitched));   // 3.22
            factories[SEARCH] = () => new SearchPage();
            foreach (var hk in headers.Keys.ToList()) SetSectionOpen(hk, NavState(hk));
            side.Controls.Add(nav); nav.BringToFront();   // Fill docks last, after the pinned panel

            // ---- top bar
            // 3.16: less empty height; a page glyph before the title
            var top = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Theme.Bg, Padding = new Padding(20, 8, 16, 4) };
            pageTitle = new Label { Font = Theme.FH, ForeColor = Theme.Text, AutoSize = true, Location = new Point(0, 0), Name = "pageTitle", UseMnemonic = false };   // 3.38.2: Name gives the UIA AutomationId; screen readers now read the page title, not "pageTitle"
            pageGlyph = new Label { AutoSize = true, ForeColor = Theme.Accent, UseMnemonic = false, Visible = false, Location = new Point(0, 4), AccessibleRole = AccessibleRole.Graphic, Tag = "notr" };
            if (NavItem.IconFont != null) pageGlyph.Font = new Font(NavItem.IconFont.FontFamily, 20f * Theme.Dpi / 96f, GraphicsUnit.Pixel);   // fonts are not part of the DPI scale pass
            // 3.1: the subtitle gets its own full-width line under the title and the toolbar, so it is no longer cut short
            pageSub = new Label { Font = Theme.FS, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Bottom, Height = 20, Padding = new Padding(1, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "pageSubtitle", UseMnemonic = false };
            var titleBox = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            titleBox.Controls.Add(pageTitle); titleBox.Controls.Add(pageGlyph);
            var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Bg };
            search = new TextBox { Width = 240, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
            var searchField = new SearchField(search, 260) { Margin = new Padding(0, 3, 12, 0) };
            SetCue(search, "Search all tweaks  (Ctrl+F)"); search.AccessibleName = "Search all tweaks";
            search.TextChanged += delegate { OnSearch(); };
            // 3.12: chips above the list replace the "Show" drop-down
            filterChips = new FilterChips { Dock = DockStyle.Top, Visible = false };
            filterChips.Changed += delegate { OnSearch(); };
            content.Controls.Add(filterChips);
            // 3.29: Pro-only pages say so; it stays while the page is open (the page itself never covers it)
            // 3.36.2: the Pro notice docks at window level, like the update and drift bars. Inside the page area its place in
            // the docking order could be lost on Windows, and it was drawn over the page's first toolbar row (tuner pages).
            proBar = new Label { Dock = DockStyle.Top, Visible = false, AutoSize = false, Height = Theme.S(34), Padding = new Padding(Theme.S(20), 0, Theme.S(12), 0), TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Gfx.Mix(Theme.Warn, Theme.Panel, 0.86), ForeColor = Theme.Text, Font = Theme.FS, UseMnemonic = false,
                Text = "⚠  " + L.T("Expert area (Pro). These changes can affect stability. ArchTweaker makes a restore point before it applies them."), AccessibleRole = AccessibleRole.Alert, Tag = "notr" };
            // 3.36: the tint carries the warning; hairlines in the tone close it (no edge stripe)
            proBar.Paint += (s9, e9) => { using (var pen = new Pen(Gfx.Mix(Theme.Warn, Theme.Bg, 0.55))) { e9.Graphics.DrawLine(pen, 0, 0, proBar.Width, 0); e9.Graphics.DrawLine(pen, 0, proBar.Height - 1, proBar.Width, proBar.Height - 1); } };
            // 3.33.1: a bar that appears, hides or changes height outside ShowPage moves the page too
            EventHandler relayout = delegate { if (!content.IsDisposed) content.PerformLayout(); };
            proBar.VisibleChanged += relayout; proBar.SizeChanged += relayout;
            filterChips.VisibleChanged += relayout; filterChips.SizeChanged += relayout;
            preset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 5, 6, 0) };
            preset.AccessibleName = "Preset"; preset.Items.AddRange(new object[] { Catalog.PRESET_AUTO, Catalog.PRESET_SAFE, Catalog.PRESET_GAMING, Catalog.PRESET_PRIVACY });
            preset.SelectedIndex = 0;
            selectPreset = Theme.Btn("Select preset", delegate { SelectPreset(preset.SelectedItem.ToString()); });
            // 3.10.4: View: the mouse opens the Appearance flyout; Enter/Space or a right-click opens the View menu
            var viewBtn = Theme.Btn("View", null); viewBtn.Margin = new Padding(0, 0, 14, 0); viewBtn.AccessibleName = "View options";
            viewButton = viewBtn;
            bool viewByMouse = false;
            viewBtn.MouseDown += delegate (object s2, MouseEventArgs me) { viewByMouse = me.Button == MouseButtons.Left; if (me.Button == MouseButtons.Right) ShowViewMenu(); };
            viewBtn.KeyDown += delegate { viewByMouse = false; };
            viewBtn.Click += delegate { if (viewByMouse) OpenAppearance(); else ShowViewMenu(); viewByMouse = false; };
            right.Controls.Add(viewBtn); right.Controls.Add(searchField); right.Controls.Add(preset); right.Controls.Add(selectPreset);
            top.Controls.Add(titleBox); top.Controls.Add(right); top.Controls.Add(pageSub);   // last added docks first: the subtitle spans the full width
            // 3.16: a 1 px rule across the page, with a 2 px accent segment under the title only
            var strip = new Panel { Dock = DockStyle.Top, Height = 2, BackColor = Theme.Bg };
            strip.Paint += (s9, e9) =>
            {
                using (var pen = new Pen(Theme.Border)) e9.Graphics.DrawLine(pen, 0, strip.Height - 1, strip.Width, strip.Height - 1);
                using (var b = new SolidBrush(Theme.Accent)) e9.Graphics.FillRectangle(b, top.Padding.Left, 0, (int)Math.Max(Theme.S(40), underline), strip.Height);
            };
            // the accent segment follows the title width; it slides there over 150 ms when the page changes
            EventHandler follow = delegate
            {
                float to = pageTitle.Right + Theme.S(4), from = underline;
                if (!Motion.On || from <= 0) { underline = to; strip.Invalidate(); return; }
                double t = 0;
                Motion.Run(strip, dt => { t = Math.Min(1, t + dt / Theme.Ms.Small); double e = 1 - Math.Pow(1 - t, 3); underline = (float)(from + (to - from) * e); strip.Invalidate(); return t >= 1; });
            };
            pageTitle.SizeChanged += follow; pageTitle.LocationChanged += follow;

            // ---- bottom bar + log
            logOpen = sessionLog;   // 3.10.1: closed at every start; a rebuild (theme, language) keeps the current state
            bottom = new Panel { Dock = DockStyle.Bottom, Height = logOpen ? 204 : 56, BackColor = Theme.Side, Padding = new Padding(16, 8, 16, 8) };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, BackColor = Theme.Side };
            // 3.36: the Apply bar is its own layer: a hairline along its top edge
            bottom.Paint += (s9, e9) => Gfx.Hair(e9.Graphics, 0, 0, bottom.Width, 0);
            applyBtn = Theme.Primary("Apply changes", delegate { ApplyPending(); });
            discardBtn = Theme.Btn("Discard changes", delegate { Pending.Clear(); RefreshAll(); });
            var undoAll = Theme.Btn("Undo everything", delegate { UndoAll(); });
            undoLastBtn = Theme.Btn("Undo last apply", delegate { UndoLast(); });
            logToggle = Theme.Btn(logOpen ? "Hide log" : "Show log", delegate { ToggleLog(); });
            var tips = Tips.New();
            tips.SetToolTip(undoLastBtn, L.T("Reverts only the tweaks from your most recent Apply."));
            tips.SetToolTip(applyBtn, L.T("Review and apply the ticked changes (Ctrl+Enter)."));
            tailLine = new Label { AutoSize = false, Width = 420, Height = 22, ForeColor = Theme.Muted, Font = Theme.FS, Margin = new Padding(12, 9, 0, 0), AutoEllipsis = true, UseMnemonic = false, Visible = !logOpen };
            rpCheck = new TickBox { Text = "Create restore point before first apply", Checked = true, ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
            if (Mode.Lite) { rpCheck.Checked = true; rpCheck.Enabled = false; }   // 3.23: always on in Lite
            status = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(16, 9, 0, 0) };
            actions.Controls.AddRange(new Control[] { applyBtn, discardBtn, undoLastBtn, undoAll, logToggle, rpCheck, status, tailLine });
            // 3.33: the status line takes the rest of the bar, so its text is cut only when the window is really narrow
            actions.Layout += delegate { if (tailLine.Visible) { int w = actions.ClientSize.Width - tailLine.Left - Theme.S(12); if (w > Theme.S(120) && w != tailLine.Width) tailLine.Width = w; } };
            logBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.LogBg, ForeColor = Theme.LogFg, BorderStyle = BorderStyle.None, Font = Theme.Mono, AccessibleName = "Activity log" };
            // 3.2: log filter and a support copy. The whole session's log lives in LogStore, so the filter can change after the fact.
            logBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false, BackColor = Theme.Side, Visible = logOpen, Padding = new Padding(0, 2, 0, 0) };
            var logFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, BackColor = Theme.Panel, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 8, 0), AccessibleName = "Show in log" };
            foreach (var fn in LogStore.FilterNames) logFilter.Items.Add(L.T(fn));
            logFilter.SelectedIndex = LogStore.Filter;
            logFilter.SelectionChangeCommitted += delegate { LogStore.Filter = logFilter.SelectedIndex; ReloadLog(); };
            var copyLog = Theme.Btn("Copy for support", delegate { CopySupportLog(); });
            tips.SetToolTip(copyLog, L.T("Copies the ArchTweaker version, the Windows build and the log shown here, ready to paste into a bug report."));
            var clearView = Theme.Btn("Clear view", delegate { logBox.Clear(); });
            logBar.Controls.Add(logFilter); logBar.Controls.Add(copyLog); logBar.Controls.Add(clearView);
            bottom.Controls.Add(logBox); bottom.Controls.Add(logBar); bottom.Controls.Add(actions);
            LogStore.Hook();
            // details and problems arrive as they are logged; results-only lines arrive once Log.Say marks them
            entryHandler = e2 =>
            {
                bool show = LogStore.Filter == 0 && e2.Kind != LogStore.Kind.Result || LogStore.Filter == 2 && e2.Kind == LogStore.Kind.Problem
                         || LogStore.Filter == 1 && (e2.Kind == LogStore.Kind.Result || e2.Kind == LogStore.Kind.Problem);
                if (!show) return;
                try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => logBox.AppendText(e2.Text + Environment.NewLine))); } catch { }
            };
            // snapshot and subscribe under one lock: no line is lost or shown twice after a rebuild
            logBox.Text = LogStore.Subscribe(entryHandler, LogStore.Filter);
            FormClosed += delegate { LogStore.Unsubscribe(entryHandler); };
            logHandler = s => { };
            // status line: only Log.Say messages (results the user cares about); full text on hover
            noticeHandler = s => { try { if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => { tailLine.Text = s; tips.SetToolTip(tailLine, L.T(s)); })); } catch { } };
            Log.Notice += noticeHandler;
            FormClosed += delegate { Log.Notice -= noticeHandler; };
            Log.Line += logHandler;
            FormClosed += delegate { Log.Line -= logHandler; };

            // ---- details panel (right)
            details = new Panel { Dock = DockStyle.Right, Width = 370, BackColor = Theme.Panel };
            var detInner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(16, 14, 12, 10) };
            detTitle = new Label { Dock = DockStyle.Top, Height = 54, Font = Theme.Semi(12f), ForeColor = Theme.Strong, Text = "Tweak details" };
            detBadge = new Label { Dock = DockStyle.Top, Height = 24, Font = Theme.FT, ForeColor = Theme.Accent };
            detBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.F,
                Text = L.T("Select a tweak (click it, or Tab to its switch) to see what it does and its state. You also see the exact registry values, services, tasks, power settings and commands it changes, and how Undo restores each one.") };
            detLink = new LinkLabel { Dock = DockStyle.Bottom, Height = 28, Font = Theme.FS, LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentDark, VisitedLinkColor = Theme.Accent, Visible = false, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
            detLink.LinkClicked += delegate { var act = detLink.Tag as Action; if (act != null) { act(); return; } var u = detLink.Tag as string; if (u != null && u.StartsWith("https://")) Shell.OpenVisible(u, ""); };
            // 3.35: "Forget this result" under a stored measurement
            detForget = new LinkLabel { Dock = DockStyle.Bottom, Height = 26, Font = Theme.FS, LinkColor = Theme.Muted, ActiveLinkColor = Theme.Accent, VisitedLinkColor = Theme.Muted, Visible = false, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
            detForget.LinkClicked += delegate { var act = detForget.Tag as Action; if (act != null) act(); };
            var detStrip = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Border };   // quiet divider: the accent is kept for state, not decoration
            // 3.2: one action under the page summary ("Select the 3 recommended tweaks")
            detAction = Theme.Btn("", delegate { var a = detAction.Tag as Action; if (a != null) a(); });
            detAction.Dock = DockStyle.Bottom; detAction.AutoSize = false; detAction.Height = 34; detAction.Visible = false; detAction.TextAlign = ContentAlignment.MiddleCenter;
            var detGap = new Panel { Dock = DockStyle.Bottom, Height = 8, BackColor = Theme.Panel };
            detStats = new StatTiles { Dock = DockStyle.Top, Visible = false };   // 3.33: page summary numbers as tiles
            detInner.Controls.Add(detBox); detInner.Controls.Add(detLink); detInner.Controls.Add(detForget); detInner.Controls.Add(detGap); detInner.Controls.Add(detAction); detInner.Controls.Add(detStats); detInner.Controls.Add(detBadge); detInner.Controls.Add(detTitle);
            details.Controls.Add(detInner); details.Controls.Add(detStrip);

            restartBar = new InfoBar(InfoBar.Kind.Warning) { AccessibleName = "Restart notice" }; restartBar.VisibleChanged += delegate { if (sideFooter != null) sideFooter.Refresh2(); };
            var rbFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };
            restartText = new Label { AutoSize = false, AutoEllipsis = true, Height = 22, Width = 300, ForeColor = Theme.Strong, Font = Theme.FB, Margin = new Padding(0, 6, 16, 0), TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
            rbFlow.Controls.Add(restartText);
            rbFlow.Controls.Add(Theme.Lead("Restart now", delegate { RestartNow(); }));
            rbFlow.Controls.Add(Theme.Btn("Later", delegate { restartBar.Visible = false; restartDismissed = true; }));
            restartBar.Controls.Add(rbFlow);
            restartTip = Tips.New();
            restartBar.Resize += delegate { if (restartBar.Visible) FitRestartText(); };
            if (rebootTitles.Count > 0 && !restartDismissed) ShowRestartSummary();   // 3.2: survives a theme/language rebuild
            sideFooter.Refresh2();

            // feature-update notice: shown when a Windows build change reset tweaks ArchTweaker had applied
            updBar = new InfoBar(InfoBar.Kind.Warning) { AccessibleName = "Windows update notice" };
            updText = new Label { ForeColor = Theme.Strong, Font = Theme.FB, UseMnemonic = false };
            updBar.Compose(updText,   // 3.36.1: the text wraps; the buttons stay on screen
                Theme.Lead("Re-apply them", delegate { ReapplyReset(); }),
                Theme.Btn("Review first", delegate { if (updReset != null) { SelectIds(updReset.Select(d => d.Tweak.Id)); Log.W("Reset by the Windows update: {0}", string.Join(", ", updReset.Select(d => d.Tweak.Title).ToArray())); } updBar.Visible = false; }),
                Theme.Btn("Dismiss", delegate { if (updIsDrift && updReset != null) { DriftGuard.Dismiss(updReset); DriftWatch.Seen(updReset); } updBar.Visible = false; }));

            // 3.12: tweaks on this page that ArchTweaker applied and something else turned back
            outBar = new InfoBar(InfoBar.Kind.Info) { AccessibleName = "Changed outside ArchTweaker" };
            outText = new Label { ForeColor = Theme.Strong, Font = Theme.FB, UseMnemonic = false };
            outBar.Compose(outText,   // 3.36.1
                Theme.Btn("Re-apply them", delegate { ReapplyOutside(); }),
                Theme.Btn("Show them", delegate { TweakFilter.Mode = 7; OnSearch(); filterChips.Invalidate(); }),
                Theme.Btn("Dismiss", delegate { if (current != null) outDismissed.Add(current); outBar.Visible = false; }));

            // result pop-up sits over the content, bottom-right, just above the Apply bar
            toast = new Toast();
            EventHandler placeToast = delegate { if (toast.Visible) toast.Location = new Point(ClientSize.Width - (details.Visible ? details.Width : 0) - toast.Width - Theme.S(24), bottom.Top - toast.Height - Theme.S(12)); };
            toast.Placed += placeToast; Resize += placeToast; Resize += delegate { FitDetails(); };   // 3.16
            Controls.Add(toast);
            Controls.Add(content); Controls.Add(proBar); Controls.Add(outBar); Controls.Add(updBar); Controls.Add(restartBar); Controls.Add(details); Controls.Add(strip); Controls.Add(top); Controls.Add(bottom); Controls.Add(side);
            // chrome is built in 96-dpi pixels: scale it once here. Pages are scaled on first show (ShowPage).
            foreach (Control c in new Control[] { outBar, updBar, restartBar, details, strip, top, bottom, side }) Theme.ScaleTree(c);
            content.Padding = Theme.S(content.Padding);
            // keyboard order follows reading order: sidebar → top bar → restart notice → page → details → Apply bar (WCAG 2.4.3)
            side.TabIndex = 0; top.TabIndex = 1; restartBar.TabIndex = 2; content.TabIndex = 3; details.TabIndex = 4; bottom.TabIndex = 5; strip.TabStop = false;
            side.AccessibleName = "Pages"; side.AccessibleRole = AccessibleRole.PageTabList; content.AccessibleName = "Page content"; details.AccessibleName = "Tweak details"; bottom.AccessibleName = "Apply bar"; Theme.NativeTheme(this);
            KeyPreview = true;
            Chrome.Hook(this);
            // coming back to the window: settings may have changed outside the app (Control Panel, a driver update…).
            // Re-read states at most every 30 s; rows keep showing the last known state while that happens.
            Activated += delegate
            {
                if (busyCount > 0 || (DateTime.UtcNow - lastRefresh).TotalSeconds < 30) return;
                lastRefresh = DateTime.UtcNow; StateCache.Clear(); RefreshAll();
            };
            Chrome.Polish(this);
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreference;   // static event: removed on close
            FormClosed += delegate { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreference; };

            Shown += delegate { ThreadPool.QueueUserWorkItem(delegate { Tuner.Recover(); IrqTuner.Recover(); PowerTuner.Recover(); RescueBackfill.Run(); }); };
            Shown += delegate   // 3.22: after a switch to Lite, say what it hides
            {
                bool first = false;
                if (!modeChanged && Mode.Lite)
                    try { first = Reg.Get(Hive.HKCU, @"Software\ArchTweaker", "LiteNoticeShown") == null; if (first) using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\ArchTweaker")) k.SetValue("LiteNoticeShown", 1, Microsoft.Win32.RegistryValueKind.DWord); } catch { }
                if (!modeChanged && !(first && Mode.HiddenApplied() > 0)) return; modeChanged = false;
                if (Mode.Pro) { Notify(L.T("Pro mode is on. Every tweak and tool is shown. Read each tweak's details before you apply it."), Toast.Kind.Warn); return; }
                int h = Mode.HiddenApplied();
                Notify(h > 0 ? L.F("Lite mode hides {0} applied Pro tweak(s). They stay applied. Undo everything still covers them, or switch to Pro to see them.", h) : L.T("Lite mode is on. Only safe tweaks and tools are shown."), Toast.Kind.Ok);
            };   // 3.19: a tuning test cut short by a crash or close
            Shown += delegate { StartupTrace.Mark("window shown"); BeginInvoke((Action)(() => StartupTrace.FinishLater())); };   // 3.8: /trace
            Shown += delegate { if (AppearanceFlyout.Reopen) { AppearanceFlyout.Reopen = false; BeginInvoke((Action)OpenAppearance); } };   // 3.10.4: back to the flyout after a change rebuilt the window
            Shown += delegate
            {
                // opened on a monitor whose scale differs from the system scale: rebuild once at the right size
                QueueDpiRebuild(Theme.WindowDpi(this));
                UpdateCounts();   // every window, including one rebuilt for a language, scheme, compact or DPI change
                // 3.2: the advisor's picks for the "Recommended" badges, worked out once per session
                Recommend.Warm(() => { try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() => { RefreshAll(); if (detTweak == null && current != null && Catalog.Categories.Contains(current)) ShowCategorySummary(current); })); } catch (InvalidOperationException) { } });
                if (backupDone) { StartDriftWatch(); return; }   // 3.27.1: a rebuilt window takes over the drift watch
                if (SetupGuide.Due) BeginInvoke((Action)(() => RunSetupGuide()));
                // 3.17: What's new once after an update; a first start only records the version
                else if (WhatsNew.Due && Environment.GetEnvironmentVariable("ARCHTWEAKER_NO_WIZARD") != "1") BeginInvoke((Action)(() => { if (!IsDisposed) WhatsNew.Show(this); }));
                else WhatsNew.MarkSeen();
                // after a Windows feature update: which applied tweaks did it reset?
                UpdateWatch.Result ur = null;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    FullUndo.RemoveLegacyGameProfiles();
                    try { ur = UpdateWatch.Check(); } catch (Exception ex) { Log.W("Update check: {0}", ex.Message); }
                    if (ur == null || ur.Reset.Count == 0)
                    {
                        List<DriftItem> dg = null;   // 3.24: drift guard: a driver update or another program turned tweaks back
                        try { dg = DriftGuard.Check(); } catch (Exception ex) { Log.W("Drift guard: {0}", ex.Message); }
                        if (dg != null && dg.Count > 0) try { BeginInvoke((Action)(() => ShowDriftNotice(dg))); } catch (InvalidOperationException) { }
                        DriftWatch.Seen(dg);
                        StartDriftWatch();
                        return;
                    }
                    try { BeginInvoke((Action)(() => ShowUpdateNotice(ur))); } catch (InvalidOperationException) { }
                    DriftWatch.Seen(ur.Reset);   // 3.27.1: the update bar already shows these
                    StartDriftWatch();
                });
                backupDone = true;
                ThreadPool.QueueUserWorkItem(delegate { Probe.Warm(); });
                // 3.9: after network tweaks, check the network drivers for DPC spikes
                NetDpcCheck.RunLater(msg => { try { BeginInvoke((Action)(() => Notify(msg, Toast.Kind.Warn))); } catch (InvalidOperationException) { } });
                // 3.7: a restart A/B test continues after the restart
                RebootAB.Resume(
                    msg => { try { BeginInvoke((Action)(() => Log.Say(msg))); } catch (InvalidOperationException) { } },
                    (t, v, worse) => { try { BeginInvoke((Action)(() => ABTestPage.ShowResult(t, v, worse))); } catch (InvalidOperationException) { } },
                    () => { try { BeginInvoke((Action)(() => { if (Dlg.Confirm("Restart to finish the A/B test?", "ArchTweaker took the first measurement and applied the tweak. Restart now. ArchTweaker opens by itself after you sign in and takes the second measurement.", "Restart now")) Shell.Run("shutdown.exe", "/r /t 5", false); })); } catch (InvalidOperationException) { } });
                Log.W("ArchTweaker {0} on {1}. Journal entries: {2}", Program.Version, OSInfo.Name, Journal.Count);
                Busy("Backing up service configuration…", () => ServicesModule.AutoBackupIfNone(), null);
            };
            L.Apply(this);   // 3.0: interface language (no-op in English)
            if (Tray.Enabled) { try { tray = Tray.Create(this); } catch (Exception ex) { Log.W("Tray icon: {0}", ex.Message); } }
            ShowPage(startPage ?? "Home");
            UpdatePending();
        }
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public const string SEARCH = "Search results";

        readonly Dictionary<string, Func<Control>> factories = new Dictionary<string, Func<Control>>();

        static bool modeChanged;

        // Applied / available per tweak category, shown in the sidebar.
        // ---- feature-update notice
        InfoBar updBar; Label updText; static List<DriftItem> updReset;
        static bool updIsDrift;
        InfoBar outBar; Label outText; static readonly HashSet<string> outDismissed = new HashSet<string>();

        int countSeq;
        static DateTime lastRefresh = DateTime.UtcNow;
        bool suppressSearch;
        readonly HashSet<string> scaledPages = new HashSet<string>();

        // Per-monitor DPI (manifest: PerMonitorV2). .NET 4.5 WinForms doesn't rescale on its own, so a DPI change
        // takes the suggested bounds and rebuilds the window at the new scale, the same way a theme switch does.
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        bool dpiRebuildQueued;
        public bool RestartPending { get { return restartBar != null && restartBar.Visible || BootGuard.Armed; } }
        static IEnumerable<Control> All(Control c) { foreach (Control k in c.Controls) { yield return k; foreach (var d in All(k)) yield return d; } }

        // ---- 3.0: tray icon ----
        NotifyIcon tray; bool exiting;
        bool rebuildWhenShown;
        bool rebuilding;
        // 3.16: the details pane folds away when the window is too narrow for it and a readable tweak list
        // (sidebar + list of at least 560 px + pane); each row still opens in place
        bool detailsWanted;

        // ---- 3.2: restart summary (kept for the session, so a rebuild still shows it)
        static readonly List<string> rebootTitles = new List<string>();
        static bool restartDismissed;
        ToolTip restartTip;

        // ---- 3.2: log
        FlowLayoutPanel logBar; Action<LogStore.Entry> entryHandler;
        static bool guideOpen; bool rebuildAfterGuide;

        Toast toast;
        Button viewButton;

        Button detAction;

        int busyCount;
    }
}
