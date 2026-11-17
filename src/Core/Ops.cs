// ArchTweaker: Tweak operations: NIC advanced properties, WMI power, service presets, keep, device disable, PLM.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Microsoft.Win32;

namespace ArchTweaker
{
    // Network adapter advanced properties. Each keyword is written only where the driver exposes it
    // (Ndi\Params\<keyword>) and the value is one the driver accepts; originals are saved for exact undo.
    public class NicAdvOp : Op
    {
        public string Label;
        public string[][] Rules;   // { keyword, "=value" | "min" | "cap:n" }
        List<string[]> plan; DateTime planAt;
        readonly object gate = new object();

        static string Param(string key, string kw) { return key + @"\Ndi\Params\" + kw; }
        static int? Int(object o) { int v; return o != null && int.TryParse(o.ToString(), out v) ? (int?)v : null; }

        // driver accepts the value: an enum lists it, or an int type range contains it, or it's free text
        static bool Accepts(string key, string kw, string v)
        {
            string p = Param(key, kw);
            if (Reg.KeyExists(Hive.HKLM, p + @"\enum")) return Reg.Get(Hive.HKLM, p + @"\enum", v) != null;
            int? n = Int(v), mn = Int(Reg.Get(Hive.HKLM, p, "min")), mx = Int(Reg.Get(Hive.HKLM, p, "max"));
            if (n.HasValue && mn.HasValue && n < mn) return false;
            if (n.HasValue && mx.HasValue && n > mx) return false;
            return true;
        }

        // { key, keyword, target } for every adapter and keyword that applies on this PC
        public static List<string[]> PlanFor(IEnumerable<string> nics, string[][] rules)
        {
            var l = new List<string[]>();
            foreach (var key in nics)
                foreach (var r in rules)
                {
                    string kw = r[0], rule = r[1], p = Param(key, kw);
                    if (!Reg.KeyExists(Hive.HKLM, p)) continue;
                    string target = null;
                    if (rule.StartsWith("=")) target = rule.Substring(1);
                    else if (rule == "min") { var mn = Reg.Get(Hive.HKLM, p, "min"); if (mn != null) target = mn.ToString(); }
                    else if (rule.StartsWith("cap:"))
                    {
                        int cap = int.Parse(rule.Substring(4));
                        int? cur = Int(Reg.Get(Hive.HKLM, key, kw)) ?? Int(Reg.Get(Hive.HKLM, p, "default"));
                        if (!cur.HasValue || cur <= cap) continue;   // already at or under the cap
                        int? mn = Int(Reg.Get(Hive.HKLM, p, "min"));
                        target = Math.Max(cap, mn ?? 0).ToString();
                    }
                    if (target == null || !Accepts(key, kw, target)) continue;
                    l.Add(new[] { key, kw, target });
                }
            return l;
        }

        List<string[]> Plan()
        {
            lock (gate)
            {
                if (plan != null && (DateTime.UtcNow - planAt).TotalSeconds < 20) return plan;
                try { plan = PlanFor(Devices.PhysicalNicKeys().ToList(), Rules); }
                catch (Exception ex) { Log.W("  {0}: {1}", Label, ex.Message); plan = new List<string[]>(); }
                planAt = DateTime.UtcNow; return plan;
            }
        }
        void Forget() { lock (gate) plan = null; }

        public override bool IsOn()
        {
            var p = Plan();
            return p.Count > 0 && p.All(x => (Reg.Get(Hive.HKLM, x[0], x[1]) ?? "").ToString() == x[2]);
        }
        public override void Apply()
        {
            Forget(); int n = 0;
            foreach (var x in Plan())
            {
                try { DevStore.Save(x[0], x[1]); Reg.Set(Hive.HKLM, x[0], x[1], RegistryValueKind.String, x[2]); n++; }
                catch (Exception ex) { Log.W("  {0} {1}: {2}", x[0], x[1], ex.Message); }
            }
            Forget();
            Log.W("  {0}: {1} adapter setting(s) written", Label, n);
        }
        public override void Revert()
        {
            foreach (var key in Devices.PhysicalNicKeys().ToList())
                foreach (var r in Rules) DevStore.Restore(key, r[0]);
            Forget();
        }
        public override string Source { get { return "network adapter driver settings"; } }
        public override string Describe()
        {
            var p = Plan();
            var sb = new StringBuilder("Network adapter settings: " + Label + " — " + p.Count + " value(s) on this PC");
            foreach (var x in p.Take(12))
            {
                string name = (Reg.Get(Hive.HKLM, x[0], "DriverDesc") ?? x[0]).ToString();
                sb.Append("\r\n      " + name + ": " + x[1] + " = " + x[2] + "  (now " + (Reg.Get(Hive.HKLM, x[0], x[1]) ?? "driver default") + ")");
            }
            if (p.Count > 12) sb.Append("\r\n      … and " + (p.Count - 12) + " more");
            sb.Append("\r\n      only settings the driver offers are written · undo: each original value restored");
            return sb.ToString();
        }
    }

