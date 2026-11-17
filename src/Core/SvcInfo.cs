// ArchTweaker: service and OS info helpers (3.32 split from Engine.cs).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class Svc
    {
        const string Base = @"SYSTEM\CurrentControlSet\Services\";
        public static bool Exists(string name) { return Reg.KeyExists(Hive.HKLM, Base + name); }
        public static int GetStart(string name)
        {
            object o = Reg.Get(Hive.HKLM, Base + name, "Start");
            return o is int ? (int)o : -1;
        }
        public static bool GetDelayed(string name)
        {
            object o = Reg.Get(Hive.HKLM, Base + name, "DelayedAutostart");
            return o is int && (int)o == 1;
        }
        // start: 2 auto, 3 manual, 4 disabled. delayed only meaningful for 2.
        public static void SetStart(string name, int start, bool delayed)
        {
            StateCache.Clear();   // changes outside the tweak catalog make cached states stale
            if (!Exists(name)) return;
            try
            {
                Reg.Set(Hive.HKLM, Base + name, "Start", RegistryValueKind.DWord, start);
                if (start == 2) Reg.Set(Hive.HKLM, Base + name, "DelayedAutostart", RegistryValueKind.DWord, delayed ? 1 : 0);
            }
            catch (Exception ex)
            {
                // Protected services: fall back to sc.exe
                string t = start == 4 ? "disabled" : start == 3 ? "demand" : delayed ? "delayed-auto" : "auto";
                string r = Shell.Run("sc.exe", "config \"" + name + "\" start= " + t, true);
                Log.W("{0}: registry denied ({1}); sc.exe: {2}", name, ex.Message, r.Trim());
            }
            if (start == 4) Shell.Run("sc.exe", "stop \"" + name + "\"", false);
        }
        public static string StartName(int s, bool delayed)
        {
            switch (s)
            {
                case 0: return "Boot";
                case 1: return "System";
                case 2: return delayed ? "Automatic (Delayed)" : "Automatic";
                case 3: return "Manual";
                case 4: return "Disabled";
                default: return "?";
            }
        }
    }

    public static partial class OSInfo
    {
        // 3.35: tests set this instead of writing HKLM\...\CurrentBuildNumber (a killed test left Windows reporting a wrong build)
        public static int BuildOverride;
        public static int Build
        {
            get
            {
                if (BuildOverride > 0) return BuildOverride;
                object o = Reg.Get(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
                int b; return o != null && int.TryParse(o.ToString(), out b) ? b : 0;
            }
        }
        public static bool IsWin11 { get { return Build >= 22000; } }
        public static string Name
        {
            get
            {
                string pn = (Reg.Get(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") ?? "Windows").ToString();
                if (IsWin11) pn = pn.Replace("Windows 10", "Windows 11");
                string dv = (Reg.Get(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion") ?? "").ToString();
                object ubr = Reg.Get(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");
                return pn + " " + dv + " (build " + Build + (ubr != null ? "." + ubr : "") + ")";
            }
        }
    }
}
