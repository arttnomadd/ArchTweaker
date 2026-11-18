// 3.2.1: file-system trust for an elevated app.
// ProgramData\ArchTweaker is read and run by the elevated app and by SYSTEM tasks, but by default any standard
// user may create files and folders in a new ProgramData subfolder. So:
//  * Harden(): the folder tree is locked to SYSTEM + Administrators (Users may only read), owners reset to Administrators.
//  * Trusted(): a file is only acted on (imported, replayed, run, loaded) if SYSTEM, Administrators or TrustedInstaller
//    owns it. A file someone else planted before the lock-down keeps its owner, so it is still recognised and ignored.
//  * InstallExe(): the one way to put ArchTweaker.exe into bin\ for scheduled tasks and the timer service.
//  * DeleteUnder(): deletes a file only if the file actually opened lies under the folder being cleaned (a junction
//    swapped into the path between listing and deleting can't redirect the delete).
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace ArchTweaker
{
    // .reg backups are only re-imported from ArchTweaker's backup folder, and only if an administrator owns them
    public static class Backups
    {
        public static bool Ok(string file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return false;
            string full; try { full = Path.GetFullPath(file); } catch { return false; }
            string dir = Path.GetFullPath(Paths.Backups).TrimEnd('\\') + "\\";
            if (!full.StartsWith(dir, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(".reg", StringComparison.OrdinalIgnoreCase)) return false;
            return Secure.TrustedData(full, "a registry backup");
        }
    }

    public static class Secure
    {
        static bool Win { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }
        static readonly SecurityIdentifier Sys = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        static readonly SecurityIdentifier Admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

        public static bool IsTrustedOwner(SecurityIdentifier sid)
        {
            if (sid == null) return false;
            // Not the current user's own SID: a program running as the same user without elevation could create
            // such a file too. Elevated processes and SYSTEM tasks create files owned by Administrators.
            return sid.Equals(Sys) || sid.Equals(Admins) || sid.Value == TrustedInstaller;
        }

        static bool? elevated;   // the token is in the Administrators role (elevated, given the manifest)
        static bool IsElevated()
        {
            if (elevated.HasValue) return elevated.Value;
            try { elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { elevated = false; }
            return elevated.Value;
        }

        // True when the file or folder exists and SYSTEM / Administrators / TrustedInstaller owns it.
        public static bool Trusted(string path)
        {
            if (!Win) return true;   // Mono test runs on Linux: no Windows ACLs
            try
            {
                FileSystemSecurity sec;
                if (Directory.Exists(path)) sec = Directory.GetAccessControl(path, AccessControlSections.Owner);
                else if (File.Exists(path)) sec = File.GetAccessControl(path, AccessControlSections.Owner);
                else return false;
                return IsTrustedOwner(sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier);
            }
            catch { return false; }
        }

        // A file the elevated app is about to act on: exists and is owned by an admin principal. Logs why when not.
        // Deletes an untrusted copy, so the app writes a fresh one it owns (settings, caches). True when a file was removed.
        public static bool DropUntrusted(string path, string why)
        {
            if (!Win || !File.Exists(path) || Trusted(path)) return false;
            TrustedData(path, why);   // logs owner + reason
            try { File.Delete(path); } catch (Exception ex) { Log.W("  could not remove it: {0}", ex.Message); }
            return true;
        }

        public static bool TrustedData(string path, string why)
        {
            if (!File.Exists(path)) return false;
            if (Trusted(path)) return true;
            string owner = "?";
            try { owner = File.GetAccessControl(path, AccessControlSections.Owner).GetOwner(typeof(NTAccount)).ToString(); } catch { }
            Log.W("Ignored {0}: {1} is owned by {2}, not by an administrator, so it may have been planted.", why, path, owner);
            return false;
        }

        static int hardened;
        const string HardenMark = "3.2.1";   // bump to walk the whole tree again after a rule change
        // Locks ProgramData\ArchTweaker down. The folder ACL is checked on every start (cheap); the walk over every
        // sub-folder and file runs once per HardenMark. Only when elevated (the app always is).
        // 3.27.1: a folder the elevated app writes outside ProgramData (the recovery kit on the system drive root).
        // A junction in its place is removed; the folder gets owner Administrators and a protected ACL: SYSTEM and
        // Administrators full control, Users read only (C:\ lets Authenticated Users modify new sub-folders).
        public static void LockDir(string dir)
        {
            if (!Win) return;
            try
            {
                if (Directory.Exists(dir) && (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                { Directory.Delete(dir, false); Log.W("Removed a junction planted as {0}", dir); }
                Directory.CreateDirectory(dir);
                var sec = new DirectorySecurity();
                sec.SetOwner(Admins);
                sec.SetAccessRuleProtection(true, false);
                var all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                sec.AddAccessRule(new FileSystemAccessRule(Sys, FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, all, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(dir).SetAccessControl(sec);
            }
            catch (Exception ex) { Log.W("Could not lock {0}: {1}", dir, ex.Message); throw; }
        }

        public static void Harden(string root)
        {
            if (!Win || System.Threading.Interlocked.Exchange(ref hardened, 1) == 1) return;
            if (!IsElevated()) return;
            try
            {
                // a junction planted as ProgramData\ArchTweaker would make us re-own and re-ACL its target: remove it
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(root, false); Directory.CreateDirectory(root);
                    Log.W("Removed a junction planted as {0}", root);
                }
                var di = new DirectoryInfo(root);
                var sec = new DirectorySecurity();
                sec.SetOwner(Admins);
                sec.SetAccessRuleProtection(true, false);   // no inherited "Users: create files" from ProgramData
                var all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                sec.AddAccessRule(new FileSystemAccessRule(Sys, FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
                // no Users entry: undo data and backups hold old registry values; only the elevated app and SYSTEM need them
                var cur = di.GetAccessControl();
                bool same = cur.AreAccessRulesProtected && Equals(cur.GetOwner(typeof(SecurityIdentifier)), Admins)
                    && cur.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().All(r => r.IdentityReference.Equals(Sys) || r.IdentityReference.Equals(Admins));
                if (!same) di.SetAccessControl(sec);
                if (Equals(Reg.Get(Hive.HKLM, @"Software\ArchTweaker", "Hardened"), HardenMark) && same) { CheckBinExe(root); return; }
                string realRoot = RealDir(root);
                // every folder below: owner Administrators, explicit entries removed, so it inherits exactly the rules above.
                // Files keep their owner on purpose: Trusted() then still recognises one that somebody else planted.
                foreach (var d in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(d, false); Log.W("Removed a junction from {0}: {1}", root, d); continue; }
                        string rd = RealDir(d);   // swapped for a junction since it was listed: leave it alone
                        if (rd == null || realRoot == null || !rd.StartsWith(realRoot + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                        var ds = Directory.GetAccessControl(d);
                        ds.SetOwner(Admins);
                        foreach (FileSystemAccessRule r in ds.GetAccessRules(true, false, typeof(SecurityIdentifier))) ds.RemoveAccessRuleSpecific(r);
                        ds.SetAccessRuleProtection(false, false);
                        Directory.SetAccessControl(d, ds);
                    }
                    catch (Exception ex) { Log.W("Securing {0}: {1}", d, ex.Message); }
                }
                // files: explicit entries removed, so nobody keeps write access granted before the lock-down
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fs = File.GetAccessControl(f);
                        bool changed = false;
                        foreach (FileSystemAccessRule r in fs.GetAccessRules(true, false, typeof(SecurityIdentifier))) { fs.RemoveAccessRuleSpecific(r); changed = true; }
                        if (fs.AreAccessRulesProtected) { fs.SetAccessRuleProtection(false, false); changed = true; }
                        if (changed) File.SetAccessControl(f, fs);
                    }
                    catch (Exception ex) { Log.W("Securing {0}: {1}", f, ex.Message); }
                }
                CheckBinExe(root);
                try { using (var b = Reg.Base(Hive.HKLM)) using (var k = b.CreateSubKey(@"Software\ArchTweaker")) k.SetValue("Hardened", HardenMark); }
                catch (Exception ex) { Log.W("Could not record the lock-down mark (the walk runs again next start): {0}", ex.Message); }
            }
            catch (Exception ex) { Log.W("Could not lock down {0}: {1}", root, ex.Message); }
        }

        // The copy that SYSTEM tasks and the timer service run: replaced if anyone but an administrator owns it
        static void CheckBinExe(string root)
        {
            string exe = Path.Combine(root, @"bin\ArchTweaker.exe");
            if (!File.Exists(exe) || Trusted(exe)) return;
            Log.W("bin\\ArchTweaker.exe was not owned by an administrator: replacing it before any task runs it.");
            try { File.Delete(exe); } catch (Exception ex) { Log.W("  could not remove it: {0}", ex.Message); }
            InstallExe();
        }

        // Puts this exe into bin\ for SYSTEM tasks and the timer service. A copy someone else owns is replaced, the new
        // copy is written under a random name and renamed, and its owner is checked before anything is scheduled.
        public static string InstallExe()
        {
            string bin = Path.Combine(Paths.Root, "bin"), dest = Path.Combine(bin, "ArchTweaker.exe");
            try
            {
                if (Win && Directory.Exists(bin) && !Trusted(bin)) { Directory.Delete(bin, true); Log.W("Replaced a bin folder ArchTweaker did not create."); }
                Directory.CreateDirectory(bin);
                if (Win)
                {
                    var ds = Directory.GetAccessControl(bin);
                    ds.SetOwner(Admins);
                    foreach (FileSystemAccessRule r in ds.GetAccessRules(true, false, typeof(SecurityIdentifier))) ds.RemoveAccessRuleSpecific(r);
                    ds.SetAccessRuleProtection(false, false);   // inherit the locked-down rules of ArchTweaker's folder
                    Directory.SetAccessControl(bin, ds);
                }
                string self = System.Windows.Forms.Application.ExecutablePath;
                CleanBin(bin);
                if (!string.Equals(self, dest, StringComparison.OrdinalIgnoreCase) && !SameFile(self, dest))
                {
                    string tmp = Path.Combine(bin, "at-" + Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        File.Copy(self, tmp, false);
                        if (File.Exists(dest))
                        {
                            // 3.9: a running copy (timer service, boot task) can't be deleted, but it can be renamed
                            try { File.Delete(dest); }
                            catch (Exception) { File.Move(dest, Path.Combine(bin, "at-" + Guid.NewGuid().ToString("N") + ".old")); }
                        }
                        File.Move(tmp, dest);
                    }
                    finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
                    if (Win) { var fs = File.GetAccessControl(dest); fs.SetOwner(Admins); File.SetAccessControl(dest, fs); }
                }
                if (Win && !Trusted(dest)) { Log.W("bin\\ArchTweaker.exe is not owned by an administrator: nothing was scheduled."); return null; }
                return dest;
            }
            catch (Exception ex) { Log.W("Installing the scheduled-task copy: {0}", ex.Message); return null; }
        }

        // 3.9: same bytes, so the copy can stay (no churn while a task runs it)
        public static bool SameFile(string a, string b)
        {
            try
            {
                if (!File.Exists(a) || !File.Exists(b) || new FileInfo(a).Length != new FileInfo(b).Length) return false;
                using (var h = System.Security.Cryptography.SHA256.Create())
                using (var fa = File.OpenRead(a)) using (var fb = File.OpenRead(b))
                    return Convert.ToBase64String(h.ComputeHash(fa)) == Convert.ToBase64String(h.ComputeHash(fb));
            }
            catch { return false; }
        }

        // 3.9: removes temp copies left by failed installs and renamed copies that no longer run
        public static int CleanBin(string bin)
        {
            int n = 0;
            if (!Directory.Exists(bin)) return 0;
            foreach (var f in Directory.GetFiles(bin, "at-*.tmp").Concat(Directory.GetFiles(bin, "at-*.old")))
            {
                try { File.Delete(f); n++; } catch { }   // still running: next time
            }
            if (n > 0) Log.W("Removed {0} leftover program copies from {1}", n, bin);
            return n;
        }

        // ---- deletes that can't be redirected ----
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern uint GetFinalPathNameByHandle(IntPtr h, StringBuilder buf, uint len, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetFileInformationByHandle(IntPtr h, int cls, ref FILE_DISPOSITION_INFO info, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetFileInformationByHandle(IntPtr h, int cls, ref FILE_BASIC_INFO info, uint size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] struct FILE_DISPOSITION_INFO { [MarshalAs(UnmanagedType.U1)] public bool DeleteFile; }
        [StructLayout(LayoutKind.Sequential)] struct FILE_BASIC_INFO { public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime; public uint FileAttributes; }
        const uint DELETE = 0x00010000, FILE_READ_ATTRIBUTES = 0x80, FILE_WRITE_ATTRIBUTES = 0x100, SHARE_ALL = 7, OPEN_EXISTING = 3;
        const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000, FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        static readonly IntPtr Invalid = new IntPtr(-1);

        static string FinalPath(IntPtr h)
        {
            var sb = new StringBuilder(1024);
            uint n = GetFinalPathNameByHandle(h, sb, (uint)sb.Capacity, 0);
            if (n == 0 || n >= sb.Capacity) return null;
            string p = sb.ToString();
            if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p.Substring(8);
            else if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);
            return p.TrimEnd('\\');
        }
        // The real location of a folder, through any junction in its path.
        public static string RealDir(string dir)
        {
            if (!Win) { try { return Path.GetFullPath(dir).TrimEnd('\\', '/'); } catch { return null; } }
            IntPtr h = CreateFile(dir, FILE_READ_ATTRIBUTES, SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (h == Invalid) return null;
            try { return FinalPath(h); } finally { CloseHandle(h); }
        }

        // Deletes `file` only if the file that actually opens is inside `realBase` (from RealDir). Throws when it can't.
        public static void DeleteUnder(string file, string realBase)
        {
            if (!Win) { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); return; }
            if (string.IsNullOrEmpty(realBase)) throw new IOException("cleaning folder could not be resolved");
            IntPtr h = CreateFile(file, DELETE | FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES, SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
            if (h == Invalid) h = CreateFile(file, DELETE | FILE_READ_ATTRIBUTES, SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
            if (h == Invalid) throw new IOException("in use or access denied", Marshal.GetLastWin32Error());
            try
            {
                string real = FinalPath(h);
                if (real == null || !real.StartsWith(realBase + "\\", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("path leads outside the folder being cleaned: " + (real ?? "?"));
                var basic = new FILE_BASIC_INFO { FileAttributes = 0x80 };   // clear read-only on this handle only
                SetFileInformationByHandle(h, 0 /* FileBasicInfo */, ref basic, (uint)Marshal.SizeOf(typeof(FILE_BASIC_INFO)));
                var disp = new FILE_DISPOSITION_INFO { DeleteFile = true };
                if (!SetFileInformationByHandle(h, 4 /* FileDispositionInfo */, ref disp, (uint)Marshal.SizeOf(typeof(FILE_DISPOSITION_INFO))))
                    throw new IOException("delete refused", Marshal.GetLastWin32Error());
            }
            finally { CloseHandle(h); }   // the file goes away here, on exactly the handle that was checked
        }
    }
}
