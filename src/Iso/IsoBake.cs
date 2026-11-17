// ArchTweaker 3.19: bake tweaks into a Windows ISO.
//  - Copies the ISO's files to a work folder, turns install.esd into install.wim when needed, mounts each chosen
//    edition with DISM, loads its SOFTWARE, SYSTEM and default-user hives and writes the chosen tweaks' registry
//    values there. Then it commits the image.
//  - With oscdimg.exe (Windows ADK, or picked once) it writes a new bootable ISO (BIOS + UEFI). Without it, the
//    patched files stay in the folder, ready for Rufus or a USB stick.
//  - Only tweaks made purely of registry values, service start types and added keys qualify: an offline image
//    can't run commands, scheduled-task changes, power plans or device settings. Each tweak says why it's left out.
// Nothing on THIS PC changes: the writes go to the offline image's hives, not to the live registry, so they are
// not journaled. The original ISO is never written to.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    public class IsoEdition { public int Index; public string Name = ""; public override string ToString() { return Index + "  " + Name; } }

    public class IsoJob
    {
        public string Iso, OutDir; public List<IsoEdition> Editions = new List<IsoEdition>(); public List<Tweak> Tweaks = new List<Tweak>();
    }

    public class IsoBakeResult
    {
        public string Error, IsoPath, Folder; public int Values, Missing; public bool NeedsCleanup;
        public List<string> Notes = new List<string>();
    }

    public static class IsoBake
    {
        public const string SW = "AT_ISO_SOFTWARE", SYS = "AT_ISO_SYSTEM", USR = "AT_ISO_DEFUSER";
        static string Sys(string exe) { return Units.Sys(exe); }

        // ---------- pure helpers (unit-tested) ----------

        // Where a live-registry path lives in the loaded offline hives: a path under HKLM (users = false) or HKU (users = true). Null = not reachable offline.
        public static string OfflinePath(Hive h, string path, int controlSet, out bool users)
        {
            users = false; path = path ?? "";
            if (h == Hive.HKCU) { users = true; return USR + "\\" + path; }
            if (h == Hive.HKCR) return SW + @"\Classes\" + path;
            if (h != Hive.HKLM) return null;
            const string CCS = @"SYSTEM\CurrentControlSet\";
            if (path.StartsWith(CCS, StringComparison.OrdinalIgnoreCase)) return SYS + @"\ControlSet" + Math.Max(1, controlSet).ToString("000", CultureInfo.InvariantCulture) + "\\" + path.Substring(CCS.Length);
            if (path.StartsWith(@"SYSTEM\", StringComparison.OrdinalIgnoreCase)) return SYS + "\\" + path.Substring(7);
            if (path.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase)) return SW + "\\" + path.Substring(9);
            return null;
        }

        // Null when the tweak can be written into an offline image; otherwise the reason in plain words.
        public static string Unbakeable(Tweak t)
        {
            if (t == null) return "none";
            if (t.HasCommands) return "runs commands";
            if (t.ApplyExtra != null) return "uses a built-in action";
            if (t.Ops.Count == 0) return "has no registry values";
            foreach (var o in t.Ops)
            {
                bool u;
                var v = o as VOp; if (v != null) { if (OfflinePath(v.H, v.Path, 1, out u) == null) return "writes outside the image's hives"; continue; }
                var k = o as KeyAddOp; if (k != null) { if (OfflinePath(k.H, k.Path, 1, out u) == null) return "writes outside the image's hives"; continue; }
                if (o is SvcOp) continue;
                return "changes a " + o.Source;   // power plan, scheduled task, device, NVIDIA profile, key delete
            }
            return null;
        }

        // "2|Windows 11 Pro" lines from Get-WindowsImage
        public static List<IsoEdition> ParseEditions(string text)
        {
            var o = new List<IsoEdition>();
            foreach (var raw in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int bar = raw.IndexOf('|'); if (bar <= 0) continue;
                int i; if (!int.TryParse(raw.Substring(0, bar).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i) || i < 1) continue;
                o.Add(new IsoEdition { Index = i, Name = raw.Substring(bar + 1).Trim() });
            }
            return o;
        }

        public static string Label(string s)
        {
            var l = Regex.Replace(s ?? "", "[^A-Za-z0-9_]", "_").Trim('_');
            if (l.Length == 0) l = "ARCHTWEAKER_WIN";
            return l.Length > 32 ? l.Substring(0, 32) : l;
        }

        public static string OscdimgArgs(string src, string iso, string label, bool bios, bool uefi)
        {
            string s = src.TrimEnd('\\');
            string etfs = s + @"\boot\etfsboot.com", efi = s + @"\efi\microsoft\boot\efisys.bin";
            string boot = bios && uefi ? "-bootdata:2#p0,e,b\"" + etfs + "\"#pEF,e,b\"" + efi + "\""
                : uefi ? "-bootdata:1#pEF,e,b\"" + efi + "\"" : bios ? "-b\"" + etfs + "\"" : "";
            return "-m -o -u2 -udfver102 -l" + Label(label) + (boot.Length > 0 ? " " + boot : "") + " \"" + s + "\" \"" + iso + "\"";
        }

        // Paths reach command lines in quotes: a quote inside one could add arguments.
        public static bool SafePath(string p) { return !string.IsNullOrEmpty(p) && p.IndexOf('"') < 0 && p.IndexOfAny(Path.GetInvalidPathChars()) < 0; }

        // ---------- oscdimg ----------
        public static string BinPath { get { return Path.Combine(Paths.Root, @"bin\oscdimg.exe"); } }
        public static string FindOscdimg()
        {
            // known folders, not environment variables: the elevated process inherits the launcher's environment
            foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                string p = Path.Combine(pf, @"Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\amd64\Oscdimg\oscdimg.exe");
                if (Secure.TrustedData(p, "oscdimg in the Windows ADK folder")) return p;
            }
            return Secure.TrustedData(BinPath, "oscdimg in ArchTweaker's bin folder") ? BinPath : null;
        }
        public static string ImportOscdimg(string src)
        {
            try
            {
                if (!File.Exists(src)) return "The file does not exist.";
                var head = new byte[2];
                using (var fs = File.OpenRead(src)) if (fs.Read(head, 0, 2) != 2 || head[0] != 'M' || head[1] != 'Z') return "This file is not a program.";
                var v = FileVersionInfo.GetVersionInfo(src);
                string names = (v.OriginalFilename ?? "") + " " + (v.InternalName ?? "") + " " + Path.GetFileName(src);
                if (names.IndexOf("oscdimg", StringComparison.OrdinalIgnoreCase) < 0) return "This file is not oscdimg.exe.";
                Directory.CreateDirectory(Path.GetDirectoryName(BinPath));
                File.Copy(src, BinPath, true);
                Log.W("oscdimg copied to {0}", BinPath);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ---------- processes ----------
        // Runs a tool and passes each output line on. Returns the exit code (-1 = could not start or timed out).
        // 3.35.1: Shell.RunLines does the work; the whole process tree is stopped on a timeout (DismHost.exe children)
        static int Exec(string exe, string args, int timeoutMs, Action<string> line)
        {
            Log.W("  > {0} {1}", Path.GetFileName(exe), args);
            string tail; bool started;
            int? code = Shell.RunLines(exe, args, timeoutMs, line, true, out tail, out started);
            if (!started) { Log.W("  could not run {0}: {1}", exe, tail); return -1; }
            if (code == null) { Log.W("  timed out after {0} min: {1}", timeoutMs / 60000, tail); return -1; }
            if (code.Value != 0) Log.W("  exit {0}: {1}", code.Value, tail);
            return code.Value;
        }
        // A path as a PowerShell expression. Base64 instead of a quoted literal: PowerShell also ends a literal at
        // the curly quotes U+2018..U+201B, which a file name may contain.
        static string Ps1(string s) { return "([Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" + Convert.ToBase64String(Encoding.Unicode.GetBytes(s ?? "")) + "')))"; }

        // Mounts the ISO; returns its root ("E:\") or null. mountedHere = false when Windows had it mounted already.
        static string MountIso(string iso, out bool mountedHere)
        {
            mountedHere = false;
            // the mounted flag is written first, so a failed drive-letter lookup still says whether to dismount;
            // the letter can take a moment to appear, so it is asked for up to 10 times
            string o = Shell.PS("$d=Get-DiskImage -ImagePath " + Ps1(iso) + "; $m=$false; if(-not $d.Attached){$d=Mount-DiskImage -ImagePath " + Ps1(iso) + " -PassThru; $m=$true}; Write-Output ('ATM|'+$m); "
                + "$l=$null; for($i=0;$i -lt 10 -and -not $l;$i++){ Start-Sleep -Milliseconds 500; try{ $l=@(Get-DiskImage -ImagePath " + Ps1(iso) + " | Get-Volume | Where-Object {$_.DriveLetter} | Select-Object -First 1 -ExpandProperty DriveLetter)[0] }catch{} }; "
                + "Write-Output ('ATL|'+$l)");
            mountedHere = Regex.IsMatch(o ?? "", @"ATM\|True", RegexOptions.IgnoreCase);
            var m = Regex.Match(o ?? "", @"ATL\|([A-Za-z])\b");
            if (!m.Success)
            {
                Log.W("Mounting the ISO failed: {0}", (o ?? "").Trim());
                if (mountedHere) { DismountIso(iso); mountedHere = false; }
                return null;
            }
            return m.Groups[1].Value.ToUpperInvariant() + @":\";
        }
        static void DismountIso(string iso) { Shell.PS("Dismount-DiskImage -ImagePath " + Ps1(iso) + " | Out-Null"); }

        static string ImageFile(string root)
        {
            foreach (var n in new[] { "install.wim", "install.esd" }) { string p = Path.Combine(root, "sources", n); if (File.Exists(p)) return p; }
            return null;
        }

        // Reads the editions of a Windows ISO. Null + error when it isn't one.
        public static List<IsoEdition> Inspect(string iso, out string error)
        {
            error = null;
            if (!SafePath(iso) || !File.Exists(iso)) { error = "Choose an existing .iso file."; return null; }
            bool here; string root = MountIso(iso, out here);
            if (root == null) { error = "Windows could not mount this ISO."; return null; }
            try
            {
                string img = ImageFile(root);
                if (img == null) { error = "This ISO has no sources\\install.wim or install.esd, so it is not a Windows setup ISO."; return null; }
                var eds = ParseEditions(Shell.PS("Get-WindowsImage -ImagePath " + Ps1(img) + " | ForEach-Object { Write-Output ([string]$_.ImageIndex + '|' + $_.ImageName) }"));
                if (eds.Count == 0) error = "DISM could not read the editions in " + Path.GetFileName(img) + ".";
                return eds;
            }
            finally { if (here) DismountIso(iso); }
        }

        // ---------- the bake ----------
        public static IsoBakeResult Run(IsoJob job, Action<string> say)
        {
            var r = new IsoBakeResult();
            if (!SafePath(job.Iso) || !SafePath(job.OutDir) || !File.Exists(job.Iso) || !Directory.Exists(job.OutDir)) { r.Error = "The ISO or the output folder is not valid."; return r; }
            if (job.Editions.Count == 0 || job.Tweaks.Count == 0) { r.Error = "Choose at least one edition and one tweak."; return r; }
            var bad = job.Tweaks.Where(t => Unbakeable(t) != null).ToList();
            if (bad.Count > 0) { r.Error = "These tweaks can't be baked: " + string.Join(", ", bad.Select(t => t.Title).ToArray()); return r; }

            if (!UnloadAll()) { r.Error = "Image hives from an earlier bake are still loaded. Restart Windows and try again."; return r; }   // left over if ArchTweaker was closed mid-bake
            DriveInfo drive;
            try { drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(job.OutDir))); } catch { r.Error = "The output folder is not on a local drive."; return r; }
            if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) { r.Error = "DISM needs an NTFS drive for the output folder (this one is " + drive.DriveFormat + ")."; return r; }
            long need = new FileInfo(job.Iso).Length * 3 + (2L << 30);
            if (drive.AvailableFreeSpace < need) { r.Error = L.F("The output drive needs about {0} GB free (it has {1} GB).", need >> 30, drive.AvailableFreeSpace >> 30); return r; }

            string stem = Path.GetFileNameWithoutExtension(job.Iso) + "-ArchTweaker";
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string work = Path.Combine(job.OutDir, stem), mount = Path.Combine(job.OutDir, stem + "-mount"), outIso = Path.Combine(job.OutDir, stem + ".iso");
            if (Directory.Exists(work) || Directory.Exists(mount) || File.Exists(outIso))
            {
                string sfx = "-" + stamp + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                work = Path.Combine(job.OutDir, stem + sfx); mount = Path.Combine(job.OutDir, stem + sfx + "-mount"); outIso = Path.Combine(job.OutDir, stem + sfx + ".iso");
            }
            // never merge into, or later delete, something that was already there
            if (Directory.Exists(work) || Directory.Exists(mount) || File.Exists(outIso)) { r.Error = "The output folder already has a folder or ISO with that name."; return r; }
            try { if ((File.GetAttributes(job.OutDir) & FileAttributes.ReparsePoint) != 0) { r.Error = "The output folder is a link to another folder. Choose a normal folder."; return r; } } catch { }
            r.Folder = work;
            Log.W("Bake into ISO: {0} → {1}; editions {2}; {3} tweak(s)", job.Iso, job.OutDir, string.Join(",", job.Editions.Select(e => e.Index.ToString(CultureInfo.InvariantCulture)).ToArray()), job.Tweaks.Count);

            // 1. copy the ISO's files
            say(L.T("Step 1 of 4: copying the ISO's files…"));
            bool here; string root = MountIso(job.Iso, out here);
            if (root == null) { r.Error = "Windows could not mount the ISO."; return r; }
            try
            {
                Directory.CreateDirectory(work); Protect(work);
                File.WriteAllText(Path.Combine(work, Marker), "made by ArchTweaker " + Program.Version);
                // "E:\." not "E:\": a backslash before the closing quote would escape the quote
                int rc = Exec(Sys("robocopy.exe"), "\"" + root.TrimEnd('\\') + "\\.\" \"" + work + "\" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XF " + Marker, 60 * 60000, null);
                if (rc < 0 || rc >= 8) { r.Error = "Copying the ISO's files failed (robocopy " + rc + ")."; return r; }
            }
            finally { if (here) DismountIso(job.Iso); }
            foreach (var f in Directory.GetFiles(work, "*", SearchOption.AllDirectories))
                try { var a = File.GetAttributes(f); if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly); } catch { }

            // 2. install.esd → install.wim (an ESD can't be mounted for writing)
            string img = ImageFile(work);
            if (img == null) { r.Error = "The copy has no sources\\install.wim or install.esd."; return r; }
            var targets = job.Editions.Select(e => e.Index).ToList();
            if (img.EndsWith(".esd", StringComparison.OrdinalIgnoreCase))
            {
                string wim = Path.Combine(Path.GetDirectoryName(img), "install.wim");
                for (int i = 0; i < job.Editions.Count; i++)
                {
                    say(L.F("Step 2 of 4: converting {0} to install.wim ({1} of {2})…", job.Editions[i].Name, i + 1, job.Editions.Count));
                    int rc = Exec(Sys("dism.exe"), "/English /Export-Image /SourceImageFile:\"" + img + "\" /SourceIndex:" + job.Editions[i].Index + " /DestinationImageFile:\"" + wim + "\" /Compress:max /CheckIntegrity", 90 * 60000, null);
                    if (rc != 0) { r.Error = "DISM could not convert install.esd (exit " + rc + "). Details are in the log."; return r; }
                }
                File.Delete(img); img = wim;
                targets = Enumerable.Range(1, job.Editions.Count).ToList();
                r.Notes.Add("install.esd was converted to install.wim. Only the chosen editions are in the new image.");
            }
            else r.Notes.Add("Editions you did not choose are still in the image, without the tweaks. Pick the tweaked edition during setup.");

            // 3. each edition: mount, write, commit
            Directory.CreateDirectory(mount); Protect(mount);
            var report = new StringBuilder("Tweaks baked in by ArchTweaker " + Program.Version + " on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "\r\n\r\n");
            for (int i = 0; i < targets.Count; i++)
            {
                var ed = job.Editions[i];
                say(L.F("Step 3 of 4: {0} ({1} of {2}): mounting…", ed.Name, i + 1, targets.Count));
                int rc = Exec(Sys("dism.exe"), "/English /Mount-Wim /WimFile:\"" + img + "\" /Index:" + targets[i] + " /MountDir:\"" + mount + "\"", 60 * 60000, null);
                if (rc != 0) { r.Error = "DISM could not mount " + ed.Name + " (exit " + rc + ")."; Discard(mount, r); return r; }
                string werr = null; int written = 0, missing = 0;
                try
                {
                    say(L.F("Step 3 of 4: {0} ({1} of {2}): writing {3} tweak(s)…", ed.Name, i + 1, targets.Count, job.Tweaks.Count));
                    werr = WriteImage(mount, job.Tweaks, out written, out missing);
                }
                catch (Exception ex) { werr = ex.Message; }
                if (werr != null) { r.Error = ed.Name + ": " + werr; Discard(mount, r); return r; }
                r.Values += written; r.Missing += missing;
                say(L.F("Step 3 of 4: {0} ({1} of {2}): saving the image…", ed.Name, i + 1, targets.Count));
                rc = Exec(Sys("dism.exe"), "/English /Unmount-Wim /MountDir:\"" + mount + "\" /Commit", 60 * 60000, null);
                if (rc != 0) { r.Error = "DISM could not save " + ed.Name + " (exit " + rc + ")."; Discard(mount, r); return r; }
                report.Append(ed.Name + ": " + written + " value(s)" + (missing > 0 ? ", " + missing + " service(s) not in this edition" : "") + "\r\n");
            }
            report.Append("\r\n");
            foreach (var t in job.Tweaks) report.Append("• " + t.Title + "  [" + t.Id + "]\r\n");
            try { File.WriteAllText(Path.Combine(work, "ArchTweaker-baked.txt"), report.ToString(), new UTF8Encoding(false)); } catch (Exception ex) { Log.W("Bake: ArchTweaker-baked.txt: {0}", ex.Message); }
            try { Directory.Delete(mount, false); } catch { }   // empty after the last unmount
            try { if (new FileInfo(img).Length > (4L << 30) - 1) r.Notes.Add("install.wim is over 4 GB. For a FAT32 USB stick use Rufus, which handles this."); } catch { }

            // 4. the ISO
            string osc = FindOscdimg();
            if (osc == null) { r.Notes.Add("oscdimg.exe not found, so no ISO was built. The folder is ready for Rufus or a USB stick."); return r; }
            say(L.T("Step 4 of 4: writing the new ISO…"));
            try { File.Delete(Path.Combine(work, Marker)); } catch { }   // not part of the ISO
            osc = FindOscdimg();   // checked again right before it runs elevated
            if (osc == null || File.Exists(outIso)) { r.Notes.Add("oscdimg.exe is no longer trusted, or the ISO name is taken, so no ISO was built. The folder is ready for Rufus."); return r; }
            bool bios = File.Exists(Path.Combine(work, @"boot\etfsboot.com")), uefi = File.Exists(Path.Combine(work, @"efi\microsoft\boot\efisys.bin"));
            int oc = Exec(osc, OscdimgArgs(work, outIso, Path.GetFileNameWithoutExtension(job.Iso), bios, uefi), 60 * 60000, null);
            if (oc != 0 && File.Exists(outIso)) try { File.Delete(outIso); } catch (Exception ex) { Log.W("Could not remove the half-written ISO: {0}", ex.Message); }   // this run's own file, name checked free above
            if (oc != 0 || !File.Exists(outIso)) { r.Notes.Add("oscdimg failed (exit " + oc + "), so no ISO was built. The folder is ready for Rufus or a USB stick."); return r; }
            r.IsoPath = outIso;
            if (!bios) r.Notes.Add("The source ISO has no BIOS boot file, so the new ISO boots on UEFI only.");
            // the folder was only a step towards the ISO, and ArchTweaker made it in this run (fresh name, checked above)
            try
            {
                if ((File.GetAttributes(work) & FileAttributes.ReparsePoint) == 0 && File.Exists(Path.Combine(work, "ArchTweaker-baked.txt")))
                { Directory.Delete(work, true); r.Folder = null; }
            }
            catch (Exception ex) { Log.W("Could not remove the work folder: {0}", ex.Message); }
            return r;
        }

        const string Marker = ".archtweaker-work";

        // Admins and SYSTEM full control, Users read: a standard user can't swap install.wim or boot files while
        // the elevated steps run, but can still read the result (for Rufus).
        static void Protect(string dir)
        {
            try
            {
                var all = System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit;
                var sec = new System.Security.AccessControl.DirectorySecurity();
                sec.SetAccessRuleProtection(true, false);
                foreach (var sid in new[] { System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, System.Security.Principal.WellKnownSidType.LocalSystemSid })
                    sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier(sid, null), System.Security.AccessControl.FileSystemRights.FullControl, all, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
                sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null), System.Security.AccessControl.FileSystemRights.ReadAndExecute, all, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
                sec.SetOwner(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null));
                Directory.SetAccessControl(dir, sec);
            }
            catch (Exception ex) { Log.W("Bake: could not lock {0} to administrators: {1}", dir, ex.Message); }
        }

        static void Discard(string mount, IsoBakeResult r)
        {
            UnloadAll();
            int rc = Exec(Sys("dism.exe"), "/English /Unmount-Wim /MountDir:\"" + mount + "\" /Discard", 30 * 60000, null);
            if (rc != 0) rc = Exec(Sys("dism.exe"), "/English /Cleanup-Mountpoints", 30 * 60000, null);
            if (rc != 0) { r.NeedsCleanup = true; r.Notes.Add("DISM could not unmount " + mount + ". Restart Windows, then run in an admin Command Prompt: dism /Cleanup-Mountpoints"); }
        }

        static bool Load(string name, string file)
        {
            if (!File.Exists(file)) return false;
            bool hu = name.StartsWith("HKU", StringComparison.OrdinalIgnoreCase);
            string root = hu ? name.Substring(4) : name.Substring(5);
            Func<bool> loaded = () =>
            {
                using (var b = RegistryKey.OpenBaseKey(hu ? RegistryHive.Users : RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = b.OpenSubKey(root)) return k != null;
            };
            if (loaded())
            {
                string u = Shell.Run(Sys("reg.exe"), "unload \"" + name + "\"", true, 30000) ?? "";   // a leftover from a crashed run
                if (loaded()) { Log.W("Bake: {0} is still loaded from an earlier run ({1}); restart Windows and try again.", name, u.Trim()); return false; }   // never write into a stale hive
            }
            string o = Shell.Run(Sys("reg.exe"), "load \"" + name + "\" \"" + file + "\"", true, 60000) ?? "";
            bool ok = loaded();
            if (!ok) Log.W("Bake: reg load {0} failed: {1}", name, o.Trim());
            return ok;
        }
        static bool Loaded(string n)
        {
            using (var b = RegistryKey.OpenBaseKey(n.StartsWith("HKU", StringComparison.Ordinal) ? RegistryHive.Users : RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = b.OpenSubKey(n.Substring(n.IndexOf('\\') + 1))) return k != null;
        }
        // True when none of the three image hives is loaded any more.
        static bool UnloadAll()
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            bool all = true;
            foreach (var n in new[] { @"HKLM\" + SW, @"HKLM\" + SYS, @"HKU\" + USR })
            {
                if (!Loaded(n)) continue;
                for (int i = 0; i < 5; i++)
                {
                    string o = Shell.Run(Sys("reg.exe"), "unload \"" + n + "\"", true, 30000) ?? "";
                    if (!Loaded(n)) break;
                    if (i == 4) { Log.W("Could not unload {0}: {1}", n, o.Trim()); all = false; }
                    Thread.Sleep(1000);
                }
            }
            return all;
        }

        // Writes the tweaks into the mounted image. Null on success, else the reason.
        static string WriteImage(string mount, List<Tweak> tweaks, out int written, out int missing)
        {
            written = missing = 0; bool unloadFailed = false;
            string cfg = Path.Combine(mount, @"Windows\System32\config");
            try
            {
                if (!Load(@"HKLM\" + SW, Path.Combine(cfg, "SOFTWARE")) || !Load(@"HKLM\" + SYS, Path.Combine(cfg, "SYSTEM"))) return "could not load the image's registry hives";
                bool users = Load(@"HKU\" + USR, Path.Combine(mount, @"Users\Default\NTUSER.DAT"));
                using (var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var hu = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64))
                {
                    int cs = 1;
                    using (var sel = lm.OpenSubKey(SYS + @"\Select")) if (sel != null) { object c = sel.GetValue("Current"); if (c is int && (int)c > 0) cs = (int)c; }
                    foreach (var t in tweaks)
                        foreach (var o in t.Ops)
                        {
                            bool u;
                            var v = o as VOp;
                            if (v != null)
                            {
                                string p = OfflinePath(v.H, v.Path, cs, out u);
                                if (p == null || u && !users) continue;
                                if (v.On == null)
                                {
                                    using (var k = (u ? hu : lm).OpenSubKey(p, true)) if (k != null) k.DeleteValue(v.Name, false);   // no empty key for a delete
                                }
                                else using (var k = (u ? hu : lm).CreateSubKey(p)) k.SetValue(v.Name, v.On, v.Kind);
                                written++; continue;
                            }
                            var ka = o as KeyAddOp;
                            if (ka != null)
                            {
                                string p = OfflinePath(ka.H, ka.Path, cs, out u);
                                if (p == null || u && !users) continue;
                                using ((u ? hu : lm).CreateSubKey(p)) { }
                                written++; continue;
                            }
                            var so = o as SvcOp;
                            if (so != null)
                            {
                                using (var k = lm.OpenSubKey(SYS + @"\ControlSet" + cs.ToString("000", CultureInfo.InvariantCulture) + @"\Services\" + so.Name, true))
                                {
                                    if (k == null) { missing++; continue; }   // the service isn't in this edition
                                    k.SetValue("Start", so.On, RegistryValueKind.DWord);
                                    if (k.GetValue("DelayedAutostart") != null) k.SetValue("DelayedAutostart", 0, RegistryValueKind.DWord);
                                }
                                written++;
                            }
                        }
                    using (var k = lm.CreateSubKey(SW + @"\ArchTweaker"))
                    {
                        k.SetValue("BakedBy", "ArchTweaker " + Program.Version, RegistryValueKind.String);
                        k.SetValue("BakedTweaks", tweaks.Select(t => t.Id).ToArray(), RegistryValueKind.MultiString);
                    }
                }
                if (!users) Log.W("Bake: the image has no Users\\Default\\NTUSER.DAT, so per-user values were skipped.");
            }
            finally { if (!UnloadAll()) unloadFailed = true; }
            // a hive still loaded blocks the commit: say so here instead of a generic "could not save"
            return unloadFailed ? "Windows did not release the image's registry (an antivirus or indexer may hold it). Nothing was saved." : null;
        }
    }

}