    // "Allow the computer to turn off this device to save power", unticked on every device (WMI MSPower_DeviceEnable).
    public class WmiPowerOp : Op
    {
        const string P = @"SOFTWARE\ArchTweaker\WmiPower";
        static List<ManagementObject> All()
        {
            var l = new List<ManagementObject>();
            using (var q = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\wmi"), new ObjectQuery("SELECT * FROM MSPower_DeviceEnable")))
                foreach (ManagementObject o in q.Get()) l.Add(o);
            return l;
        }
        static bool Enabled(ManagementObject o) { object e = o["Enable"]; return e is bool && (bool)e; }
        static string Name(ManagementObject o) { return (o["InstanceName"] ?? "").ToString(); }
        static HashSet<string> List(string value) { return new HashSet<string>((Reg.Get(Hive.HKLM, P, value) as string[]) ?? new string[0], StringComparer.OrdinalIgnoreCase); }

        // 3.8.2: many drivers don't save this setting, so Windows turns it on again at every boot.
        // A SYSTEM task turns it off again 30 s after each start. Devices whose driver refuses the change don't count.
        public const string TASK = @"ArchTweaker\DevicePower";
        public override bool IsOn()
        {
            try { var refused = List("Refused"); var l = All(); return l.Count > 0 && l.All(o => !Enabled(o) || refused.Contains(Name(o))); }
            catch { return false; }
        }
        // Runs from the boot task (/devicepower): turns power saving off again on the devices this tweak turned off.
        public static void BootReapply()
        {
            var saved = List("Enabled"); int n = 0;
            if (saved.Count == 0) return;
            foreach (var o in All())
            {
                if (!Enabled(o) || !saved.Contains(Name(o))) continue;
                try { o["Enable"] = false; o.Put(); n++; } catch (Exception ex) { Log.W("  {0}: {1}", Name(o), ex.Message); }
            }
            Log.W("Device power (boot): power saving turned off again on {0} device(s) that Windows turned back on", n);
        }
        static void InstallTask()
        {
            if (Secure.InstallExe() == null) { Log.W("  Device power boot task not created: ArchTweaker couldn't secure the program copy for SYSTEM tasks (see log)."); return; }
            string o = Shell.Run("schtasks.exe", "/Create /F /TN \"" + TASK + "\" /SC ONSTART /DELAY 0000:30 /RL HIGHEST /RU SYSTEM /TR \"\\\"" + HostsEngine.InstalledExe + "\\\" /devicepower\"", true).Trim();
            Log.W("  Device power boot task: {0}", o.Length > 0 ? o : "created");
        }
        static void RemoveTask()
        {
            if (Shell.Run("schtasks.exe", "/Query /TN \"" + TASK + "\"", true).IndexOf("DevicePower", StringComparison.OrdinalIgnoreCase) >= 0)
                Shell.Run("schtasks.exe", "/Delete /F /TN \"" + TASK + "\"", true);
        }
        public override void Apply()
        {
            var saved = List("Enabled"); var refused = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var o in All())
            {
                if (!Enabled(o)) continue;
                string name = Name(o);
                try { o["Enable"] = false; o.Put(); saved.Add(name); n++; }
                catch (Exception ex) { refused.Add(name); Log.W("  {0}: the driver refused the change ({1})", name, ex.Message); }
            }
            Reg.Set(Hive.HKLM, P, "Enabled", RegistryValueKind.MultiString, saved.ToArray());
            Reg.Set(Hive.HKLM, P, "Refused", RegistryValueKind.MultiString, refused.ToArray());
            Log.W("  Device power saving turned off on {0} device(s){1}", n, refused.Count > 0 ? ", " + refused.Count + " refused" : "");
            InstallTask();
        }
        public override void Revert()
        {
            var saved = new HashSet<string>((Reg.Get(Hive.HKLM, P, "Enabled") as string[]) ?? new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (var o in All())
            {
                string name = (o["InstanceName"] ?? "").ToString();
                if (!saved.Contains(name) || Enabled(o)) continue;
                try { o["Enable"] = true; o.Put(); } catch (Exception ex) { Log.W("  {0}: {1}", name, ex.Message); }
            }
            Reg.DeleteValue(Hive.HKLM, P, "Enabled");
            Reg.DeleteValue(Hive.HKLM, P, "Refused");
            RemoveTask();
        }
        public override string Source { get { return "WMI MSPower_DeviceEnable"; } }
        public override string Describe()
        {
            int all = 0, on = 0;
            try { var l = All(); all = l.Count; on = l.Count(Enabled); } catch { }
            return "WMI root\\wmi MSPower_DeviceEnable: Enable = false on every device (" + on + " of " + all + " still allow power-off)\r\n      undo: only the devices ArchTweaker changed are allowed again";
        }
    }

