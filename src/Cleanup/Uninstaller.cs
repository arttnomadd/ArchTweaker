// ArchTweaker 1.9: classic Win32 program uninstaller with leftover scan.
// Enumerates the Uninstall registry hives, runs the vendor uninstaller, then finds and (reversibly)
// removes leftover folders and registry keys. UWP/Store apps are handled separately by AppsModule.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace ArchTweaker
{
    public class Program32
    {
        public string Key;              // full uninstall subkey path
        public Hive Hive;
        public string Name, Version, Publisher, InstallLocation, UninstallString, QuietUninstall, Icon;
        public long EstBytes;           // EstimatedSize is KB in registry
        public bool Msi;
        public string Display { get { return Name + (Version.Length > 0 ? "  " + Version : ""); } }
        public bool HasUninstall { get { return QuietUninstall.Length > 0 || UninstallString.Length > 0; } }
    }

    public class Leftover
    {
        public bool IsReg;
        public string Path;             // folder path, or "HIVE\subpath" for registry
        public Hive Hive;
        public string RegPath;
        public long Size;               // folders only
        public string Why;              // short reason it matched
        public bool Confident;          // high-confidence (recorded install folder) → pre-checked in the dialog
    }

    public static class Uninstaller
    {
        static readonly string[] Roots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        public static List<Program32> Load()
        {
            var list = new List<Program32>();
            foreach (var root in Roots) Scan(Hive.HKLM, root, list);
            Scan(Hive.HKCU, Roots[0], list);
            // de-dup by name+version (same product under HKLM 64/32)
            return list.GroupBy(p => (p.Name + "|" + p.Version).ToLowerInvariant()).Select(g => g.First())
                       .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static void Scan(Hive h, string root, List<Program32> outList)
        {
            foreach (var sub in Reg.SubKeys(h, root))
            {
                string k = root + "\\" + sub;
                string name = S(h, k, "DisplayName");
                if (name.Length == 0) continue;
                // skip system components, updates and patches
                object sc = Reg.Get(h, k, "SystemComponent"); if (sc is int && (int)sc == 1) continue;
                if (S(h, k, "ParentKeyName").Length > 0) continue;        // update/patch child entry
                string rt = S(h, k, "ReleaseType");
                if (rt == "Security Update" || rt == "Update Rollup" || rt == "Hotfix") continue;
                var p = new Program32
                {
                    Hive = h, Key = k, Name = name,
                    Version = S(h, k, "DisplayVersion"), Publisher = S(h, k, "Publisher"),
                    InstallLocation = S(h, k, "InstallLocation").Trim('"'),
                    UninstallString = S(h, k, "UninstallString"),
                    QuietUninstall = S(h, k, "QuietUninstallString"),
                    Icon = S(h, k, "DisplayIcon"),
                };
                object es = Reg.Get(h, k, "EstimatedSize"); if (es is int) p.EstBytes = (long)(int)es * 1024L;
                object wi = Reg.Get(h, k, "WindowsInstaller"); p.Msi = wi is int && (int)wi == 1;
                outList.Add(p);
            }
        }

        static string S(Hive h, string path, string name) { object o = Reg.Get(h, path, name); return o == null ? "" : o.ToString(); }

        static readonly System.Text.RegularExpressions.Regex MsiGuidRx =
            new System.Text.RegularExpressions.Regex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}");

        // Launch the vendor uninstaller. Returns the process exit code (0 = ok), or -1 if nothing could be launched.
        public static int Run(Program32 p, bool quiet)
        {
            string cmd = p.QuietUninstall.Length > 0 ? p.QuietUninstall : p.UninstallString;
            if (cmd.Length == 0) return -1;
            if (p.Msi && p.QuietUninstall.Length == 0)
            {
                // Only accept a strictly-formatted product GUID; never pass arbitrary registry text to msiexec.
                var m = MsiGuidRx.Match(cmd);
                if (!m.Success) { Log.W("Uninstall {0}: no valid MSI product code found", p.Name); return -1; }
                cmd = "msiexec.exe /x " + m.Value + (quiet ? " /qn /norestart" : " /qb");
            }
            Log.W("Uninstall {0}: {1}", p.Name, cmd);
            return SplitExec(cmd);
        }

        static int SplitExec(string cmd)
        {
            string file, args;
            ParseCommand(cmd, out file, out args);
            return Exec(file, args);
        }

        // Split a command string into file + args, handling quoted paths, unquoted paths that contain spaces,
        // and environment variables. Pure (no process launch) so it can be unit-tested.
        internal static void ParseCommand(string cmd, out string file, out string args)
        {
            cmd = (cmd ?? "").Trim();
            if (cmd.StartsWith("\""))
            {
                int e = cmd.IndexOf('"', 1);
                if (e < 0) { file = cmd.Substring(1); args = ""; }                 // unterminated quote: take the rest
                else { file = cmd.Substring(1, e - 1); args = cmd.Substring(e + 1).Trim(); }
            }
            else
            {
                // Try progressively longer prefixes ending at a space, picking the first that is an existing file.
                file = null; args = "";
                int from = 0;
                while (true)
                {
                    int sp = cmd.IndexOf(' ', from);
                    string cand = sp < 0 ? cmd : cmd.Substring(0, sp);
                    if (File.Exists(Environment.ExpandEnvironmentVariables(cand))) { file = cand; args = sp < 0 ? "" : cmd.Substring(sp + 1); break; }
                    if (sp < 0) break;
                    from = sp + 1;
                }
                if (file == null)   // nothing matched a real file: fall back to first token
                {
                    int sp = cmd.IndexOf(' ');
                    if (sp < 0) { file = cmd; args = ""; } else { file = cmd.Substring(0, sp); args = cmd.Substring(sp + 1); }
                }
            }
            file = Environment.ExpandEnvironmentVariables(file);
        }

        // Build the msiexec removal args for a product string, or null if no valid GUID (testable).
        internal static string MsiRemoveArgs(string cmd, bool quiet)
        {
            var m = MsiGuidRx.Match(cmd ?? "");
            if (!m.Success) return null;
            return "/x " + m.Value + (quiet ? " /qn /norestart" : " /qb");
        }

        static int Exec(string file, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(Shell.ResolveTool(file), args) { UseShellExecute = false };
                using (var proc = System.Diagnostics.Process.Start(psi)) { proc.WaitForExit(); return proc.ExitCode; }
            }
            catch (Exception ex) { Log.W("Uninstaller launch failed: {0} — {1}", file, ex.Message); return -1; }
        }

        // Re-read the Uninstall hives: is this product still registered?
        public static bool StillInstalled(Program32 p)
        {
            try { return Reg.KeyExists(p.Hive, p.Key) && S(p.Hive, p.Key, "DisplayName").Length > 0; }
            catch { return false; }
        }

        // ---------- leftover scan ----------
        static string Env(string v) { return Environment.ExpandEnvironmentVariables(v); }

        public static List<Leftover> ScanLeftovers(Program32 p)
        {
            var found = new List<Leftover>();

            // 1) the recorded install folder, if it survived — high confidence, pre-checked.
            if (p.InstallLocation.Length > 3 && Directory.Exists(p.InstallLocation) && !Protected(p.InstallLocation))
                found.Add(new Leftover { Path = p.InstallLocation, Size = DirSize(p.InstallLocation), Why = "install folder still present", Confident = true });

            // 2) data folders whose cleaned name EXACTLY equals the cleaned product name (never the publisher alone,
            //    never a prefix) — conservative, and left unchecked by default in the dialog.
            string target = Clean(p.Name);
            if (target.Length >= 4)
                foreach (var baseVar in new[] { "%LocalAppData%", "%AppData%", "%ProgramData%", "%ProgramFiles%", "%ProgramFiles(x86)%" })
                {
                    string bdir = Env(baseVar); if (!Directory.Exists(bdir)) continue;
                    string[] subs; try { subs = Directory.GetDirectories(bdir); } catch { continue; }
                    foreach (var d in subs)
                    {
                        if (Protected(d)) continue;
                        if (Clean(System.IO.Path.GetFileName(d)) != target) continue;   // exact match only
                        if (found.Any(x => x.Path != null && x.Path.Equals(d, StringComparison.OrdinalIgnoreCase))) continue;
                        found.Add(new Leftover { Path = d, Size = DirSize(d), Why = "data folder \"" + System.IO.Path.GetFileName(d) + "\"", Confident = false });
                    }
                }

            // 3) registry branch, only the exact Publisher\Product key (never the whole publisher branch).
            if (p.Publisher.Length > 0 && Clean(p.Publisher).Length > 1 && p.Name.Length > 1
                && p.Publisher.IndexOfAny(new[] { '\n', '\r' }) < 0 && p.Name.IndexOfAny(new[] { '\n', '\r' }) < 0)
            {
                string cand = p.Publisher + "\\" + p.Name;
                foreach (var h in new[] { Hive.HKCU, Hive.HKLM })
                    foreach (var baseP in new[] { @"SOFTWARE", @"SOFTWARE\WOW6432Node" })
                    {
                        string rp = baseP + "\\" + cand;
                        if (Reg.KeyExists(h, rp) && !SafeLists.RegistryBlocked(rp) && !found.Any(x => x.IsReg && x.Hive == h && x.RegPath.Equals(rp, StringComparison.OrdinalIgnoreCase)))
                            found.Add(new Leftover { IsReg = true, Hive = h, RegPath = rp, Why = "registry branch", Confident = false });
                    }
            }
            return found;
        }

        static string Clean(string s) { return new string((s ?? "").ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch)).ToArray()); }

        static readonly string[] ProtectedRoots = BuildProtected();
        static readonly string[] SharedLeaves = { "common files", "microsoft", "windows", "windowsapps", "packages", "temp", "internet explorer", "windows defender", "windows nt", "windows mail", "windows photo viewer", "windows sidebar", "windows media player" };
        static string[] BuildProtected()
        {
            var g = new List<string>();
            foreach (var sf in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.System,
                                       Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                                       Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile,
                                       Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
                                       Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.DesktopDirectory })
                try { g.Add(Environment.GetFolderPath(sf).TrimEnd('\\').ToLowerInvariant()); } catch { }
            try { g.Add((Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public").TrimEnd('\\').ToLowerInvariant()); } catch { }
            return g.ToArray();
        }
        static bool Protected(string dir)
        {
            string d;
            try { d = Path.GetFullPath(dir).TrimEnd('\\').ToLowerInvariant(); } catch { return true; }
            if (d.Length < 4) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(d, @"^[a-z]:\\?$")) return true;         // drive root
            try { if ((new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) != 0) return true; } catch { return true; }  // junction/symlink
            if (ProtectedRoots.Any(r => d == r)) return true;                                          // exactly a protected root
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\').ToLowerInvariant();
            if (d == win || d.StartsWith(win + "\\", StringComparison.Ordinal)) return true;           // anything under Windows
            string leaf = System.IO.Path.GetFileName(d);
            if (SharedLeaves.Any(s => string.Equals(s, leaf, StringComparison.OrdinalIgnoreCase))) return true;   // shared vendor/runtime folders
            if (SafeLists.LeftoverBlocked(d)) return true;                                             // system / security-product / profile locations
            return false;
        }

        static long DirSize(string d) { long s = 0; try { foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories)) try { s += new FileInfo(f).Length; } catch { } } catch { } return s; }

        // Reversible removal: folders are moved into the backup area; registry keys are exported+deleted via the journal.
        public static string Backup { get { string p = System.IO.Path.Combine(Paths.Root, @"backup\uninstall\" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(p); return p; } }

        public static long RemoveLeftovers(IEnumerable<Leftover> items) { int f; return RemoveLeftovers(items, out f); }

        public static long RemoveLeftovers(IEnumerable<Leftover> items, out int failed)
        {
            long freed = 0; int fail = 0; string bk = Backup;
            var manifest = new List<string>();
            foreach (var lo in items)
            {
                try
                {
                    if (lo.IsReg) { Reg.DeleteTreeBackedUp(lo.Hive, lo.RegPath); Log.W("Removed registry leftover {0}\\{1}", lo.Hive, lo.RegPath); manifest.Add("REG\t" + lo.Hive + "\\" + lo.RegPath); }
                    else if (Directory.Exists(lo.Path) && !Protected(lo.Path))
                    {
                        string dest = System.IO.Path.Combine(bk, System.IO.Path.GetFileName(lo.Path.TrimEnd('\\')));
                        if (Directory.Exists(dest)) dest += "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                        // Directory.Move fails across volumes — fall back to copy+delete so leftovers on another drive still back up.
                        if (string.Equals(Path.GetPathRoot(lo.Path), Path.GetPathRoot(dest), StringComparison.OrdinalIgnoreCase))
                            Directory.Move(lo.Path, dest);
                        else { CopyTree(lo.Path, dest); DeleteTree(lo.Path); }
                        freed += lo.Size; manifest.Add("DIR\t" + dest + "\t<= " + lo.Path);
                        Log.W("Moved leftover folder {0} -> {1}", lo.Path, dest);
                    }
                }
                catch (Exception ex) { fail++; Log.W("Leftover removal failed for {0}: {1}", lo.IsReg ? lo.RegPath : lo.Path, ex.Message); }
            }
            try { if (manifest.Count > 0) File.WriteAllLines(System.IO.Path.Combine(bk, "_restore-manifest.txt"), manifest.ToArray()); } catch { }
            failed = fail;
            return freed;
        }

        static void CopyTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src)) if ((new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) == 0) CopyTree(d, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(d)));
        }
        static void DeleteTree(string d) { try { Directory.Delete(d, true); } catch { } }
    }
}
