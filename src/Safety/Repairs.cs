// ArchTweaker: Windows repairs added in 3.30: the ChkDsk → SFC → DISM → SFC chain with a time limit per step,
// WMI repository check and repair, the runtime installer (winget) and named restore points.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace ArchTweaker
{
    public static class Repairs
    {
        static string Sys(string exe) { return Units.Sys(exe); }

        // Runs a System32 tool hidden; returns the exit code, or null when it overran and was stopped.
        // 3.35.1: Shell.RunLines does the work; output = every output line (LastLines picks the end), or the start error
        static int? RunTool(string path, string args, int timeoutMs, out string output)
        {
            var all = new System.Text.StringBuilder();
            string tail; bool started;
            int? code = Shell.RunLines(path, args, timeoutMs, l => { lock (all) { if (all.Length < 1000000) all.Append(l).Append('\n'); } }, true, out tail, out started);   // true: DISM leaves DismHost.exe children
            lock (all) output = !started ? tail : all.ToString();
            return code;
        }
        static string LastLines(string s, int n)
        {
            var lines = (s ?? "").Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.All(c => c == '.' || c == '%' || char.IsDigit(c) || c == ' ' || c == '[' || c == ']' || c == '=')).ToList();
            return string.Join(" / ", lines.Skip(Math.Max(0, lines.Count - n)));
        }

        public class Step { public string Name, Exe, Args; public int Minutes; }
        public static List<Step> ChainSteps()
        {
            string drive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
            return new List<Step>
            {
                new Step { Name = "ChkDsk online scan", Exe = Sys("chkdsk.exe"), Args = drive + " /scan", Minutes = 45 },
                new Step { Name = "SFC (first pass)", Exe = Sys("sfc.exe"), Args = "/scannow", Minutes = 60 },
                new Step { Name = "DISM RestoreHealth", Exe = Sys("dism.exe"), Args = "/Online /Cleanup-Image /RestoreHealth", Minutes = 90 },
                new Step { Name = "SFC (second pass)", Exe = Sys("sfc.exe"), Args = "/scannow", Minutes = 60 },
            };
        }

        // 'progress' gets each step's name as it starts. Returns one line per step.
        public static List<string> RunChain(Action<string> progress)
        {
            var res = new List<string>();
            var steps = ChainSteps();
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                if (progress != null) progress(L.F("Repair step {0} of {1}: {2}…", i + 1, steps.Count, L.T(s.Name)));
                Log.W("Repair: {0} ({1} {2}), limit {3} min", s.Name, Path.GetFileName(s.Exe), s.Args, s.Minutes);
                string o; var sw = Stopwatch.StartNew();
                int? code = RunTool(s.Exe, s.Args, s.Minutes * 60000, out o);
                string line = code == null
                    ? L.F("{0}: stopped after {1} min with no result, skipped", L.T(s.Name), s.Minutes)
                    : L.F("{0}: exit code {1} after {2:0} min", L.T(s.Name), code.Value, sw.Elapsed.TotalMinutes) + (o.Length > 0 ? " — " + LastLines(o, 2) : "");
                Log.W("Repair: {0}", line);
                res.Add(line);
            }
            return res;
        }

        // winmgmt /verifyrepository answers 0 when the repository is consistent. Salvage keeps what it can and
        // rebuilds the rest; reset is never used, because it throws away every class that apps registered.
        public static string RepairWmi()
        {
            string wm = Path.Combine(Environment.SystemDirectory, @"wbem\winmgmt.exe"), o;
            int? v = RunTool(wm, "/verifyrepository", 120000, out o);
            Log.W("WMI verify: exit {0} {1}", v, LastLines(o, 1));
            if (v == 0) return L.T("The WMI repository is consistent. Nothing to repair.");
            int? s = RunTool(wm, "/salvagerepository", 300000, out o);
            Log.W("WMI salvage: exit {0} {1}", s, LastLines(o, 1));
            int? v2 = RunTool(wm, "/verifyrepository", 120000, out o);
            Log.W("WMI verify after salvage: exit {0} {1}", v2, LastLines(o, 1));
            return v2 == 0 ? L.T("The WMI repository was inconsistent. ArchTweaker repaired it. Restart Windows to finish.")
                           : L.T("The WMI repository is still inconsistent after the repair. Details are in the log.");
        }

        // Runtimes that games and apps expect. winget ids from the winget-pkgs catalog.
        public static readonly string[] Runtimes =
        {
            "Microsoft.VCRedist.2005.x86", "Microsoft.VCRedist.2005.x64", "Microsoft.VCRedist.2008.x86", "Microsoft.VCRedist.2008.x64",
            "Microsoft.VCRedist.2010.x86", "Microsoft.VCRedist.2010.x64", "Microsoft.VCRedist.2012.x86", "Microsoft.VCRedist.2012.x64",
            "Microsoft.VCRedist.2013.x86", "Microsoft.VCRedist.2013.x64", "Microsoft.VCRedist.2015+.x86", "Microsoft.VCRedist.2015+.x64",
            "Microsoft.DirectX", "Microsoft.DotNet.DesktopRuntime.8", "Microsoft.DotNet.DesktopRuntime.10",
        };
        public static string Winget()
        {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
            return File.Exists(p) ? p : null;
        }
        public static string RuntimeScript(string winget)
        {
            var sb = new StringBuilder("title ArchTweaker: installing runtimes");
            foreach (var id in Runtimes)
                sb.Append(" & echo. & echo ==== ").Append(id).Append(" & \"").Append(winget).Append("\" install --id ").Append(id)
                  .Append(" -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity");
            sb.Append(" & echo. & echo Done. Already installed runtimes show as 'No newer package versions' or 'already installed'.");
            return sb.ToString();
        }
        // false when winget is missing
        public static bool InstallRuntimes()
        {
            string w = Winget();
            if (w == null) return false;
            Log.W("Runtimes: installing {0} packages with winget.", Runtimes.Length);
            Shell.OpenVisible(Sys("cmd.exe"), "/k " + RuntimeScript(w));
            return true;
        }

        // A restore point with your own name. Turns System Protection on first when it is off (that also sizes its storage).
        public static string CleanName(string name)
        {
            var s = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (s.Length > 64) s = s.Substring(0, 64);
            return s.Length == 0 ? "ArchTweaker" : s;
        }
        public static string NamedRestorePoint(string name)
        {
            string n = CleanName(name);
            if (RestoreCheck.Problem() != null)
            {
                string why;
                if (!RestoreCheck.Fix(out why)) return L.F("System Protection is off and could not be turned on: {0}", why ?? L.T("check the log"));
            }
            Reg.Set(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", Microsoft.Win32.RegistryValueKind.DWord, 0);
            string r = Shell.PS("try { Checkpoint-Computer -Description '" + n.Replace("'", "''") + "' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop; 'OK' } catch { $_.Exception.Message }").Trim();
            Log.W("Restore point \"{0}\": {1}", n, r);
            return r.EndsWith("OK", StringComparison.Ordinal) ? L.F("Restore point \"{0}\" created.", n) : L.F("Windows did not create the restore point: {0}", r);
        }
    }

}
