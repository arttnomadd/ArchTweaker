// ArchTweaker: running processes: Shell.Run, PS, OpenVisible, tool allowlist (3.32 split from Engine.cs).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class Shell
    {
        // Known Windows tools: always resolve to System32 so a planted exe in a user-writable
        // app/current directory can't be launched in their place (we run elevated).
        static readonly HashSet<string> SysTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "reg.exe","schtasks.exe","powershell.exe","cmd.exe","msiexec.exe","ipconfig.exe","netsh.exe",
            "bcdedit.exe","powercfg.exe","shutdown.exe","sfc.exe","dism.exe","defrag.exe","wsreset.exe",
            "lodctr.exe","pnputil.exe","tasklist.exe","taskkill.exe","fsutil.exe","sc.exe","wmic.exe"
        };
        public static string ResolveTool(string file)
        {
            if (string.IsNullOrEmpty(file)) return file;
            if (file.IndexOf('\\') >= 0 || file.IndexOf('/') >= 0) return file;   // already a path
            // 3.19: powershell.exe lives in System32\WindowsPowerShell\v1.0, so the System32 check below missed it and
            // the bare name was resolved through the app folder and PATH
            if (file.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                if (File.Exists(ps)) return ps;
            }
            if (SysTools.Contains(file))
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), file);
                if (File.Exists(p)) return p;
            }
            return file;
        }

        public static string Run(string file, string args, bool wait) { return Run(file, args, wait, 0); }

        // timeoutMs > 0 kills the process if it overruns (returns whatever was captured).
        public static Action<string, string> Trace;   // 3.6.4: profiling hook (file, args); null in normal runs
        public static Action<string, string, long> TraceDone;   // 3.8: /trace (file, args, ms)
        public static string Run(string file, string args, bool wait, int timeoutMs)
        {
            var tr = Trace; if (tr != null) tr(file, args);
            var td = TraceDone; var tsw = td != null ? Stopwatch.StartNew() : null;
            try { return RunCore(file, args, wait, timeoutMs); }
            finally { if (td != null) td(file, args, tsw.ElapsedMilliseconds); }
        }
        static string RunCore(string file, string args, bool wait, int timeoutMs)
        {
            try
            {
                var psi = HiddenInfo(file, args);
                using (var p = Process.Start(psi))
                {
                    if (!wait) return "";
                    var errTask = p.StandardError.ReadToEndAsync();
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    if (timeoutMs > 0)
                    {
                        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } Log.W("Run timed out after {0}ms: {1}", timeoutMs, file); }
                    }
                    else p.WaitForExit();
                    string o = ""; string e = "";
                    try { o = outTask.Result; } catch { }
                    try { e = errTask.Result; } catch { }
                    return o + e;
                }
            }
            catch (Exception ex) { Log.W("Run failed: {0} {1} — {2}", file, args, ex.Message); return ""; }
        }

        // 3.35.1: the one way to start a hidden tool with its output redirected (Run, RunLines, LiveOverlay's PresentMon).
        // Bare System32 tool names resolve to System32 (ResolveTool), as before.
        public static ProcessStartInfo HiddenInfo(string file, string args)
        {
            return new ProcessStartInfo(ResolveTool(file), args ?? "")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }

        // 3.35.1: runs a tool, waits up to timeoutMs, passes each trimmed output line (stdout and stderr) to `line`.
        // Returns the exit code, or null when it overran and was stopped. started = false (code -1, the error in tail)
        // when it could not start, so a tool that really exits with -1 can be told apart.
        // tail = the last 6 output lines (or the start error), for a log line or a message. killTree also stops
        // the tool's child processes on a timeout (DISM's work runs in DismHost.exe children). Replaces the private
        // copies in IsoBake.Exec and Repairs.RunTool.
        public static int? RunLines(string file, string args, int timeoutMs, Action<string> line, bool killTree, out string tail)
        {
            bool started; return RunLines(file, args, timeoutMs, line, killTree, out tail, out started);
        }
        public static int? RunLines(string file, string args, int timeoutMs, Action<string> line, bool killTree, out string tail, out bool started)
        {
            tail = ""; started = false;
            if (timeoutMs <= 0) { Log.W("RunLines: no timeout given for {0}, using 10 min", file); timeoutMs = 600000; }
            var tr = Trace; if (tr != null) tr(file, args);
            var td = TraceDone; var tsw = td != null ? Stopwatch.StartNew() : null;
            var last = new List<string>();
            try
            {
                using (var p = new Process { StartInfo = HiddenInfo(file, args) })
                {
                    DataReceivedEventHandler h = (s, e) =>
                    {
                        if (string.IsNullOrEmpty(e.Data)) return;
                        string t = e.Data.Replace("\0", "").Trim(); if (t.Length == 0) return;
                        lock (last) { last.Add(t); if (last.Count > 6) last.RemoveAt(0); }
                        if (line != null) line(t);
                    };
                    p.OutputDataReceived += h; p.ErrorDataReceived += h;
                    p.Start(); started = true;
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        if (killTree) { try { Run("taskkill.exe", "/T /F /PID " + p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), true, 30000); } catch (Exception ex) { Log.W("RunLines: taskkill failed: {0}", ex.Message); } }
                        try { if (!p.HasExited) p.Kill(); p.WaitForExit(10000); } catch (Exception ex) { Log.W("RunLines: could not stop {0}: {1}", file, ex.Message); }
                        lock (last) tail = string.Join(" | ", last.ToArray());
                        Log.W("Run timed out after {0}ms: {1} (last output: {2})", timeoutMs, file, tail);
                        return null;
                    }
                    // the no-argument WaitForExit also waits for the last output lines, but a child that inherited the
                    // pipes (DismHost.exe) can hold them open for ever: wait for that at most 5 s
                    if (!System.Threading.Tasks.Task.Run(() => p.WaitForExit()).Wait(5000)) Log.W("RunLines: {0} exited, its output pipe stayed open; the last lines may be missing", file);
                    lock (last) tail = string.Join(" | ", last.ToArray());
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { Log.W("Run failed: {0} {1} — {2}", file, args, ex.Message); tail = ex.Message; return -1; }
            finally { if (td != null) td(file, args, tsw.ElapsedMilliseconds); }
        }

        public static string PS(string script)
        {
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ProgressPreference='SilentlyContinue';" + script));
            return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc, true);
        }

        public static void OpenVisible(string file, string args)
        {
            try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); }
            catch (Exception ex) { Log.W("Open failed: {0}", ex.Message); }
        }
    }
}
