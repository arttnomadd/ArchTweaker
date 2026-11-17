// ArchTweaker main window: Appearance: theme, scheme, accent, progress colour, density, text size, language and DPI rebuilds.
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
        protected override void WndProc(ref Message m)
        {
            const int WM_DPICHANGED = 0x02E0, WM_QUERYENDSESSION = 0x0011;
            // 3.30: answer "not yet" while a change runs, so Windows shows the block reason (see ShutdownBlock)
            if (m.Msg == WM_QUERYENDSESSION && busyCount > 0) { Log.W("Windows asked to shut down while ArchTweaker was busy: asked it to wait."); m.Result = IntPtr.Zero; return; }
            if (m.Msg == WM_DPICHANGED)
            {
                int dpi = (int)((long)m.WParam & 0xFFFF);
                var r = (RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(RECT));
                Bounds = new Rectangle(r.L, r.T, r.R - r.L, r.B - r.T);
                QueueDpiRebuild(dpi);
                m.Result = IntPtr.Zero; return;
            }
            base.WndProc(ref m);
        }

        void QueueDpiRebuild(int dpi)
        {
            if (dpi < 48 || dpi > 960 || dpi == Theme.Dpi || dpiRebuildQueued) return;
            if (busyCount > 0)
            {   // never rebuild mid-apply: retry once the work is done (pending selections are kept either way)
                var t = new System.Windows.Forms.Timer { Interval = 500 };
                t.Tick += delegate { if (busyCount == 0) { t.Stop(); t.Dispose(); QueueDpiRebuild(dpi); } };
                t.Start(); return;
            }
            dpiRebuildQueued = true;
            Log.Say("Display scale changed: {0}% → {1}%", Theme.Dpi * 100 / 96, dpi * 100 / 96);
            Theme.SetDpi(dpi);
            BeginInvoke((Action)Rebuild);
        }


        public void SetAccent(int i)
        {
            if (busyCount > 0 || i == Theme.AccentIndex) return;
            Theme.AccentIndex = i; Theme.Set(Theme.Dark); Theme.Save(); Rebuild();
        }

        public void SetLanguage(string code)
        {
            if (busyCount > 0 || code == L.Lang) return;
            L.Save(code); L.Use(code); Rebuild();
        }

        public void OpenAppearance()
        {
            if (busyCount > 0 || viewButton == null || viewButton.IsDisposed) return;
            var fl = new AppearanceFlyout(this);
            L.Apply(fl.Items);
            fl.Show(viewButton, new Point(viewButton.Width - fl.PreferredSize.Width, viewButton.Height + Theme.S(4)));
        }

        void ShowViewMenu()
        {
            if (viewButton == null || viewButton.IsDisposed) return;
            ViewMenu.Build(this, OpenAppearance).Show(viewButton, new Point(0, viewButton.Height));
        }

        public void SetScheme(int i)
        {
            if (busyCount > 0 || i == Theme.Scheme) return;
            // 3.37: a scheme sets colours only; the accent is what the accent picker says
            Theme.SetScheme(i); Theme.Save(); Rebuild();
        }

        // 3.10.2: custom colour scheme from one background colour
        public void PickCustomScheme()
        {
            if (busyCount > 0) return;
            using (var cd = new ColorDialog { Color = Theme.SchemeCustom, FullOpen = true, AnyColor = true })
            {
                if (cd.ShowDialog(this) != DialogResult.OK) { Rebuild(); return; }   // the scheme list goes back to the current scheme
                Theme.SchemeCustom = cd.Color;
                Theme.SetScheme(Theme.CustomScheme); Theme.Save();
                if (AccentMaker.Hex(Theme.SchemeCustomUsed) != AccentMaker.Hex(cd.Color)) Log.Say("ArchTweaker adjusted the background colour slightly so text stays readable.");
                Rebuild();
            }
        }

        // 3.10: colour of the progress bars
        public void SetProgress(int i)
        {
            if (busyCount > 0 || i == Theme.ProgressIndex || i < 0 || i >= Theme.ProgressNames.Length) return;
            Theme.ProgressIndex = i; Theme.Set(Theme.Dark); Theme.Save(); Rebuild();
        }

        public void PickCustomProgress()
        {
            if (busyCount > 0) return;
            using (var cd = new ColorDialog { Color = Theme.ProgressCustom, FullOpen = true, AnyColor = true })
            {
                if (cd.ShowDialog(this) != DialogResult.OK) return;
                Theme.ProgressCustom = cd.Color; Theme.ProgressIndex = Theme.ProgressNames.Length - 1;
                Theme.Set(Theme.Dark); Theme.Save();
                if (AccentMaker.Hex(Theme.Progress) != AccentMaker.Hex(cd.Color)) Log.Say("ArchTweaker adjusted the colour slightly so the bars stay visible.");
                Rebuild();
            }
        }

        public void PickCustomAccent()
        {
            if (busyCount > 0) return;
            using (var cd = new ColorDialog { Color = Theme.CustomColor, FullOpen = true, AnyColor = true })
            {
                if (cd.ShowDialog(this) != DialogResult.OK) return;
                Theme.CustomColor = cd.Color; Theme.AccentIndex = Theme.AccentNames.Length - 1;
                Theme.Set(Theme.Dark); Theme.Save();
                if (AccentMaker.Hex(Theme.Accent) != AccentMaker.Hex(cd.Color)) Log.Say("ArchTweaker adjusted the colour slightly so text and buttons stay readable.");
                Rebuild();
            }
        }

        public void SetCompact(bool on)
        {
            if (busyCount > 0) return;
            Theme.Compact = on; Theme.Save(); Rebuild();
        }

        public void SetSlim(bool on)
        {
            if (busyCount > 0) return;
            Theme.Slim = on; Theme.Save(); Rebuild();
        }

        public void SetTextSize(int i)
        {
            if (busyCount > 0 || i == Theme.TextSize) return;
            Theme.SetTextSize(i); Theme.Save(); Rebuild();
        }

        public void SetSort(int i)
        {
            TweakSort.Mode = i;
            foreach (var p in pages.Values.OfType<TweakPage>()) p.Resort();
        }

        public void SetFollowWindows(bool on)
        {
            if (busyCount > 0) return;
            Theme.FollowWindows = on;
            if (on) Theme.SetScheme(Theme.FollowScheme(Theme.Scheme));
            Theme.Save(); Rebuild();
        }

        // Windows changed light/dark or high contrast while we run: follow it
        void OnSystemPreference(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category != Microsoft.Win32.UserPreferenceCategory.General && e.Category != Microsoft.Win32.UserPreferenceCategory.Accessibility && e.Category != Microsoft.Win32.UserPreferenceCategory.Color) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (busyCount > 0 || rebuilding) return;
                    bool hc = false; try { hc = SystemInformation.HighContrast; } catch { }
                    bool schemeOff = Theme.FollowWindows && Theme.FollowScheme(Theme.Scheme) != Theme.Scheme;
                    if (hc == Theme.HighContrast && !schemeOff) return;
                    Theme.SetScheme(Theme.FollowWindows ? Theme.FollowScheme(Theme.Scheme) : Theme.Scheme);
                    Rebuild();
                }));
            }
            catch (InvalidOperationException) { }
        }


        public void SwitchTheme()
        {
            if (busyCount > 0) return;
            if (Theme.HighContrast) { Notify("A Windows high-contrast theme is on: its colours are used. Turn it off in Windows Settings → Accessibility → Contrast themes.", Toast.Kind.Warn); return; }
            Theme.FollowWindows = false;   // choosing a theme by hand stops following Windows
            Theme.Set(!Theme.Dark); Theme.Save();
            Rebuild();
        }


        // Runs the rebuild from a fresh message: a combo box or menu that asked for it may still be
        // finishing its own selection messages, and must not see its window destroyed underneath it.
        void Rebuild()
        {
            if (rebuilding) return;
            if (guideOpen) { rebuildAfterGuide = true; return; }        // never close the owner of the open setup guide
            if (!Visible && tray != null) { rebuildWhenShown = true; return; }   // hidden in the tray: rebuild when it comes back
            rebuilding = true;
            var t = new System.Windows.Forms.Timer { Interval = 30 };
            t.Tick += delegate
            {
                t.Stop(); t.Dispose();
                try { RebuildNow(); }
                catch (Exception ex)
                {
                    rebuilding = false;
                    Log.W("Rebuild failed: {0}", ex);
                    Dlg.Error("Couldn't refresh the window", "ArchTweaker saved the new setting and uses it at the next start.", ex.Message);
                }
            };
            t.Start();
        }


        void RebuildNow()
        {
            var nf = new MainForm(current);
            nf.StartPosition = FormStartPosition.Manual;
            nf.Bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            nf.WindowState = WindowState == FormWindowState.Minimized ? FormWindowState.Normal : WindowState;
            nf.logBox.SelectionStart = nf.logBox.Text.Length; nf.logBox.ScrollToCaret();   // 3.2: the log text comes from LogStore
            if (detTweak != null) nf.ShowDetails(detTweak);
            Program.Ctx.MainForm = nf;
            nf.Show();
            Close();
        }
    }
}
