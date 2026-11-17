// Live system probes (bcdedit, PowerShell network/memory/storage state) and the whole-PC scan.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ArchTweaker
{
    public static class Probe
    {
        static Dictionary<string, string> bcd, ps;
        static readonly object sync = new object();

        public static void Invalidate() { lock (sync) { bcd = null; ps = null; } StateCache.Clear(); }

        // ---- boot configuration: element names are never translated, so this works on any Windows language
        static readonly string[] No = { "no", "нет", "nein", "non", "nee", "não", "nie", "否", "いいえ", "아니요", "off" };
        static Dictionary<string, string> Bcd()
        {
            lock (sync)
            {
                if (bcd != null) return bcd;
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in Shell.Run("bcdedit.exe", "/enum {current}", true).Replace("\r", "").Split('\n'))
                {
                    var line = raw.Trim();
                    int i = line.IndexOf(' ');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i).Trim();
                    if (k.All(c => char.IsLetterOrDigit(c)) && !d.ContainsKey(k)) d[k] = v;
                }
                bcd = d;
                return d;
            }
        }
        public static bool BcdHas(string element) { return Bcd().ContainsKey(element); }
        public static string BcdValue(string element) { string v; return Bcd().TryGetValue(element, out v) ? v : null; }
        public static bool BcdYes(string element)
        {
            string v = BcdValue(element);
            return v != null && !No.Contains(v.Trim().ToLowerInvariant());
        }
        public static string BcdRaw() { return Shell.Run("bcdedit.exe", "/enum {current}", true).Trim(); }

        // ---- PowerShell probe: enum values are always English
        const string Script =
            "$o=[ordered]@{};" +
            "try{$t=Get-NetTCPSetting -SettingName Internet -EA Stop;$o.AutoTuning=$t.AutoTuningLevelLocal;$o.Heuristics=$t.ScalingHeuristics;$o.Congestion=$t.CongestionProvider;$o.InitialRto=$t.InitialRtoMs}catch{};" +
            "try{$g=Get-NetOffloadGlobalSetting -EA Stop;$o.RSS=$g.ReceiveSideScaling;$o.RSC=$g.ReceiveSegmentCoalescing}catch{};" +
            "try{$o.Teredo=(Get-NetTeredoConfiguration -EA Stop).Type}catch{};" +
            "try{$m=Get-MMAgent -EA Stop;$o.MemCompression=$m.MemoryCompression;$o.PageCombining=$m.PageCombining;$o.Prefetcher=$m.ApplicationPreLaunch}catch{};" +
            "try{$o.ReservedStorage=(Get-WindowsReservedStorageState -EA Stop).ReservedStorageState}catch{};" +
            "try{$o.DNS=((Get-DnsClientServerAddress -AddressFamily IPv4 -EA Stop|?{$_.ServerAddresses}|%{$_.ServerAddresses}|Select -Unique) -join ',')}catch{};" +
            "try{$o.NicPower=((Get-NetAdapter -Physical -EA Stop|Get-NetAdapterPowerManagement -EA SilentlyContinue|%{$_.AllowComputerToTurnOffDevice}) -join ',')}catch{};" +
            "try{$o.LLDP=((Get-NetAdapterBinding -ComponentID ms_lldp -EA Stop|%{$_.Enabled}) -join ',')}catch{};" +
            "try{$o.QoS=((Get-NetAdapterBinding -ComponentID ms_pacer -EA Stop|%{$_.Enabled}) -join ',')}catch{};" +
            "try{$o.Implat=((Get-NetAdapterBinding -ComponentID ms_implat -EA Stop|%{$_.Enabled}) -join ',')}catch{};" +
            "try{$o.IPv6=((Get-NetAdapterBinding -ComponentID ms_tcpip6 -EA Stop|%{$_.Enabled}) -join ',')}catch{};" +
            "try{$c=Get-CimInstance Win32_ComputerSystem -EA Stop;$o.HypervisorPresent=$c.HypervisorPresent}catch{};" +
            "try{$v=Get-CimInstance -Namespace root\\Microsoft\\Windows\\DeviceGuard -ClassName Win32_DeviceGuard -EA Stop;$o.VbsRunning=$v.VirtualizationBasedSecurityStatus;$o.HvciRunning=($v.SecurityServicesRunning -contains 2)}catch{};" +
            "$o.GetEnumerator()|%{\"$($_.Key)=$($_.Value)\"}";

        static Dictionary<string, string> Ps()
        {
            lock (sync)
            {
                if (ps != null) return ps;
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in Shell.PS(Script).Replace("\r", "").Split('\n'))
                {
                    int i = raw.IndexOf('=');
                    if (i > 0) d[raw.Substring(0, i).Trim()] = raw.Substring(i + 1).Trim();
                }
                ps = d;
                return d;
            }
        }
        public static string Get(string key) { string v; return Ps().TryGetValue(key, out v) ? v : null; }
        public static bool Is(string key, string value) { string v = Get(key); return v != null && v.Equals(value, StringComparison.OrdinalIgnoreCase); }
        public static bool AllAre(string key, string value)
        {
            string v = Get(key);
            if (string.IsNullOrEmpty(v)) return false;
            var parts = v.Split(',').Where(p => p.Length > 0 && !p.Equals("Unsupported", StringComparison.OrdinalIgnoreCase)).ToList();
            return parts.Count > 0 && parts.All(p => p.Equals(value, StringComparison.OrdinalIgnoreCase));
        }
        public static IEnumerable<KeyValuePair<string, string>> All() { return Ps(); }

        public static void Warm() { try { Bcd(); Ps(); } catch { } }
    }

    public static partial class Catalog
    {
        static void Probe_(string id, Func<bool> f, string how)
        {
            var t = all.FirstOrDefault(x => x.Id == id);
            if (t == null) return;
            t.Detect = f; t.DetectHow = how;
        }

        // Live detection for tweaks that are applied by commands, so the scan shows the PC's REAL state
        // even when something other than ArchTweaker changed it.
        static void How_(string id, string how) { var t = all.FirstOrDefault(x => x.Id == id); if (t != null) t.DetectHow = how; }

        static void AttachProbes()
        {
            How_("hibernate", "registry: Control\\Power\\HibernateEnabled");
            How_("nagle", "registry: TcpAckFrequency/TCPNoDelay on every active interface");
            How_("netbios", "registry: NetBT interfaces NetbiosOptions");
            How_("svchostsplit", "registry: SvcHostSplitThresholdInKB vs installed RAM");
            How_("pp_plan", "powercfg /getactivescheme");
            How_("il_timerres", "service ArchTimerRes + registry GlobalTimerResolutionRequests");
            How_("sys_pauseupdates", "registry: WindowsUpdate\\UX\\Settings PauseUpdatesExpiryTime");
            Probe_("cmd_dynamictick", () => Probe.BcdYes("disabledynamictick"), "bcdedit /enum {current} → disabledynamictick");
            Probe_("cmd_bootmenu", () => (Probe.BcdValue("bootmenupolicy") ?? "").Trim().Equals("Legacy", StringComparison.OrdinalIgnoreCase), "bcdedit /enum {current} → bootmenupolicy");
            Probe_("cmd_hypervisor", () => (Probe.BcdValue("hypervisorlaunchtype") ?? "").Trim().Equals("Off", StringComparison.OrdinalIgnoreCase), "bcdedit /enum {current} → hypervisorlaunchtype");
            Probe_("cmd_trim", () => Reg.Norm(Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "DisableDeleteNotification")) != "1", "registry: FileSystem\\DisableDeleteNotification (same as fsutil behavior query disabledeletenotify)");
            Probe_("cmd_autotuning", () => Probe.Is("AutoTuning", "Normal"), "PowerShell: Get-NetTCPSetting Internet → AutoTuningLevelLocal");
            Probe_("cmd_rsc", () => Probe.Is("RSC", "Disabled"), "PowerShell: Get-NetOffloadGlobalSetting → ReceiveSegmentCoalescing");   // 3.5
            Probe_("cmd_timerclean", () => !Probe.BcdHas("useplatformclock") && !Probe.BcdHas("useplatformtick") && !Probe.BcdHas("tscsyncpolicy"), "bcdedit /enum {current} → useplatformclock, useplatformtick and tscsyncpolicy absent");
            Probe_("cmd_rss", () => Probe.Is("RSS", "Enabled"), "PowerShell: Get-NetOffloadGlobalSetting → ReceiveSideScaling");
            Probe_("cmd_heuristics", () => Probe.Is("Heuristics", "Disabled"), "PowerShell: Get-NetTCPSetting Internet → ScalingHeuristics");
            Probe_("cmd_teredo", () => Probe.Is("Teredo", "Disabled"), "PowerShell: Get-NetTeredoConfiguration → Type");
            Probe_("cmd_fastopen", () => NetshGlobal.FastOpen() ?? State.Get("cmd_fastopen"), "netsh int tcp show global → Fast Open (remembered state if the output isn't English)");   // 3.6.2
            Probe_("cmd_dupplans", () => PowerPlans.Duplicates().Count == 0, "powercfg /list → no two plans with the same name (built-in and active plans excepted)");
            Probe_("memcompress", () => Probe.Is("MemCompression", "False"), "PowerShell: Get-MMAgent → MemoryCompression");
            Probe_("reservedstorage", () => Probe.Is("ReservedStorage", "Disabled"), "PowerShell: Get-WindowsReservedStorageState");
            Probe_("dnscloudflare", () => (Probe.Get("DNS") ?? "").Split(',').Contains("1.1.1.1"), "PowerShell: Get-DnsClientServerAddress → DNS servers");
            Probe_("nicpower", () => Probe.AllAre("NicPower", "Disabled"), "PowerShell: Get-NetAdapterPowerManagement → AllowComputerToTurnOffDevice");
            Probe_("net_bindings", () => Probe.AllAre("LLDP", "False") && Probe.AllAre("Implat", "False"), "PowerShell: Get-NetAdapterBinding ms_lldp / ms_implat");
            Probe_("net_pacer", () => Probe.AllAre("QoS", "False"), "PowerShell: Get-NetAdapterBinding ms_pacer");   // 3.30
            Probe_("net_ipv6off", () => Probe.AllAre("IPv6", "False"), "PowerShell: Get-NetAdapterBinding ms_tcpip6");
        }
    }

    // ---- whole-PC scan
    public class ScanRow
    {
        public Tweak T; public string Status, Source;
    }

    public static class Scanner
    {
        public static List<ScanRow> Run(Action<int, int> progress)
        {
            Probe.Invalidate(); PowerCache.Invalidate();
            Probe.Warm();
            var rows = new List<ScanRow>();
            int i = 0, n = Catalog.All.Count;
            foreach (var t in Catalog.All)
            {
                var r = new ScanRow { T = t, Source = t.DetectSource };
                try
                {
                    if (!t.Applicable) r.Status = "N/A";
                    else r.Status = t.IsApplied() ? "ON" : "OFF";
                }
                catch (Exception ex) { r.Status = "ERROR"; r.Source += " (" + ex.Message + ")"; }
                rows.Add(r);
                if (progress != null) progress(++i, n);
            }
            return rows;
        }

        public static string RawState()
        {
            var sb = new StringBuilder();
            Action<string> H = h => sb.Append("\r\n==================== ").Append(h).Append(" ====================\r\n");
            sb.Append("ArchTweaker " + Program.Version + " system state report — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\r\n" + OSInfo.Name + "\r\n");

            H("BOOT CONFIGURATION (bcdedit /enum {current})");
            sb.Append(Probe.BcdRaw()).Append("\r\n");

            H("NETWORK / MEMORY / STORAGE (PowerShell)");
            foreach (var kv in Probe.All()) sb.Append(kv.Key.PadRight(20)).Append(kv.Value).Append("\r\n");
            sb.Append(Shell.Run("netsh.exe", "int tcp show global", true).Trim()).Append("\r\n");

            H("POWER PLAN");
            sb.Append(Shell.Run("powercfg.exe", "/getactivescheme", true).Trim()).Append("\r\n");
            foreach (var s in new[] { "PROCTHROTTLEMIN", "PROCTHROTTLEMAX", "PERFBOOSTMODE", "PERFEPP", "CPMINCORES", "IDLEDISABLE", "PERFINCPOL", "LATENCYHINTPERF", "SCHEDPOLICY" })
            {
                var v = PowerCache.Get("SUB_PROCESSOR", s);
                sb.Append(("  " + s).PadRight(22)).Append(v == null ? "(not present)" : "AC " + v[0] + " / DC " + v[1]).Append("\r\n");
            }
            var a = PowerCache.Get("SUB_PCIEXPRESS", "ASPM"); sb.Append("  ASPM".PadRight(22)).Append(a == null ? "(not present)" : "AC " + a[0] + " / DC " + a[1]).Append("\r\n");
            sb.Append(Shell.Run("powercfg.exe", "/a", true).Trim()).Append("\r\n");

            H("TIMER");
            sb.Append(TimerRes.Query()).Append("\r\n");
            sb.Append("GlobalTimerResolutionRequests = " + (Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests") ?? "(not set)")).Append("\r\n");
            sb.Append("Timer service (" + TimerRes.SVC + "): " + (Svc.Exists(TimerRes.SVC) ? "installed" : "not installed")).Append("\r\n");

            H("KEY REGISTRY VALUES");
            Action<Hive, string, string> R = (h, p, n) => sb.Append(("  " + n).PadRight(36)).Append(Reg.Norm(Reg.Get(h, p, n)) ?? "(not set)").Append("   [" + h + "\\" + p + "]\r\n");
            R(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation");
            R(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness");
            R(Hive.HKLM, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex");
            R(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
            R(Hive.HKCU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
            R(Hive.HKCU, @"System\GameConfigStore", "GameDVR_Enabled");
            R(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff");
            R(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "FeatureSettingsOverride");
            R(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
            R(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB");
            R(Hive.HKCU, @"Control Panel\Mouse", "MouseSpeed");
            R(Hive.HKLM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry");

            H("SERVICES");
            try
            {
                var sv = ServicesModule.Load();
                sb.Append(sv.Count + " services — " + sv.Count(s => s.Status == "Running") + " running, " + sv.Count(s => s.Start == 4) + " disabled\r\n");
                foreach (var s in sv.Where(s => s.Group != "")) sb.Append(("  " + s.Name).PadRight(44)).Append(Svc.StartName(s.Start, s.Delayed).PadRight(22)).Append(s.Status).Append("\r\n");
            }
            catch (Exception ex) { sb.Append(ex.Message + "\r\n"); }

            H("ARCHTWEAKER");
            sb.Append("Hosts block: " + (HostsModule.IsActive() ? "active" : "not active") + "\r\n");
            sb.Append("Undo journal entries: " + Journal.Count + "\r\n");
            return sb.ToString();
        }
    }
}
