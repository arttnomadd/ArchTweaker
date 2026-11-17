// ArchTweaker: Tweaks: network adapters, input, NVIDIA profile groups, service tiers, privacy (3.5).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class Catalog
    {
        static readonly string[] SafeTierSvcs = { "PhoneSvc", "ALG", "CertPropSvc", "cnghwassist", "DevicesFlowUserSvc", "KtmRm", "lltdsvc", "lmhosts", "MessagingService", "MSDTC", "NcdAutoSetup",
            "p2pimsvc", "p2psvc", "PeerDistSvc", "PerfHost", "pla", "PNRPAutoReg", "PNRPsvc", "SCardSvr", "ScDeviceEnum", "WarpJITSvc", "WFDSConMgrSvc",
            "AssignedAccessManagerSvc", "AxInstSV", "NetTcpPortSharing", "shpamsvc", "SmsRouter", "SNMPTRAP", "Wecsvc", "wmiApSrv" };
        static readonly string[] WarningTierSvcs = { "icssvc", "iphlpsvc", "IpxlatCfgSvc", "LanmanWorkstation", "PcaSvc", "PolicyAgent", "QWAVE", "RasMan", "SCPolicySvc", "UevAgentService", "WbioSrvc" };
        static readonly string[] PermPersonal = { "contacts", "appointments", "email", "phoneCall", "phoneCallHistory", "chat", "userAccountInformation", "userDataTasks", "userNotificationListener" };
        static readonly string[] PermFiles = { "documentsLibrary", "picturesLibrary", "videosLibrary", "musicLibrary", "downloadsFolder", "broadFileSystemAccess" };
        static readonly string[] PermDevice = { "appDiagnostics", "radios", "bluetoothSync", "graphicsCaptureWithoutBorder" };

        // latency rules (moderate) and power rules (safe) for the network adapter writer
        internal static readonly string[][] NicLatencyRules =
        {
            new[] { "*FlowControl", "=0" },
            new[] { "*LsoV2IPv4", "=0" }, new[] { "*LsoV2IPv6", "=0" }, new[] { "*RscIPv4", "=0" }, new[] { "*RscIPv6", "=0" },
            new[] { "*RSS", "=1" }, new[] { "*JumboPacket", "min" }, new[] { "*ReceiveBuffers", "cap:1024" }, new[] { "*TransmitBuffers", "cap:512" },
        };
        internal static readonly string[][] NicPowerRules =
        {
            new[] { "*PMARPOffload", "=0" }, new[] { "*PMNSOffload", "=0" }, new[] { "*SelectiveSuspend", "=0" }, new[] { "*DeviceSleepOnDisconnect", "=0" },
            new[] { "AutoDisableGigabit", "=0" }, new[] { "WolShutdownLinkSpeed", "=2" }, new[] { "EnablePME", "=0" }, new[] { "ReduceSpeedOnPowerDown", "=0" },
        };

        static Tweak Perms(Tweak t, string[] caps)
        {
            foreach (var c in caps) t.Sz(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + c, "Value", "Deny", "Allow");
            return t;
        }
        static Tweak SvcTier(Tweak t, IEnumerable<string> names)
        {
            foreach (var n in names)
            {
                t.Ops.Add(new SvcPrevOp { Name = n });
                foreach (var inst in Svc.Instances(n)) t.Ops.Add(new SvcPrevOp { Name = inst });
            }
            return t;
        }

        static void AddV35()
        {
            string P = Cat.Privacy, IL = Cat.Input, GD = Cat.Gpu, N = Cat.Network, TS = Cat.TasksServices, X = DangerZone.CAT;
            string safe = PRESET_SAFE, game = PRESET_GAMING, priv = PRESET_PRIVACY;
            const string MOUSE = @".DEFAULT\Control Panel\Mouse";

            // ---------------- NETWORK ADAPTER ----------------
            var nic = T("net_nicadv", N, "Network adapter: no flow control or packet batching", "On every physical Ethernet / Wi-Fi adapter: flow control, large-send and receive coalescing off, RSS on. Jumbo frames go to the driver minimum, and receive / transmit buffers go to 1024 / 512. Interrupt moderation stays at the driver default (usually Adaptive). ArchTweaker writes only settings that the driver offers. The adapter restarts after a reboot.")
                .Info("From synez's 2026 network pack. It reads each driver's own list of settings (Ndi\\Params) and skips VPN, capture and virtual adapters. Costs some CPU at very high download speeds.")
                .Requires(() => Devices.PhysicalNicKeys().Any(), "a physical network adapter")
                .R(Risk.Moderate).NeedsReboot();
            nic.Ops.Add(new NicAdvOp { Label = "latency settings", Rules = NicLatencyRules });
            var nicp = T("net_nicpower2", N, "Network adapter: no power-down offloads or link slow-down", "On every physical adapter, ArchTweaker turns off ARP / NS offload while asleep, selective suspend and sleep on cable disconnect. It also turns off automatic gigabit-off, PME wake and speed reduction on power-down. Then the link never drops speed or naps. Adds to 'Network adapters: no power saving'.")
                .Requires(() => Devices.PhysicalNicKeys().Any(), "a physical network adapter")
                .NeedsReboot().In(game);
            nicp.Ops.Add(new NicAdvOp { Label = "power settings", Rules = NicPowerRules });

            // ---------------- DEVICES ----------------
            T("gd_msi_more", GD, "MSI mode for USB and storage controllers", "Turns on message-signalled interrupts for USB host controllers and NVMe / SATA controllers whose driver offers it, like 'MSI mode for the GPU'. Fewer shared interrupt lines, steadier input and disk latency. ArchTweaker restores each original value when you untick.")
                .Dev("USB / storage MSISupported", Devices.UsbStorageMsiKeys, "MSISupported", RegistryValueKind.DWord, 1, false)
                .R(Risk.Moderate).NeedsReboot();
            var wmi = T("gd_wmi_power", GD, "No device can be turned off to save power", "Unticks 'Allow the computer to turn off this device to save power' on every device that has it. This includes hubs, audio and controllers, not only USB and network. Windows sometimes ticks it again after a driver update. The drift check then offers to apply it again.")
                .R(Risk.Moderate).NeedsReboot().In(game);
            wmi.Ops.Add(new WmiPowerOp());

            // ---------------- INPUT ----------------
            T("il_mouse_default", IL, "Mouse acceleration off on the sign-in screen and for new users", "Also writes the no-acceleration mouse values (MouseSpeed 0, thresholds 0) to the default profile. Then the sign-in screen and new Windows accounts move the pointer the same way as your account.")
                .Sz(Hive.HKU, MOUSE, "MouseSpeed", "0", "1")
                .Sz(Hive.HKU, MOUSE, "MouseThreshold1", "0", "6")
                .Sz(Hive.HKU, MOUSE, "MouseThreshold2", "0", "10")
                .In(game);

            // ---------------- NVIDIA PROFILE GROUPS ----------------
            T("gd_nv_drs_texture", GD, "NVIDIA: texture filtering for speed", "Texture filtering quality = High performance, with anisotropic sample, anisotropic filter and trilinear optimizations on. A few % more FPS in texture-heavy games. Distant textures can shimmer a little more.")
                .NvZ(0x00CE2691, 20, "Texture filtering - Quality", "High performance")
                .NvZ(0x00E73211, 1, "Texture filtering - Anisotropic sample optimization", "On")
                .NvZ(0x0084CD70, 1, "Texture filtering - Anisotropic filter optimization", "On")
                .NvZ(0x002ECAF2, 0, "Texture filtering - Trilinear optimization", "On")
                .Requires(() => NvDrs.Available, "NVIDIA driver").R(Risk.Moderate);
            T("gd_nv_drs_misc", GD, "NVIDIA: no Ansel, FXAA, Smooth Motion or CUDA P2 clocks", "Turns off Ansel, driver-forced FXAA, Smooth Motion frame generation and the CUDA 'force P2' memory clock cap. Also hides the driver watermark. You lose nothing you would notice, and games keep full memory clocks.")
                .NvZ(0x1075D972, 0, "Ansel", "Off")
                .NvZ(0x1074C972, 0, "FXAA", "Off")
                .NvZ(0xB0D384C0, 0, "Smooth Motion", "Off")
                .NvZ(0x50166C5E, 0, "CUDA - Force P2 state", "Off")
                .NvZ(0x00380880, 0, "Watermark indicator", "Off")
                .Requires(() => NvDrs.Available, "NVIDIA driver").In(game);
            T("gd_nv_drs_vsync", GD, "NVIDIA: V-Sync and G-SYNC forced off (competitive)", "Forces V-Sync off and turns G-SYNC off in the global profile, for the lowest latency with uncapped FPS and tearing allowed. Don't use it if you play with G-SYNC: untick 'cap frame rate 3 below refresh' too, as that one is for G-SYNC.")
                .NvZ(0x00A879CF, 0x08416747, "Vertical sync", "Force off")
                .NvZ(0x1094F157, 0, "G-SYNC - Global feature", "Off")
                .NvZ(0x1094F1F7, 0, "G-SYNC - Global mode", "Off")
                .NvZ(0x1194F158, 0, "G-SYNC - Application mode", "Off")
                .Requires(() => NvDrs.Available, "NVIDIA driver").R(Risk.Moderate);

            // ---------------- SERVICES ----------------
            SvcTier(T("svc_tier_safe", TS, "Disable rarely used services (safe tier)", "Turns off about 30 services that most gaming PCs never use. Examples: smart cards, phone and SMS relay, peer-to-peer networking, Wi-Fi Direct and casting, distributed transactions and performance-log collectors. ArchTweaker saves each service's startup type and puts it back on undo.")
                .Info("From synez's 2026 'Lower Processes' safe tier, without the services that break Mail / People / Calendar, adding languages or Hyper-V. Wireless display casting (Miracast) and smart-card sign-in stop working.")
                .R(Risk.Moderate), SafeTierSvcs);

            // ---------------- PRIVACY ----------------
            T("priv_phonelink", P, "Phone Link and cross-device features off", "Stops Windows linking to your phone: Phone Link in Start, resuming phone apps on the PC, and cross-device experiences.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableMmx", 0, null)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\CrossDeviceResume\Configuration", "IsResumeAllowed", 0, null)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Mobility", "CrossDeviceEnabled", 0, null)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Mobility", "OptedIn", 0, null)
                .W11().In(priv);
            T("priv_accountnotif", P, "No account and backup nags", "Start and Settings stop showing 'sign in with a Microsoft account', 'back up your PC' and OneDrive sync reminders.")
                .Dw(CU, ADV, "Start_AccountNotifications", 0, null)
                .Dw(CU, ADV, "ShowSyncProviderNotifications", 0, null)
                .Dw(CU, CDM, "SubscribedContent-338393Enabled", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableConsumerAccountStateContent", 1, null)
                .In(safe, priv);
            T("priv_tips", P, "No tips, setup reminders or typing insights", "Turns off online tips in Settings and the 'finish setting up your device' screen. Also turns off typing insights, smart clipboard actions and most-used-app tracking at the screen edges.")
                .Dw(CU, @"Software\Microsoft\input\Settings", "InsightsEnabled", 0, null)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, null)
                .Dw(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "AllowOnlineTips", 0, null)
                .Dw(CU, @"Software\Policies\Microsoft\Windows\EdgeUI", "DisableMFUTracking", 1, null)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\SmartActionPlatform\SmartClipboard", "Disabled", 1, null)
                .In(safe, priv);
            T("priv_langlist", P, "Websites can't read your language list", "Windows stops sharing your list of installed languages with websites, which they use to tailor (and fingerprint) content.")
                .Dw(CU, @"Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1, null)
                .In(safe, priv);
            T("priv_pstelemetry", P, "PowerShell telemetry off", "Sets POWERSHELL_TELEMETRY_OPTOUT for the whole PC, so PowerShell 7 stops sending usage data at start-up.")
                .Sz(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", "POWERSHELL_TELEMETRY_OPTOUT", "1", null)
                .In(priv);
            Perms(T("priv_perm_personal", P, "Store apps can't read contacts, calendar, email or calls", "Denies Store apps access to contacts, calendar, email, calls and call history, messages, tasks, notifications and your account info. Desktop programs aren't affected. You can still allow single apps in Settings → Privacy.").In(priv), PermPersonal);
            Perms(T("priv_perm_files", P, "Store apps can't read your files", "Denies Store apps access to Documents, Pictures, Videos, Music, Downloads and the whole file system. Apps like Photos then can't show your pictures until you allow them in Settings → Privacy. Desktop programs aren't affected."), PermFiles);
            Perms(T("priv_perm_device", P, "Store apps can't read diagnostics, radios or capture without a border", "Denies Store apps access to other apps' diagnostic info, Bluetooth / Wi-Fi radio control, unpaired-device sync and border-free screen capture. Desktop programs aren't affected.").In(priv), PermDevice);

            // ---------------- DANGER ZONE ----------------
            SvcTier(T("dz_svc_warning", X, "Disable more services (warning tier)", "Turns off the services that synez lists as 'warning'. These are IP helper (IPv6 transition), file sharing client, mobile hotspot, VPN connections, IPsec policy and Windows Hello biometrics. The list also has program compatibility assistant, quality-of-service and app sync. ArchTweaker saves each startup type for undo.")
                .Breaking("Network drives and shared folders (LanmanWorkstation), VPNs and dial-up (RasMan) and mobile hotspot. Also IPv6 transition for Xbox networking (iphlpsvc), Windows Hello fingerprint and face sign-in (WbioSrvc), and IPsec / corporate network policies.")
                .NeedsReboot(), WarningTierSvcs);
            T("dz_nv_rebar", X, "NVIDIA: force Resizable BAR in every game", "Turns on Resizable BAR in the global profile, so every game uses it instead of only the ones NVIDIA approved. Some games gain a few %, others lose or crash. Needs Resizable BAR / Above 4G decoding on in the BIOS.")
                .Breaking("Stability and FPS in games NVIDIA left ReBAR off for: some lose performance, stutter or crash. Test your games one by one.")
                .NvZ(0x000F00BA, 1, "rBAR - Feature", "Enabled")
                .NvZ(0x000F00BB, 1, "rBAR - Options", "Enabled for all applications")
                .Requires(() => NvDrs.Available, "NVIDIA driver");
        }

        static readonly Dictionary<string, object[]> GainsV35 = new Dictionary<string, object[]>
        {
            { "net_nicadv", new object[] { 1, "No large-send or receive batching on the adapter. Small effect on ping: game traffic is UDP and light, so the change is usually within noise." } },
            { "net_nicpower2", new object[] { 1, "Removes link speed drops and naps after idle. Consistency fix, not average ping." } },
            { "gd_msi_more", new object[] { 1, "Fewer shared interrupts for mouse, headset and SSD: slightly steadier DPC latency. Often already MSI by default." } },
            { "gd_wmi_power", new object[] { 1, "Stops devices napping and waking (≈5–50 ms dropouts). Consistency fix, not FPS." } },
            { "il_mouse_default", new object[] { 0, "Same pointer feel on the sign-in screen. No in-game effect." } },
            { "gd_nv_drs_texture", new object[] { 1, "≈1–4 % FPS in texture-heavy games. Less in modern titles that set their own filtering." } },
            { "gd_nv_drs_misc", new object[] { 1, "Full memory clocks for CUDA apps and no driver overlays. Usually no FPS change in games." } },
            { "gd_nv_drs_vsync", new object[] { 2, "Removes V-Sync queueing (≈10–50 ms) where a game forced it on. Tearing appears; no G-SYNC." } },
            { "svc_tier_safe", new object[] { 1, "≈60–150 MB RAM and a few background wake-ups less. No FPS change." } },
            { "priv_phonelink", new object[] { 0, "Privacy; less background sync." } },
            { "priv_accountnotif", new object[] { 0, "Fewer nags. No speed effect." } },
            { "priv_tips", new object[] { 0, "Privacy and fewer pop-ups. No speed effect." } },
            { "priv_langlist", new object[] { 0, "Privacy only." } },
            { "priv_pstelemetry", new object[] { 0, "Privacy only." } },
            { "priv_perm_personal", new object[] { 0, "Privacy only." } },
            { "priv_perm_files", new object[] { 0, "Privacy only." } },
            { "priv_perm_device", new object[] { 0, "Privacy only." } },
            { "dz_svc_warning", new object[] { 1, "≈30–80 MB RAM more. No FPS change." } },
            { "dz_nv_rebar", new object[] { 1, "−5 % to +10 % FPS depending on the game. Measure each one." } },
        };
    }
}
