// ArchTweaker: Cleanup targets and the cleaner.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- CLEANUP ----------------
    public class CleanTarget
    {
        public string Title, Hint; public Func<IEnumerable<string>> Dirs; public string Pattern = "*";
        public Action Before, After; public bool Default; public long Size; public Func<long> CustomSize; public Action CustomClean;
    }

    public static class CleanupModule
    {
        static string Env(string v) { return Environment.ExpandEnvironmentVariables(v); }
        static IEnumerable<string> D(params string[] d) { return d.Select(Env); }

        static IEnumerable<string> BrowserCaches()
        {
            var l = new List<string>();
            foreach (var root in new[] { @"%LOCALAPPDATA%\Google\Chrome\User Data", @"%LOCALAPPDATA%\Microsoft\Edge\User Data", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data" })
            {
                string r = Env(root);
                if (!Directory.Exists(r)) continue;
                foreach (var prof in Directory.GetDirectories(r))
                {
                    l.Add(Path.Combine(prof, "Cache"));
                    l.Add(Path.Combine(prof, "Code Cache"));
                    l.Add(Path.Combine(prof, "GPUCache"));
                }
            }
            string ff = Env(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles");
            if (Directory.Exists(ff)) foreach (var p in Directory.GetDirectories(ff)) l.Add(Path.Combine(p, "cache2"));
            return l;
        }

        public static List<CleanTarget> Targets()
        {
            return new List<CleanTarget>
            {
                new CleanTarget { Title = "User temp files", Hint = "%TEMP% of the signed-in user, plus the admin account when elevated as another user", Dirs = UserTemps, Default = true },
                new CleanTarget { Title = "Windows temp files", Hint = @"C:\Windows\Temp", Dirs = () => D(@"%WINDIR%\Temp"), Default = true },
                new CleanTarget { Title = "Windows Update download cache", Hint = "SoftwareDistribution\\Download (stops wuauserv while cleaning)", Dirs = () => D(@"%WINDIR%\SoftwareDistribution\Download"), Default = true,
                    Before = () => { Shell.Run("net.exe", "stop wuauserv /y", true); Shell.Run("net.exe", "stop bits /y", true); },
                    After = () => { Shell.Run("net.exe", "start wuauserv", true); } },
                new CleanTarget { Title = "Delivery Optimization cache", Hint = "Peer-to-peer update cache", Dirs = () => D(@"%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"), Default = true },
                new CleanTarget { Title = "Crash dumps & error reports", Hint = "Minidumps, WER reports", Dirs = () => D(@"%LOCALAPPDATA%\CrashDumps", @"%WINDIR%\Minidump", @"%PROGRAMDATA%\Microsoft\Windows\WER\ReportArchive", @"%PROGRAMDATA%\Microsoft\Windows\WER\ReportQueue", @"%LOCALAPPDATA%\Microsoft\Windows\WER"), Default = true },
                new CleanTarget { Title = "Full memory dump (MEMORY.DMP)", Hint = "Can be many GB", Dirs = () => D(@"%WINDIR%"), Pattern = "MEMORY.DMP", Default = true },
                new CleanTarget { Title = "Thumbnail cache", Hint = "Rebuilt automatically", Dirs = () => D(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer"), Pattern = "thumbcache_*.db", Default = false },
                new CleanTarget { Title = "Old Windows logs", Hint = "CBS / DISM / setup logs", Dirs = () => D(@"%WINDIR%\Logs\CBS", @"%WINDIR%\Logs\DISM", @"%WINDIR%\Panther"), Pattern = "*.log", Default = true },
                new CleanTarget { Title = "Browser caches (Chrome / Edge / Brave / Firefox)", Hint = "Close browsers first. Logins and history stay.", Dirs = BrowserCaches, Default = false },
                new CleanTarget { Title = "GPU shader caches (DirectX / NVIDIA / AMD)", Hint = "Games rebuild them: expect stutter on the first launch after cleaning. Useful after a driver update.", Dirs = () => D(@"%LOCALAPPDATA%\D3DSCache", @"%LOCALAPPDATA%\NVIDIA\DXCache", @"%LOCALAPPDATA%\NVIDIA\GLCache", @"%LOCALAPPDATA%\AMD\DxCache", @"%LOCALAPPDATA%\AMD\DxcCache", @"%LOCALAPPDATA%\AMD\VkCache"), Default = false },
                new CleanTarget { Title = "Recycle Bin", Hint = "All drives", Default = true,
                    CustomSize = () => { var i = new Native.SHQUERYRBINFO(); i.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.SHQUERYRBINFO)); Native.SHQueryRecycleBin(null, ref i); return i.i64Size; },
                    CustomClean = () => Native.SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4) },
                new CleanTarget { Title = "Event logs", Hint = "Clears every Windows event log (wevtutil)", Default = false, CustomSize = () => 0,
                    CustomClean = () => { foreach (var lg in Shell.Run("wevtutil.exe", "el", true).Split('\n')) { var n = lg.Trim(); if (n.Length > 0) Shell.Run("wevtutil.exe", "cl \"" + n + "\"", true); } } },
            };
        }

        static IEnumerable<string> Files(string dir, string pattern)
        {
            var stack = new Stack<string>(); stack.Push(dir);
            while (stack.Count > 0)
            {
                string d = stack.Pop();
                string[] fs = null, ds = null;
                try { fs = Directory.GetFiles(d, pattern); } catch { }
                if (fs != null) foreach (var f in fs) if (!CleanExclude.Skip(f)) yield return f;
                if (pattern != "*") continue; // patterns are top-level only
                try { ds = Directory.GetDirectories(d); } catch { }
                if (ds != null) foreach (var s in ds)
                    {
                        try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                        if (CleanExclude.Skip(s)) continue;   // 3.0: folders the user excluded in Preview
                        stack.Push(s);
                    }
            }
        }

        public static long Measure(CleanTarget t)
        {
            if (t.CustomSize != null) return t.Size = t.CustomSize();
            long sum = 0;
            foreach (var d in t.Dirs()) if (Directory.Exists(d)) foreach (var f in Files(d, t.Pattern)) { try { sum += new FileInfo(f).Length; } catch { } }
            return t.Size = sum;
        }

        // %TEMP% of this process plus the signed-in desktop user. When ArchTweaker is elevated with a
        // different admin account (standard-user UAC prompt), %TEMP% points at the admin's profile,
        // so the user's own temp folder would never be touched. explorer.exe runs as the signed-in user.
        public static IEnumerable<string> UserTemps()
        {
            var l = new List<string>();
            Action<string> add = delegate (string p)
            {
                if (string.IsNullOrEmpty(p)) return;
                try { p = Path.GetFullPath(p).TrimEnd('\\', '/'); } catch { return; }
                if (p.Length > 3 && !l.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase))) l.Add(p);
            };
            add(Env("%TEMP%"));
            try { add(Path.GetTempPath()); } catch { }
            foreach (var prof in InteractiveProfiles()) add(Path.Combine(prof, @"AppData\Local\Temp"));
            return l;
        }

        static IEnumerable<string> InteractiveProfiles()
        {
            var l = new List<string>();
            System.Diagnostics.Process[] ps;
            try { ps = System.Diagnostics.Process.GetProcessesByName("explorer"); } catch { return l; }
            foreach (var p in ps)
            {
                IntPtr tok = IntPtr.Zero;
                try
                {
                    if (!TokenApi.OpenProcessToken(p.Handle, 0x0008 /* TOKEN_QUERY */, out tok)) continue;
                    string sid;
                    using (var id = new System.Security.Principal.WindowsIdentity(tok)) sid = id.User == null ? null : id.User.Value;
                    if (sid == null) continue;
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid))
                    {
                        var path = k == null ? null : k.GetValue("ProfileImagePath") as string;
                        if (!string.IsNullOrEmpty(path)) l.Add(Env(path));
                    }
                }
                catch { }
                finally { if (tok != IntPtr.Zero) TokenApi.CloseHandle(tok); p.Dispose(); }
            }
            return l;
        }

        static class TokenApi
        {
            [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
            public static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
            [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool CloseHandle(IntPtr h);
        }

        public static long Clean(CleanTarget t) { int sk; return Clean(t, out sk); }

        // Deletes what it can. Files held open by a running program are skipped and counted
        // in `skipped`, so the page can say why some space is still in use.
        public static long Clean(CleanTarget t, out int skipped)
        {
            skipped = 0;
            long before = Measure(t);
            if (t.CustomClean != null) { t.CustomClean(); Log.W("Cleaned: {0}", t.Title); return before; }
            if (t.Before != null) t.Before();
            long freed = 0; int deleted = 0;
            try
            {
                foreach (var d in t.Dirs())
                {
                    if (!Directory.Exists(d)) continue;
                    // 3.2.1: the real folder (through any junction in its path); each delete must land inside it
                    string real = Secure.RealDir(d);
                    if (real == null) continue;
                    foreach (var f in Files(d, t.Pattern).ToList())
                    {
                        long len = 0;
                        try { len = new FileInfo(f).Length; } catch { }
                        try { Secure.DeleteUnder(f, real); freed += len; deleted++; }
                        catch { skipped++; }
                    }
                    if (t.Pattern == "*")
                        foreach (var sd in SubDirs(d).OrderByDescending(x => x.Length))   // deepest first, so parents empty out
                            try { File.SetAttributes(sd, FileAttributes.Normal); Directory.Delete(sd, false); } catch { }
                }
            }
            finally { if (t.After != null) t.After(); }
            Log.W("Cleaned: {0} — {1} ({2} file(s) deleted{3})", t.Title, Fmt(freed), deleted, skipped > 0 ? ", " + skipped + " in use or locked" : "");
            return freed;
        }

        // Every folder under d, skipping links; one unreadable folder no longer hides all the others.
        static List<string> SubDirs(string d)
        {
            var l = new List<string>(); var stack = new Stack<string>(); stack.Push(d);
            while (stack.Count > 0)
            {
                string[] ds = null;
                try { ds = Directory.GetDirectories(stack.Pop()); } catch { }
                if (ds == null) continue;
                foreach (var s in ds)
                {
                    try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    if (CleanExclude.Skip(s)) continue;
                    l.Add(s); stack.Push(s);
                }
            }
            return l;
        }

        public static string Fmt(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.00") + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0.0") + " MB";
            if (b >= 1L << 10) return (b / 1024.0).ToString("0") + " KB";
            return b + " B";
        }
    }
}