    // Service start type with the original saved per service (some of these are Manual, some Automatic by default).
    public class SvcPrevOp : Op
    {
        public string Name;
        const string P = @"SOFTWARE\ArchTweaker\SvcPrev";
        public override bool IsOn() { return !Svc.Exists(Name) || Svc.GetStart(Name) == 4; }
        public override void Apply()
        {
            if (!Svc.Exists(Name)) return;
            if (Reg.Get(Hive.HKLM, P, Name) == null)
                Reg.Set(Hive.HKLM, P, Name, RegistryValueKind.String, Svc.GetStart(Name) + "|" + (Svc.GetDelayed(Name) ? 1 : 0));
            Svc.SetStart(Name, 4, false);
        }
        public override void Revert()
        {
            var s = Reg.Get(Hive.HKLM, P, Name) as string;
            if (s == null || !Svc.Exists(Name)) return;
            var p = s.Split('|'); int st;
            if (int.TryParse(p[0], out st)) Svc.SetStart(Name, st, p.Length > 1 && p[1] == "1");
            Reg.DeleteValue(Hive.HKLM, P, Name);
        }
        public override string Source { get { return "service"; } }
        public override string Describe()
        {
            return "Service " + Name + ": startup → Disabled" + (Svc.Exists(Name) ? " (now: " + Svc.StartName(Svc.GetStart(Name), Svc.GetDelayed(Name)) + ")" : " (not installed)") + "\r\n      undo: the startup type it had before";
        }
    }

    // A registry value whose original is saved per PC (DevStore) and restored exactly on undo.
    // Used where Windows' default differs between editions or was already changed by the user.
    public class KeepOp : Op
    {
        public Hive H = Hive.HKLM;   // 3.7: HKCU too
        public string Path, Name; public RegistryValueKind Kind = RegistryValueKind.DWord; public object Value;
        public override bool IsOn() { return Reg.Norm(Reg.Get(H, Path, Name)) == Reg.Norm(Value); }
        public override void Apply() { DevStore.Save(H, Path, Name); Reg.Set(H, Path, Name, Kind, Value); }
        public override void Revert() { DevStore.Restore(H, Path, Name); }
        public override string Describe()
        {
            return HiveName(H) + "\\" + Path + "\r\n      " + Name + " = " + Fmt(Value, Kind) + "  (now " + Fmt(Reg.Get(H, Path, Name), Kind) + ")\r\n      undo: the value it had before";
        }
    }

