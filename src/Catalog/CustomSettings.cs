// ArchTweaker: Customize page settings: everyday Windows values with a choice or a number, journaled (3.39).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace ArchTweaker
{
    // One registry value with a few named choices (Labels/Values) or a number range (Min..Max).
    // Writes go through Reg.Set, so the undo journal keeps the original and "Undo everything" restores it.
    public class CustomSetting
    {
        public string Id, Group, Title, Unit = "", AppliesWhen;
        public Hive H = Hive.HKCU;
        public string Path, Name;
        public bool IsString;          // Control Panel values are REG_SZ numbers
        public int Default, Min, Max;
        public string[] Labels; public int[] Values;
        public bool Win11Only;
        public bool IsChoice { get { return Labels != null; } }
        public bool Shown { get { return !Win11Only || OSInfo.IsWin11; } }

        public int Clamp(int v) { return IsChoice ? (Values.Contains(v) ? v : Default) : Math.Max(Min, Math.Min(Max, v)); }

        // the value now (the default when Windows has none or it can't be read)
        public int Current()
        {
            object o = null;
            try { o = Reg.Get(H, Path, Name); } catch { }
            return Parse(o);
        }
        public int Parse(object o)
        {
            if (o is int) return Clamp((int)o);
            int v; if (o != null && int.TryParse(o.ToString().Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out v)) return Clamp(v);
            return Default;
        }
        public object ToRegistry(int v) { v = Clamp(v); return IsString ? (object)v.ToString(System.Globalization.CultureInfo.InvariantCulture) : v; }
        public RegistryValueKind Kind { get { return IsString ? RegistryValueKind.String : RegistryValueKind.DWord; } }

        public void Set(int v)
        {
            Reg.Set(H, Path, Name, Kind, ToRegistry(v));
            Log.W("Customize: {0} = {1}", Title, ToRegistry(v));
        }
        // true when ArchTweaker changed it and the journal knows the value from before
        public bool Changed { get { object v; RegistryValueKind k; return Journal.Original(H, Path, Name, out v, out k); } }
        public bool PutBack()
        {
            object v; RegistryValueKind k;
            if (!Journal.Original(H, Path, Name, out v, out k)) return false;
            if (v == null) Reg.DeleteValueRaw(H, Path, Name); else Reg.SetRaw(H, Path, Name, k, v);
            Log.W("Customize: {0} put back", Title);
            return true;
        }
    }

    public static class CustomSettings
    {
        const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string Explorer = "after Explorer restarts", SignOut = "after you sign out and in again";
        public static readonly List<CustomSetting> All = new List<CustomSetting>
        {
            new CustomSetting { Id = "taskbar_align", Group = "Taskbar", Title = "Taskbar alignment", Path = Adv, Name = "TaskbarAl", Default = 1, Labels = new[] { "Left", "Centre" }, Values = new[] { 0, 1 }, Win11Only = true, AppliesWhen = "at once" },
            new CustomSetting { Id = "taskbar_search", Group = "Taskbar", Title = "Search on the taskbar", Path = @"Software\Microsoft\Windows\CurrentVersion\Search", Name = "SearchboxTaskbarMode", Default = 1, Labels = new[] { "Hidden", "Icon only", "Search box" }, Values = new[] { 0, 1, 2 }, AppliesWhen = Explorer },
            new CustomSetting { Id = "clock_seconds", Group = "Taskbar", Title = "Seconds on the clock", Path = Adv, Name = "ShowSecondsInSystemClock", Default = 0, Labels = new[] { "Off", "On" }, Values = new[] { 0, 1 }, AppliesWhen = Explorer },
            new CustomSetting { Id = "explorer_start", Group = "File Explorer", Title = "File Explorer opens to", Path = Adv, Name = "LaunchTo", Default = 2, Labels = new[] { "This PC", "Home / Quick access", "Downloads" }, Values = new[] { 1, 2, 3 }, AppliesWhen = "in the next window" },
            new CustomSetting { Id = "menu_delay", Group = "Menus and keys", Title = "Menu delay", Unit = "ms", Path = @"Control Panel\Desktop", Name = "MenuShowDelay", IsString = true, Default = 400, Min = 0, Max = 1000, AppliesWhen = SignOut },
            new CustomSetting { Id = "hover_delay", Group = "Menus and keys", Title = "Hover delay", Unit = "ms", Path = @"Control Panel\Mouse", Name = "MouseHoverTime", IsString = true, Default = 400, Min = 0, Max = 1000, AppliesWhen = SignOut },
            new CustomSetting { Id = "key_delay", Group = "Menus and keys", Title = "Key repeat delay", Path = @"Control Panel\Keyboard", Name = "KeyboardDelay", IsString = true, Default = 1, Labels = new[] { "Shortest", "Short", "Long", "Longest" }, Values = new[] { 0, 1, 2, 3 }, AppliesWhen = SignOut },
            new CustomSetting { Id = "key_rate", Group = "Menus and keys", Title = "Key repeat speed", Unit = "(0 slow, 31 fast)", Path = @"Control Panel\Keyboard", Name = "KeyboardSpeed", IsString = true, Default = 31, Min = 0, Max = 31, AppliesWhen = SignOut },
        };

        public static int PutBackAll()
        {
            int n = 0;
            foreach (var s in All) { try { if (s.PutBack()) n++; } catch (Exception ex) { Log.W("Customize: {0}: {1}", s.Title, ex.Message); } }
            if (n > 0) Native.BroadcastSettingChange();
            return n;
        }
    }
}
