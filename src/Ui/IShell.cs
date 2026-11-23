// 3.42: what a page asks of the main window. Pages call AppShell.Current instead of reaching into MainForm,
// so a page depends on these few members only, and a unit test can put a fake shell in place of the window.
using System;
using System.Collections.Generic;

namespace ArchTweaker
{
    public interface IShell
    {
        // Runs work off the UI thread with a status line, then done on the UI thread. Busy locks the pages
        // (for changes to the system); LoadData leaves them usable (for read-only page loads).
        void Busy(string msg, Action work, Action done);
        void Busy(string msg, Action work, Action done, bool lockPages);
        void LoadData(string msg, Action work, Action done);
        void Notify(string msg, Toast.Kind kind);
        void ShowPage(string name);
        void ShowDetails(Tweak t);
        void RefreshAll();
        void SelectIds(IEnumerable<string> ids);
        Dictionary<Tweak, bool> Pending { get; }
    }

    public static class AppShell
    {
        static IShell over;
        // The main window, or the shell a test put in place
        public static IShell Current { get { return over ?? MainForm.Instance; } }
        // Tests only: Use(fake) … Use(null) puts the main window back
        public static void Use(IShell shell) { over = shell; }
    }
}
