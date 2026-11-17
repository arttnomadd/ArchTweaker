// ArchTweaker 2.3: Power settings explorer — every power setting of every plan, including the ones Windows hides.
// Reads names/ranges/values through powrprof.dll (the same API Power Options uses). Value edits record the
// original AC/DC value once per setting so "Revert my edits" (and Undo everything) put back exactly what was there.
// Hide/unhide writes the setting's Attributes value through the journaled Reg.Set.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    public class PowerSetting
    {
        public Guid Scheme, Sub, Id;
        public string SubName, Name, Desc, Units;
        public uint Attributes;
        public bool Hidden { get { return (Attributes & 1) != 0; } }
        public bool IsEnum { get { return Options.Count > 0; } }
        public uint Min, Max, Inc;
        public List<KeyValuePair<uint, string>> Options = new List<KeyValuePair<uint, string>>();
        public uint Ac, Dc;
        public bool HasValue;

        public string Show(uint v)
        {
            if (IsEnum) { foreach (var o in Options) if (o.Key == v) return o.Value; return v.ToString(); }
            return v + (string.IsNullOrEmpty(Units) ? "" : " " + Units);
        }
        public string Range
        {
            get
            {
                if (IsEnum) return string.Join(" / ", Options.Select(o => o.Value).ToArray());
                return Min + "–" + Max + (string.IsNullOrEmpty(Units) ? "" : " " + Units) + (Inc > 1 ? " (step " + Inc + ")" : "");
            }
        }
        public string RegPath { get { return @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\{" + Sub + @"}\{" + Id + "}"; } }
    }

    public class PowerScheme { public Guid Id; public string Name; public bool Active; public override string ToString() { return (Active ? "● " : "") + Name; } }

    public static class PowerExplorer
    {
        const uint ACCESS_SCHEME = 16, ACCESS_SUBGROUP = 17, ACCESS_INDIVIDUAL_SETTING = 18;

        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);

        [DllImport("powrprof.dll", EntryPoint = "PowerEnumerate")] static extern uint EnumSchemes(IntPtr root, IntPtr scheme, IntPtr sub, uint access, uint index, byte[] buf, ref uint size);
        [DllImport("powrprof.dll", EntryPoint = "PowerEnumerate")] static extern uint EnumSubs(IntPtr root, ref Guid scheme, IntPtr sub, uint access, uint index, byte[] buf, ref uint size);
        [DllImport("powrprof.dll", EntryPoint = "PowerEnumerate")] static extern uint EnumSettings(IntPtr root, ref Guid scheme, ref Guid sub, uint access, uint index, byte[] buf, ref uint size);

        [DllImport("powrprof.dll", EntryPoint = "PowerReadFriendlyName")] static extern uint NameOfScheme(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, byte[] buf, ref uint size);
        [DllImport("powrprof.dll", EntryPoint = "PowerReadFriendlyName")] static extern uint NameOfSub(IntPtr root, IntPtr scheme, ref Guid sub, IntPtr setting, byte[] buf, ref uint size);
        [DllImport("powrprof.dll", EntryPoint = "PowerReadFriendlyName")] static extern uint NameOfSetting(IntPtr root, IntPtr scheme, ref Guid sub, ref Guid setting, byte[] buf, ref uint size);
        [DllImport("powrprof.dll", EntryPoint = "PowerReadDescription")] static extern uint DescOfSetting(IntPtr root, IntPtr scheme, ref Guid sub, ref Guid setting, byte[] buf, ref uint size);

        [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);

        [DllImport("powrprof.dll")] static extern uint PowerReadValueMin(IntPtr root, ref Guid sub, ref Guid setting, out uint v);
        [DllImport("powrprof.dll")] static extern uint PowerReadValueMax(IntPtr root, ref Guid sub, ref Guid setting, out uint v);
        [DllImport("powrprof.dll")] static extern uint PowerReadValueIncrement(IntPtr root, ref Guid sub, ref Guid setting, out uint v);
        [DllImport("powrprof.dll")] static extern uint PowerReadValueUnitsSpecifier(IntPtr root, ref Guid sub, ref Guid setting, byte[] buf, ref uint size);
        [DllImport("powrprof.dll")] static extern uint PowerReadPossibleFriendlyName(IntPtr root, ref Guid sub, ref Guid setting, uint index, byte[] buf, ref uint size);
        [DllImport("powrprof.dll")] static extern uint PowerReadPossibleValue(IntPtr root, ref Guid sub, ref Guid setting, out uint type, uint index, byte[] buf, ref uint size);
        [DllImport("powrprof.dll")] static extern uint PowerReadSettingAttributes(ref Guid sub, ref Guid setting);

        const string PREV = @"Software\ArchTweaker\PowerExplorer";   // original values of settings edited here

        delegate uint StrReader(byte[] buf, ref uint size);
        static string ReadStr(StrReader r)
        {
            uint size = 0;
            r(null, ref size);
            if (size == 0 || size > 65536) return "";
            var buf = new byte[size];
            if (r(buf, ref size) != 0) return "";
            return Encoding.Unicode.GetString(buf, 0, (int)size).TrimEnd('\0');
        }

        public static Guid Active()
        {
            IntPtr p;
            if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0) return Guid.Empty;
            try { return (Guid)Marshal.PtrToStructure(p, typeof(Guid)); }
            finally { LocalFree(p); }
        }

        public static List<PowerScheme> Schemes()
        {
            var l = new List<PowerScheme>(); var act = Active();
            for (uint i = 0; ; i++)
            {
                var buf = new byte[16]; uint size = 16;
                if (EnumSchemes(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, i, buf, ref size) != 0) break;
                var g = new Guid(buf);
                string n = ReadStr((byte[] b, ref uint s) => NameOfScheme(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, b, ref s));
                l.Add(new PowerScheme { Id = g, Name = n.Length > 0 ? n : g.ToString(), Active = g == act });
            }
            return l;
        }

        public static List<PowerSetting> Load(Guid scheme)
        {
            var l = new List<PowerSetting>();
            for (uint si = 0; ; si++)
            {
                var sb = new byte[16]; uint ss = 16;
                if (EnumSubs(IntPtr.Zero, ref scheme, IntPtr.Zero, ACCESS_SUBGROUP, si, sb, ref ss) != 0) break;
                var sub = new Guid(sb);
                string subName = ReadStr((byte[] b, ref uint s) => NameOfSub(IntPtr.Zero, IntPtr.Zero, ref sub, IntPtr.Zero, b, ref s));
                for (uint ti = 0; ; ti++)
                {
                    var tb = new byte[16]; uint ts = 16;
                    if (EnumSettings(IntPtr.Zero, ref scheme, ref sub, ACCESS_INDIVIDUAL_SETTING, ti, tb, ref ts) != 0) break;
                    var set = new Guid(tb);
                    try { l.Add(Read(scheme, sub, subName, set)); } catch { }
                }
            }
            return l;
        }

        static PowerSetting Read(Guid scheme, Guid sub, string subName, Guid set)
        {
            var p = new PowerSetting { Scheme = scheme, Sub = sub, Id = set, SubName = subName.Length > 0 ? subName : "(no subgroup)" };
            p.Name = ReadStr((byte[] b, ref uint s) => NameOfSetting(IntPtr.Zero, IntPtr.Zero, ref sub, ref set, b, ref s));
            if (p.Name.Length == 0) p.Name = "{" + set + "}";
            p.Desc = ReadStr((byte[] b, ref uint s) => DescOfSetting(IntPtr.Zero, IntPtr.Zero, ref sub, ref set, b, ref s));
            p.Attributes = PowerReadSettingAttributes(ref sub, ref set);
            for (uint i = 0; i < 64; i++)
            {
                uint type, size = 4; var vb = new byte[4];
                if (PowerReadPossibleValue(IntPtr.Zero, ref sub, ref set, out type, i, vb, ref size) != 0) break;
                if (type != 4) { p.Options.Clear(); break; }   // only REG_DWORD options make an enum
                uint idx = i;
                string on = ReadStr((byte[] b, ref uint s) => PowerReadPossibleFriendlyName(IntPtr.Zero, ref sub, ref set, idx, b, ref s));
                p.Options.Add(new KeyValuePair<uint, string>(BitConverter.ToUInt32(vb, 0), on.Length > 0 ? on : i.ToString()));
            }
            if (!p.IsEnum)
            {
                PowerReadValueMin(IntPtr.Zero, ref sub, ref set, out p.Min);
                PowerReadValueMax(IntPtr.Zero, ref sub, ref set, out p.Max);
                PowerReadValueIncrement(IntPtr.Zero, ref sub, ref set, out p.Inc);
                p.Units = ReadStr((byte[] b, ref uint s) => PowerReadValueUnitsSpecifier(IntPtr.Zero, ref sub, ref set, b, ref s));
            }
            uint ac, dc;
            p.HasValue = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, out ac) == 0
                       & PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, out dc) == 0;
            p.Ac = ac; p.Dc = dc;
            return p;
        }

        static string Key(Guid scheme, PowerSetting p, string side) { return scheme + "|" + p.Sub + "|" + p.Id + "|" + side; }

        // Write AC and/or DC (null = leave that side alone) to the setting's own plan. The first time a side is
        // edited, its LIVE original value is remembered so it can be restored exactly.
        public static void Write(PowerSetting p, uint? ac, uint? dc)
        {
            StateCache.Clear();   // changes outside the tweak catalog make cached states stale
            Guid scheme = p.Scheme, sub = p.Sub, set = p.Id;
            foreach (var v in new[] { ac, dc })
            {
                if (!v.HasValue) continue;
                if (p.IsEnum && !p.Options.Any(o => o.Key == v.Value)) throw new ArgumentException("value is not one of the setting's options");
                if (!p.IsEnum && (v.Value < p.Min || v.Value > p.Max)) throw new ArgumentOutOfRangeException("value", "outside " + p.Min + "–" + p.Max);
            }
            uint live;
            if (ac.HasValue)
            {
                if (PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, out live) == 0) Remember(Key(scheme, p, "AC"), live);
                uint r = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, ac.Value);
                if (r != 0) throw new InvalidOperationException("Windows refused the AC value (error " + r + ")");
                p.Ac = ac.Value;
            }
            if (dc.HasValue)
            {
                if (PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, out live) == 0) Remember(Key(scheme, p, "DC"), live);
                uint r = PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, dc.Value);
                if (r != 0) throw new InvalidOperationException("Windows refused the DC value (error " + r + ")");
                p.Dc = dc.Value;
            }
            if (scheme == Active()) { uint r = PowerSetActiveScheme(IntPtr.Zero, ref scheme); if (r != 0) Log.W("Re-activating the plan failed (error {0}) — the value applies on next plan switch.", r); }
            PowerCache.Invalidate();
            Log.W("Power setting '{0}': {1}{2}", p.Name, ac.HasValue ? "AC " + p.Show(ac.Value) + " " : "", dc.HasValue ? "DC " + p.Show(dc.Value) : "");
        }

        static void Remember(string key, uint original)
        {
            if (Reg.Get(Hive.HKCU, PREV, key) != null) return;   // keep the FIRST original only
            using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(PREV)) k.SetValue(key, unchecked((int)original), RegistryValueKind.DWord);
        }

        public static int EditedCount() { return Reg.ValueNames(Hive.HKCU, PREV).Length / 2; }

        // Restore every value edited through this page. A record is forgotten only after its restore succeeds.
        public static int RevertAll()
        {
            StateCache.Clear();   // changes outside the tweak catalog make cached states stale
            int n = 0; var act = Active();
            foreach (var name in Reg.ValueNames(Hive.HKCU, PREV))
            {
                var parts = name.Split('|');
                if (parts.Length != 4) continue;
                try
                {
                    Guid scheme = new Guid(parts[0]), sub = new Guid(parts[1]), set = new Guid(parts[2]);
                    uint v = unchecked((uint)(int)Reg.Get(Hive.HKCU, PREV, name));
                    uint r = parts[3] == "AC" ? PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, v)
                                              : PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, v);
                    if (r == 0)
                    {
                        n++;
                        using (var b = Reg.Base(Hive.HKCU)) using (var k = b.OpenSubKey(PREV, true)) if (k != null) k.DeleteValue(name, false);
                    }
                    else Log.W("Power revert of {0} failed (error {1}) — kept for a later retry.", name, r);
                }
                catch (Exception ex) { Log.W("Power revert failed for {0}: {1}", name, ex.Message); }
            }
            if (act != Guid.Empty) { uint r = PowerSetActiveScheme(IntPtr.Zero, ref act); if (r != 0) Log.W("Re-activating the plan failed (error {0}).", r); }
            if (Reg.ValueNames(Hive.HKCU, PREV).Length == 0) Reg.DeleteTreeRaw(Hive.HKCU, PREV);
            PowerCache.Invalidate();
            if (n > 0) Log.W("Restored {0} power value(s) edited in Power settings.", n);
            return n;
        }

        // Hide or show a setting in Control Panel > Power Options (ATTRIBUTE_HIDE = bit value 1).
        public static void SetHidden(PowerSetting p, bool hidden)
        {
            uint v = hidden ? (p.Attributes | 1u) : (p.Attributes & ~1u);   // only ATTRIBUTE_HIDE; AOAC bit left as is
            Reg.Set(Hive.HKLM, p.RegPath, "Attributes", RegistryValueKind.DWord, unchecked((int)v));
            p.Attributes = v;
            Log.W("{0} '{1}' in Power Options", hidden ? "Hid" : "Unhid", p.Name);
        }

        // Point regedit at the setting's key (regedit opens at its LastKey).
        public static void OpenInRegedit(PowerSetting p)
        {
            try
            {
                using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit"))
                    k.SetValue("LastKey", @"Computer\HKEY_LOCAL_MACHINE\" + p.RegPath, RegistryValueKind.String);
            }
            catch { }
            Shell.OpenVisible("regedit.exe", "-m");
        }

        public static string PowercfgCommand(PowerSetting p, uint ac, uint dc)
        {
            Guid scheme = p.Scheme;
            return "powercfg /setacvalueindex " + scheme + " " + p.Sub + " " + p.Id + " " + ac + "\r\n"
                 + "powercfg /setdcvalueindex " + scheme + " " + p.Sub + " " + p.Id + " " + dc + "\r\n"
                 + "powercfg /setactive " + scheme;
        }
    }
}
