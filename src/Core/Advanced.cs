// Advanced engine pieces: power-plan settings, per-device registry tweaks, timer resolution service.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------- Power settings of the ACTIVE plan (2.6: read/written through powrprof, not powercfg.exe) ----------
    // powercfg /qh runs once per session only to learn the alias → GUID map (SUB_PROCESSOR, PROCTHROTTLEMIN …),
    // so no GUID is hardcoded. Values are then read and written with the Power* API: no process per setting.
    public static class PowerCache
    {
        static Dictionary<string, string> alias;                 // alias (upper) -> GUID
        static Dictionary<string, uint[]> qh;                    // "SUBGUID|SETGUID" -> AC/DC from powercfg /qh (fallback when the API read fails)
        static readonly Dictionary<string, uint[]> vals = new Dictionary<string, uint[]>(StringComparer.OrdinalIgnoreCase);
        static Guid? scheme;
        static DateTime aliasFailedAt = DateTime.MinValue;
        static int batch; static bool pendingActivate;
        static readonly object sync = new object();
        static readonly Regex G = new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        static readonly Regex Alias = new Regex(":\\s*([A-Z][A-Z0-9_]+)\\s*$");

        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
        [DllImport("powrprof.dll")] static extern uint PowerReadSettingAttributes(ref Guid sub, ref Guid setting);
        [DllImport("powrprof.dll")] static extern uint PowerWriteSettingAttributes(ref Guid sub, ref Guid setting, uint attrs);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);

        public static void Invalidate() { lock (sync) { vals.Clear(); scheme = null; aliasFailedAt = DateTime.MinValue; } StateCache.Clear(); }

        // 3.6.4: active plan GUID from the power API (no powercfg process); powercfg only if the API fails
        public static string ActiveScheme()
        {
            try
            {
                IntPtr p;
                if (PowerGetActiveScheme(IntPtr.Zero, out p) == 0)
                    try { return ((Guid)Marshal.PtrToStructure(p, typeof(Guid))).ToString(); } finally { LocalFree(p); }
            }
            catch { }
            var m = G.Match(Shell.Run("powercfg.exe", "/getactivescheme", true) ?? "");
            return m.Success ? m.Value : "";
        }

        static int Indent(string l) { int i = 0; while (i < l.Length && l[i] == ' ') i++; return i; }

        // Learns aliases from powercfg's own listing (language independent: aliases are never localised).
        internal static Dictionary<string, string> ParseAliases(string outp)
        {
            var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string lastG = null; bool first = true, expect = false;
            foreach (var raw in outp.Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0) continue;
                var gm = G.Match(raw);
                if (gm.Success) { if (first) { first = false; continue; } lastG = gm.Value.ToUpperInvariant(); expect = true; continue; }
                if (expect)
                {
                    expect = false;
                    var am = Alias.Match(raw);
                    if (am.Success && raw.IndexOf("0x", StringComparison.OrdinalIgnoreCase) < 0 && lastG != null) m[am.Groups[1].Value] = lastG;
                }
            }
            return m;
        }

        static readonly Regex AcRx = new Regex("AC Power Setting Index:\\s*0x([0-9a-fA-F]+)"), DcRx = new Regex("DC Power Setting Index:\\s*0x([0-9a-fA-F]+)");
        internal static Dictionary<string, uint[]> ParseValues(string outp)
        {
            var m = new Dictionary<string, uint[]>(StringComparer.OrdinalIgnoreCase);
            string sub = null, set = null; uint? ac = null; bool first = true;
            foreach (var raw in outp.Replace("\r", "").Split('\n'))
            {
                var gm = G.Match(raw);
                if (gm.Success)
                {
                    if (first) { first = false; continue; }
                    // subgroup lines are indented less than setting lines
                    if (Indent(raw) <= 2) { sub = gm.Value.ToUpperInvariant(); set = null; } else set = gm.Value.ToUpperInvariant();
                    ac = null; continue;
                }
                if (sub == null || set == null) continue;
                var a = AcRx.Match(raw); if (a.Success) { ac = Convert.ToUInt32(a.Groups[1].Value, 16); continue; }
                var d = DcRx.Match(raw); if (d.Success && ac.HasValue) m[sub + "|" + set] = new[] { ac.Value, Convert.ToUInt32(d.Groups[1].Value, 16) };
            }
            return m;
        }

        static string Resolve(string id)
        {
            if (G.IsMatch(id) && id.Length == 36) return id;
            if (alias == null)
            {
                if ((DateTime.Now - aliasFailedAt).TotalSeconds < 30) return null;   // 3.6.4: a failed read is retried every 30 s, not once per power setting
                string outp = Shell.Run("powercfg.exe", "/qh SCHEME_CURRENT", true) ?? "";
                var m = ParseAliases(outp);
                if (m.Count == 0) { aliasFailedAt = DateTime.Now; Log.W("Power aliases: powercfg /qh returned nothing usable — will retry in 30 s"); return null; }   // don't cache a failure
                alias = m; qh = ParseValues(outp);
                Log.W("Power aliases learned: {0}", alias.Count);
            }
            string g; return alias.TryGetValue(id, out g) ? g : null;
        }

        static bool Ids(string sub, string setting, out Guid s, out Guid t, out Guid sc)
        {
            s = t = sc = Guid.Empty;
            string a = Resolve(sub), b = Resolve(setting);
            if (a == null || b == null) return false;
            if (scheme == null)
            {
                IntPtr p;
                if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0) return false;
                try { scheme = (Guid)Marshal.PtrToStructure(p, typeof(Guid)); } finally { LocalFree(p); }
            }
            s = new Guid(a); t = new Guid(b); sc = scheme.Value; return true;
        }

        public static uint[] Get(string sub, string setting)
        {
            lock (sync)
            {
                string k = sub + "|" + setting; uint[] v;
                if (vals.TryGetValue(k, out v)) return v;
                Guid s, t, sc; uint ac, dc;
                try
                {
                    if (!Ids(sub, setting, out s, out t, out sc)) return null;      // not cached: aliases may load later
                    else if (PowerReadACValueIndex(IntPtr.Zero, ref sc, ref s, ref t, out ac) == 0 && PowerReadDCValueIndex(IntPtr.Zero, ref sc, ref s, ref t, out dc) == 0) v = new[] { ac, dc };
                    else
                    {
                        // no explicit value in this plan: use what powercfg reported (it includes inherited defaults)
                        uint[] q; v = qh != null && qh.TryGetValue(s.ToString().ToUpperInvariant() + "|" + t.ToString().ToUpperInvariant(), out q) ? q : null;
                    }
                }
                catch (Exception ex) { Log.W("  power read {0}: {1}", setting, ex.Message); return null; }
                vals[k] = v; return v;
            }
        }

        // Writes AC + DC on the active plan; optionally clears ATTRIBUTE_HIDE so Power Options shows it. Falls back to powercfg.
        public static void Write(string sub, string setting, uint ac, uint dc, bool unhide)
        {
            lock (sync)
            {
                vals.Remove(sub + "|" + setting);
                Guid s, t, sc;
                try
                {
                    if (Ids(sub, setting, out s, out t, out sc))
                    {
                        if (unhide) { uint at = PowerReadSettingAttributes(ref s, ref t); if ((at & 1u) != 0) PowerWriteSettingAttributes(ref s, ref t, at & ~1u); }
                        if (PowerWriteACValueIndex(IntPtr.Zero, ref sc, ref s, ref t, ac) == 0 && PowerWriteDCValueIndex(IntPtr.Zero, ref sc, ref s, ref t, dc) == 0)
                        {
                            if (qh != null) qh[s.ToString().ToUpperInvariant() + "|" + t.ToString().ToUpperInvariant()] = new[] { ac, dc };
                            Activate(); return;
                        }
                    }
                }
                catch (Exception ex) { Log.W("  power API {0}: {1} (using powercfg)", setting, ex.Message); }
                if (unhide) Shell.Run("powercfg.exe", "-attributes " + sub + " " + setting + " -ATTRIB_HIDE", true);
                Shell.Run("powercfg.exe", "/setacvalueindex SCHEME_CURRENT " + sub + " " + setting + " " + ac, true);
                Shell.Run("powercfg.exe", "/setdcvalueindex SCHEME_CURRENT " + sub + " " + setting + " " + dc, true);
                Activate();
            }
        }

        // Re-applying the active scheme makes Windows pick up index changes. Inside Batch it runs once at the end.
        public static void Activate()
        {
            lock (sync)
            {
                if (batch > 0) { pendingActivate = true; return; }
                Guid sc = Guid.Empty; bool ok = false;
                try
                {
                    IntPtr p;
                    if (PowerGetActiveScheme(IntPtr.Zero, out p) == 0)
                    {
                        try { sc = (Guid)Marshal.PtrToStructure(p, typeof(Guid)); } finally { LocalFree(p); }
                        ok = PowerSetActiveScheme(IntPtr.Zero, ref sc) == 0;
                    }
                }
                catch { }
                if (!ok) Shell.Run("powercfg.exe", "/setactive SCHEME_CURRENT", true);
                vals.Clear(); scheme = null;
            }
        }

        // Every apply/undo loop runs inside Batch: power activation happens once at the end, and tweak states
        // are not cached while the system is half-changed (StateCache hold).
        public static void Batch(Action work)
        {
            lock (sync) batch++;
            StateCache.BeginHold();
            try { work(); }
            finally
            {
                bool go;
                lock (sync) { batch--; go = batch == 0 && pendingActivate; if (go) pendingActivate = false; }
                if (go) Activate();
                StateCache.EndHold();
            }
        }
    }

    // One setting of the active power plan, applied to both AC (plugged in) and DC (battery).
    public class PowerOp : Op
    {
        public string Sub, Setting, Label, ValueLabel; public uint Value;
        const string PREV = @"Software\ArchTweaker\PowerPrev";
        string Key { get { return Sub + "|" + Setting; } }
        public bool Exists { get { return PowerCache.Get(Sub, Setting) != null; } }
        public override bool IsOn()
        {
            var v = PowerCache.Get(Sub, Setting);
            return v == null || (v[0] == Value && v[1] == Value);
        }
        public override void Apply()
        {
            var cur = PowerCache.Get(Sub, Setting);
            if (cur == null) { Log.W("  (power setting {0} not present on this PC — skipped)", Label); return; }
            if (Reg.Get(Hive.HKCU, PREV, Key) == null)
                using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(PREV)) k.SetValue(Key, cur[0] + "," + cur[1]);
            PowerCache.Write(Sub, Setting, Value, Value, true);
        }
        public override void Revert()
        {
            object prev = Reg.Get(Hive.HKCU, PREV, Key);
            if (prev == null) return;
            var p = prev.ToString().Split(',');
            uint ac, dc;
            if (p.Length != 2 || !uint.TryParse(p[0], out ac) || !uint.TryParse(p[1], out dc)) { Log.W("  bad saved power value for {0}: {1}", Label, prev); return; }
            PowerCache.Write(Sub, Setting, ac, dc, false);
            using (var b = Reg.Base(Hive.HKCU)) using (var k = b.OpenSubKey(PREV, true)) if (k != null) k.DeleteValue(Key, false);
        }
        public override string Source { get { return "power plan (powrprof)"; } }
        public override string Describe()
        {
            return "Power plan (active): " + Label + " → " + ValueLabel + "   [plugged in + battery]\r\n      same as: powercfg /setacvalueindex SCHEME_CURRENT " + Sub + " " + Setting + " " + Value + "\r\n      undo: previous value restored";
        }
    }

    // ---------- Registry values on every matching device (NICs, USB, GPU...) with exact per-value restore ----------
    public static class DevStore
    {
        const string P = @"SOFTWARE\ArchTweaker\DevPrev";
        public static void Save(string path, string name) { Save(Hive.HKLM, path, name); }
        public static void Restore(string path, string name) { Restore(Hive.HKLM, path, name); }
        static string Id(Hive h, string path, string name) { return (h == Hive.HKLM ? "" : h + ":") + path + "|" + name; }   // 3.7: HKLM ids unchanged
        public static void Save(Hive h, string path, string name)
        {
            string id = Id(h, path, name);
            if (Reg.Get(Hive.HKLM, P, id) != null) return;
            object cur = Reg.Get(h, path, name);
            string s = cur == null ? "-" : Reg.GetKind(h, path, name) + "|" + Ser(cur);
            using (var b = Reg.Base(Hive.HKLM)) using (var k = b.CreateSubKey(P)) k.SetValue(id, s);
        }
        public static void Restore(Hive h, string path, string name)
        {
            string id = Id(h, path, name);
            object s = Reg.Get(Hive.HKLM, P, id);
            if (s == null) return;
            string str = s.ToString();
            try
            {
                if (str == "-") Reg.DeleteValue(h, path, name);
                else
                {
                    int i = str.IndexOf('|');
                    var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), str.Substring(0, i));
                    Reg.Set(h, path, name, kind, De(kind, str.Substring(i + 1)));
                }
            }
            catch (Exception ex) { Log.W("Restore failed {0}\\{1}: {2}", path, name, ex.Message); }
            using (var b = Reg.Base(Hive.HKLM)) using (var k = b.OpenSubKey(P, true)) if (k != null) k.DeleteValue(id, false);
        }
        static string Ser(object o)
        {
            if (o is byte[]) return Convert.ToBase64String((byte[])o);
            if (o is string[]) return Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\0", (string[])o)));
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(o.ToString()));
        }
        static object De(RegistryValueKind k, string b64)
        {
            byte[] d = Convert.FromBase64String(b64);
            string s = Encoding.UTF8.GetString(d);
            switch (k)
            {
                case RegistryValueKind.Binary: return d;
                case RegistryValueKind.DWord: return int.Parse(s);
                case RegistryValueKind.QWord: return long.Parse(s);
                case RegistryValueKind.MultiString: return s.Split('\0');
                default: return s;
            }
        }
    }

    public class DeviceRegOp : Op
    {
        public Func<IEnumerable<string>> Targets;   // HKLM-relative key paths
        public string Name, Label; public RegistryValueKind Kind; public object Value; public bool OnlyIfExists;
        // Walking the device class keys is the slow part of a state check, and Count, IsOn and Describe all need it:
        // the list is kept for 30 s, or until Flush() (called before every apply/undo and on a state refresh).
        static int gen;
        int liveGen = -1; DateTime liveAt; List<string> live;
        readonly object liveLock = new object();
        public static void Flush() { System.Threading.Interlocked.Increment(ref gen); }
        List<string> Live()
        {
            lock (liveLock)
            {
                int g = System.Threading.Thread.VolatileRead(ref gen);
                if (live != null && liveGen == g && (DateTime.UtcNow - liveAt).TotalSeconds < 30) return new List<string>(live);
                var l = new List<string>();
                try
                {
                    foreach (var t in Targets())
                        if (!OnlyIfExists || Reg.Get(Hive.HKLM, t, Name) != null) l.Add(t);
                }
                catch (Exception ex) { Log.W("  device list {0}: {1}", Label, ex.Message); return l; }   // not cached
                live = l; liveGen = g; liveAt = DateTime.UtcNow;
                return new List<string>(l);
            }
        }
        public int Count { get { return Live().Count; } }
        public override bool IsOn()
        {
            var l = Live();
            return l.All(t => Reg.Norm(Reg.Get(Hive.HKLM, t, Name)) == Reg.Norm(Value));   // no matching device = not applicable
        }
        public override void Apply()
        {
            int n = 0; var live = Live(); string first = null;
            foreach (var t in live)
            {
                try { DevStore.Save(t, Name); Reg.Set(Hive.HKLM, t, Name, Kind, Value); n++; }
                catch (Exception ex) { Log.W("  {0}: {1}", t, ex.Message); if (first == null) first = ex.Message; }
            }
            Log.W("  {0}: {1} device key(s) updated", Label, n);
            // 3.27.1: say when the writes failed, instead of a tweak that looks applied and is not
            if (live.Count > 0 && n == 0) throw new InvalidOperationException(L.F("could not write any of the {0} device key(s): {1}", live.Count, first));
            if (n < live.Count) Log.Say("{0}: {1} of {2} device key(s) updated. The log says why the others failed.", Label, n, live.Count);
        }
        public override void Revert() { foreach (var t in Live()) DevStore.Restore(t, Name); }
        public override string Source { get { return "device registry"; } }
        public override string Describe()
        {
            var l = Live();
            var sb = new StringBuilder();
            sb.Append("Device registry: " + Label + " — sets \"" + Name + "\" = " + Fmt(Value) + " on " + l.Count + " key(s) found on this PC");
            foreach (var t in l.Take(6)) sb.Append("\r\n      HKLM\\" + t);
            if (l.Count > 6) sb.Append("\r\n      … and " + (l.Count - 6) + " more");
            sb.Append("\r\n      undo: each original value restored exactly");
            return sb.ToString();
        }
        static string Fmt(object v) { return v is string ? "\"" + v + "\"" : v is byte[] ? BitConverter.ToString((byte[])v) : v.ToString(); }
    }

    public static partial class Devices
    {
        const string CLS = @"SYSTEM\CurrentControlSet\Control\Class\";
        public const string GPU_CLASS = "{4d36e968-e325-11ce-bfc1-08002be10318}";
        public const string NET_CLASS = "{4d36e972-e325-11ce-bfc1-08002be10318}";

        static IEnumerable<string> ClassKeys(string cls)
        {
            foreach (var s in Reg.SubKeys(Hive.HKLM, CLS + cls))
                if (s.Length == 4 && s.All(char.IsDigit)) yield return CLS + cls + "\\" + s;
        }
        public static IEnumerable<string> NvidiaKeys()
        {
            return ClassKeys(GPU_CLASS).Where(k => (Reg.Get(Hive.HKLM, k, "ProviderName") ?? "").ToString().IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        public static IEnumerable<string> AmdGpuKeys()
        {
            return ClassKeys(GPU_CLASS).Where(k => { var p = (Reg.Get(Hive.HKLM, k, "ProviderName") ?? "").ToString(); return p.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("Advanced Micro", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("ATI", StringComparison.Ordinal) >= 0; });
        }
        public static bool HasNvidia() { return NvidiaKeys().Any(); }
        public static bool HasAmdGpu() { return AmdGpuKeys().Any(); }

        // Physical Ethernet / Wi-Fi adapters
        public static IEnumerable<string> NicKeys()
        {
            return ClassKeys(NET_CLASS).Where(k =>
            {
                object t = Reg.Get(Hive.HKLM, k, "*IfType");
                int it = t is int ? (int)t : 0;
                return (it == 6 || it == 71) && Reg.Get(Hive.HKLM, k, "NetCfgInstanceId") != null && Reg.Get(Hive.HKLM, k, "*PhysicalMediaType") != null;
            });
        }

        // Enum\<bus>\<device>\<instance>[\sub]
        public static IEnumerable<string> EnumInstances(params string[] buses)
        {
            foreach (var bus in buses)
            {
                string b = @"SYSTEM\CurrentControlSet\Enum\" + bus;
                foreach (var dev in Reg.SubKeys(Hive.HKLM, b))
                    foreach (var inst in Reg.SubKeys(Hive.HKLM, b + "\\" + dev))
                        yield return b + "\\" + dev + "\\" + inst;
            }
        }
        public static IEnumerable<string> DeviceParams(params string[] buses)
        {
            return EnumInstances(buses).Select(i => i + @"\Device Parameters").Where(p => Reg.KeyExists(Hive.HKLM, p));
        }
        public static IEnumerable<string> DeviceWdf(params string[] buses)
        {
            return EnumInstances(buses).Select(i => i + @"\Device Parameters\WDF").Where(p => Reg.KeyExists(Hive.HKLM, p));
        }
        public static IEnumerable<string> GpuMsiKeys()
        {
            return EnumInstances("PCI")
                .Where(i => string.Equals((Reg.Get(Hive.HKLM, i, "ClassGUID") ?? "").ToString(), GPU_CLASS, StringComparison.OrdinalIgnoreCase))
                .Select(i => i + @"\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties");
        }
        public static IEnumerable<string> DiskCacheKeys()
        {
            return EnumInstances("NVME", "SCSI")
                .Where(i => string.Equals((Reg.Get(Hive.HKLM, i, "ClassGUID") ?? "").ToString(), "{4d36e967-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase))
                .Select(i => i + @"\Device Parameters\Disk");
        }
        public static IEnumerable<string> HiddenPowerSettings()
        {
            const string P = @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings";
            foreach (var sub in Reg.SubKeys(Hive.HKLM, P))
                foreach (var s in Reg.SubKeys(Hive.HKLM, P + "\\" + sub))
                    yield return P + "\\" + sub + "\\" + s;
        }
    }

    // ---------- Timer resolution ----------
    public static class TimerRes
    {
        [DllImport("ntdll.dll")] static extern int NtSetTimerResolution(uint desired, bool set, out uint current);
        [DllImport("ntdll.dll")] static extern int NtQueryTimerResolution(out uint min, out uint max, out uint current);
        public const string SVC = "ArchTimerRes";

        public static string Query()
        {
            uint mn, mx, cur;
            try { NtQueryTimerResolution(out mn, out mx, out cur); } catch (Exception ex) { return "Timer resolution: unavailable (" + ex.Message + ")"; }
            return string.Format("Timer resolution — current {0:0.000} ms (coarsest {1:0.000} ms, finest {2:0.000} ms)", cur / 10000.0, mn / 10000.0, mx / 10000.0);
        }
        public static double CurrentMs()
        {
            try { uint mn, mx, cur; NtQueryTimerResolution(out mn, out mx, out cur); return cur / 10000.0; } catch { return -1; }
        }
        public static void Request(uint units100ns) { uint c; NtSetTimerResolution(units100ns, true, out c); }
        public static void Release(uint units100ns) { uint c; NtSetTimerResolution(units100ns, false, out c); }

        public static string InstalledExe { get { return Path.Combine(Paths.Root, @"bin\ArchTweaker.exe"); } }

        public static void Install()
        {
            Shell.Run("sc.exe", "stop " + SVC, true);
            if (Secure.InstallExe() == null) { Log.Say("Timer service not installed: ArchTweaker couldn't secure the program copy for services (see log)."); return; }   // 3.2.1
            if (!Svc.Exists(SVC))
                Shell.Run("sc.exe", "create " + SVC + " binPath= \"\\\"" + InstalledExe + "\\\" /timerservice\" start= auto DisplayName= \"ArchTweaker Timer Resolution\"", true);
            else   // an existing service is pointed back at the secured copy, whatever its path was
                Shell.Run("sc.exe", "config " + SVC + " binPath= \"\\\"" + InstalledExe + "\\\" /timerservice\"", true);
            Shell.Run("sc.exe", "description " + SVC + " \"Keeps the Windows timer at 0.5 ms for lower input and frame latency. Installed by ArchTweaker.\"", true);
            Shell.Run("sc.exe", "start " + SVC, true);
        }
        public static void Uninstall()
        {
            Shell.Run("sc.exe", "stop " + SVC, true);
            Shell.Run("sc.exe", "delete " + SVC, true);
        }
    }

    public class TimerResService : ServiceBase
    {
        public TimerResService() { ServiceName = TimerRes.SVC; CanStop = true; }
        protected override void OnStart(string[] args) { TimerRes.Request(5000); }
        protected override void OnStop() { TimerRes.Release(5000); }
    }
}