    // Disables the matching devices; only the ones ArchTweaker disabled are enabled again on undo.
    public class DevDisableOp : Op
    {
        public string Label; public string[] Ids, Names;
        const string P = @"SOFTWARE\ArchTweaker\DevDisabled";
        List<DevInfo> cache; DateTime at;
        List<DevInfo> Now(bool fresh = false)
        {
            if (!fresh && cache != null && (DateTime.UtcNow - at).TotalSeconds < 20) return cache;
            try { cache = DevCtl.Find(Ids, Names); at = DateTime.UtcNow; return cache; }
            catch (Exception ex) { Log.W("{0}: could not read the device list: {1}", Label, ex.Message); cache = null; return new List<DevInfo>(); }
        }
        public bool Present() { return Now().Count > 0; }
        HashSet<string> Saved() { return new HashSet<string>((Reg.Get(Hive.HKLM, P, Label) as string[]) ?? new string[0], StringComparer.OrdinalIgnoreCase); }
        public override bool IsOn() { var l = Now(); return l.Count > 0 && l.All(d => d.Disabled); }
        public override void Apply()
        {
            var saved = Saved();
            foreach (var d in Now(true).Where(x => !x.Disabled)) saved.Add(d.Id);
            Reg.Set(Hive.HKLM, P, Label, RegistryValueKind.MultiString, saved.ToArray());
            var failed = DevCtl.Change(Ids, Names, d => !d.Disabled, true);
            cache = null;
            if (failed.Count > 0) throw new InvalidOperationException(L.F("Could not disable {0} device(s).", failed.Count));
        }
        public override void Revert()
        {
            var saved = Saved();
            var failed = DevCtl.Change(Ids, Names, d => d.Disabled && saved.Contains(d.Id), false);
            cache = null;
            // 3.27.1: keep the IDs that did not come back, so a later undo can try again
            if (failed.Count == 0) Reg.DeleteValue(Hive.HKLM, P, Label);
            else
            {
                Reg.Set(Hive.HKLM, P, Label, RegistryValueKind.MultiString, failed.ToArray());
                throw new InvalidOperationException(L.F("Could not enable {0} device(s) again. Undo tries them again next time.", failed.Count));
            }
        }
        public override string Source { get { return "Device Manager"; } }
        public override string Describe()
        {
            var l = Now();
            var sb = new StringBuilder("Device Manager: disable " + Label + " — " + l.Count + " device(s) on this PC");
            foreach (var d in l) sb.Append("\r\n      " + d.Name + (d.Disabled ? "  (disabled now)" : "") + "  · " + d.Id);
            sb.Append("\r\n      undo: only the devices ArchTweaker disabled are enabled again");
            return sb.ToString();
        }
    }

    // Finds the service on first use, so the catalog build doesn't read every service key at startup.
    public class PlmSvcOp : Op
    {
        static string name; static bool looked;
        public static string Name { get { if (!looked) { name = Catalog.PlmService(); looked = true; } return name; } }
        SvcOp Inner { get { return new SvcOp { Name = Name ?? "IntelPlatformLicenseManager", On = 3, Off = 2 }; } }
        public override bool IsOn() { return Inner.IsOn(); }
        public override void Apply() { if (Name != null) Inner.Apply(); }
        public override void Revert() { if (Name != null) Inner.Revert(); }
        public override string Source { get { return "service"; } }
        public override string Describe() { return Name == null ? "Intel Platform License Manager service: not installed" : Inner.Describe(); }
    }
}
