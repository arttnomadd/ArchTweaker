// ArchTweaker: Startup trace (/trace), auto-resume after a restart, and SeDebugPrivilege.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- startup timeline ----------------
    public static class StartupTrace
    {
        static Stopwatch sw; static List<string> lines; static bool exitAfter; static int ui;
        public static bool On { get { return sw != null; } }

        public static void Begin(bool exit)
        {
            sw = Stopwatch.StartNew(); lines = new List<string>(); exitAfter = exit; ui = Thread.CurrentThread.ManagedThreadId;
            double before = -1;
            try { before = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds; } catch { }
            Add("ArchTweaker " + Program.Version + " startup trace · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · " + OSInfo.Name);
            Add("time is from Main(); process start → Main() took " + (before >= 0 ? before.ToString("0") + " ms (CLR start + JIT)" : "?"));
            Add("");
            Shell.TraceDone = (file, args, ms) =>
                Add(string.Format(CultureInfo.InvariantCulture, "{0,7} ms  {1} {2} {3}  ({4} ms)", sw == null ? 0 : sw.ElapsedMilliseconds, Thread.CurrentThread.ManagedThreadId == ui ? "UI" : "bg", Path.GetFileName(file), Cut(args, 60), ms));
        }
        public static void Mark(string what) { if (sw != null) Add(string.Format(CultureInfo.InvariantCulture, "{0,7} ms  ── {1}", sw.ElapsedMilliseconds, what)); }
        static void Add(string s) { var l = lines; if (l != null) lock (l) l.Add(s); }
        static string Cut(string s, int n) { s = s ?? ""; return s.Length > n ? s.Substring(0, n) + "…" : s; }

        // called once the window has painted: writes the file 15 s later (background state reads included)
        public static void FinishLater()
        {
            if (sw == null) return;
            Mark("first paint");
            var t = new System.Windows.Forms.Timer { Interval = 15000 };
            t.Tick += delegate
            {
                t.Stop(); t.Dispose();
                Mark("+15 s");
                string file = Path.Combine(Paths.Root, "trace-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                try { lock (lines) File.WriteAllLines(file, lines.ToArray()); Log.W("Startup trace: {0}", file); } catch (Exception ex) { Log.W("Startup trace: {0}", ex.Message); }
                Shell.TraceDone = null; sw = null;
                if (exitAfter) Application.Exit();
            };
            t.Start();
        }
    }

    // ---------------- open ArchTweaker after the next sign-in ----------------
    public static class AutoResume
    {
        const string Task = "ArchTweaker resume";
        static string TaskFile { get { return Path.Combine(Environment.SystemDirectory, "Tasks", Task); } }
        public static bool Armed { get { return File.Exists(TaskFile); } }

        // one-shot task: at the next sign-in of this user, run ArchTweaker elevated with /resume
        public static bool Arm()
        {
            string exe = Application.ExecutablePath;
            string user = Environment.UserDomainName + "\\" + Environment.UserName;
            Shell.Run("schtasks.exe", "/Create /TN \"" + Task + "\" /TR \"\\\"" + exe + "\\\" /resume\" /SC ONLOGON /RU \"" + user + "\" /IT /RL HIGHEST /F", true, 15000);
            bool ok = Armed;
            Log.W(ok ? "ArchTweaker will open by itself after the restart." : "Couldn't schedule ArchTweaker to open after the restart: open it yourself.");
            return ok;
        }
        public static void Disarm() { if (Armed) Shell.Run("schtasks.exe", "/Delete /TN \"" + Task + "\" /F", true, 15000); }
    }

    // ---------------- SeDebugPrivilege ----------------
    public static class Privilege
    {
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string system, string name, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int len, IntPtr prev, IntPtr retLen);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attributes; }
        const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x8; const int SE_PRIVILEGE_ENABLED = 2;

        // true when the privilege is now enabled for this process
        public static bool Enable(string name)
        {
            IntPtr tok = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tok)) return false;
                long luid; if (!LookupPrivilegeValue(null, name, out luid)) return false;
                var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                return AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
            }
            catch { return false; }
            finally { if (tok != IntPtr.Zero) CloseHandle(tok); }
        }
    }
}
