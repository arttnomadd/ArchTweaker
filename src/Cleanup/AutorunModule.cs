// ArchTweaker: Startup entries: list, enable, disable, delete (Task Manager compatible).
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
    // ---------------- AUTORUN ----------------
    public class AutoItem
    {
        public string Name, Command, Location; public bool Enabled;
        public Hive H; public string RunPath, ApprovedPath; public string FilePath; // FilePath for startup-folder items
    }

    public static class AutorunModule
    {
        const string APP = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static List<AutoItem> Load()
        {
            var l = new List<AutoItem>();
            AddRun(l, Hive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Run", APP + "Run", "HKCU Run");
            AddRun(l, Hive.HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", APP + "Run", "HKLM Run");
            AddRun(l, Hive.HKLM, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", APP + "Run32", "HKLM Run (32-bit)");
            AddFolder(l, Environment.GetFolderPath(Environment.SpecialFolder.Startup), Hive.HKCU, "Startup folder (user)");
            AddFolder(l, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Hive.HKLM, "Startup folder (all users)");
            return l;
        }

        static bool IsEnabled(Hive h, string approved, string name)
        {
            object o = Reg.Get(h, approved, name);
            var b = o as byte[];
            if (b == null || b.Length == 0) return true;
            return (b[0] & 1) == 0; // 02/06 enabled, 03/07 disabled
        }

        static void AddRun(List<AutoItem> l, Hive h, string path, string approved, string loc)
        {
            foreach (var n in Reg.ValueNames(h, path))
            {
                if (string.IsNullOrEmpty(n)) continue;
                l.Add(new AutoItem { Name = n, Command = (Reg.Get(h, path, n) ?? "").ToString(), Location = loc, H = h, RunPath = path, ApprovedPath = approved, Enabled = IsEnabled(h, approved, n) });
            }
        }

        static void AddFolder(List<AutoItem> l, string dir, Hive h, string loc)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir))
            {
                string fn = Path.GetFileName(f);
                if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                l.Add(new AutoItem { Name = fn, Command = f, Location = loc, H = h, ApprovedPath = APP + "StartupFolder", FilePath = f, Enabled = IsEnabled(h, APP + "StartupFolder", fn) });
            }
        }

        public static void SetEnabled(AutoItem it, bool on)
        {
            var b = new byte[12];
            b[0] = (byte)(on ? 2 : 3);
            if (!on) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(b, 4);
            Reg.Set(it.H, it.ApprovedPath, it.Name, RegistryValueKind.Binary, b);
            it.Enabled = on;
        }

        public static void Delete(AutoItem it)
        {
            if (it.FilePath != null)
            {
                string bak = Path.Combine(Paths.Backups, "startup_" + DateTime.Now.Ticks + "_" + it.Name);
                File.Move(it.FilePath, bak);
                Log.W("Moved {0} to {1}", it.FilePath, bak);
            }
            else Reg.DeleteValue(it.H, it.RunPath, it.Name);
        }
    }
}
