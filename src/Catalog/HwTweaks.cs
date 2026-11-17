// ArchTweaker: hardware-aware tweaks (1.5): NVMe power-state latency, standby networking / away mode, Wi-Fi roaming.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ================= NEW TWEAKS =================
    public static partial class Catalog
    {
        public const string PRESET_AUTO = "This PC (auto)";
        const string NVME_PRI_TOL = "fc95af4d-40e7-4b6d-835a-56d131dbc80e", NVME_SEC_TOL = "dbc9e238-6de9-49e3-92cd-8c2b4946b472";
        const string NVME_SEC_IDLE = "d3d55efd-c1ff-424e-9dc3-441be7833010", NVME_NOPPME = "fc7372b6-ab2d-43ee-8797-15e9841f2cca";

        static void AddV15()
        {
            string PP = Cat.PowerPlan, GD = Cat.Gpu;
            string game = PRESET_GAMING;

            T("pp_nvme_apst", PP, "NVMe: no idle power states (APST latency tolerance 0)", "Sets both NVMe power-state transition latency tolerances to 0 ms and the secondary idle timeout to 0. Then StorNVMe never picks a non-operational power state. 'Disks never power down' only covers the primary idle timeout, and this tweak covers the rest. It also turns off non-operational permissive mode.")
                .Info("From Microsoft's StorNVMe power documentation: after the idle timeout, the driver picks the deepest state whose entry + exit latency fits within the tolerance. A tolerance of 0 means no state fits.")
                .Pow(SUBDISK, NVME_PRI_TOL, 0, "Primary NVMe power state transition latency tolerance", "0 ms")
                .Pow(SUBDISK, NVME_SEC_TOL, 0, "Secondary NVMe power state transition latency tolerance", "0 ms")
                .Pow(SUBDISK, NVME_SEC_IDLE, 0, "Secondary NVMe idle timeout", "0 (no idle transition)")
                .Pow(SUBDISK, NVME_NOPPME, 0, "NVMe NOPPME", "Off")
                .In(game);
            T("pp_standby", PP, "No away mode, no networking in standby", "Turns off Away Mode (a fake 'sleep' that keeps the PC running with the screen off). Also turns off network connectivity during standby, so the PC can't wake for updates or sync.")
                .Pow("SUB_SLEEP", "25dfa149-5dd1-4736-b5ab-e8a37b5b8187", 0, "Allow away mode policy", "No")
                .Pow("fea3413e-7e05-4911-9a71-700331f1c294", "f15576e8-98b7-4186-b944-eafa664402d9", 0, "Networking connectivity in standby", "Disable")
                .R(Risk.Moderate);
            T("pp_display", PP, "Display never dims; no adaptive brightness", "The screen doesn't dim before turning off, and adaptive brightness (on supported panels) is off. The 'turn off display after' timeout is left alone.")
                .Pow("SUB_VIDEO", "17aaa29b-8b43-4b94-aafe-35f64daaf1ee", 0, "Dim display after", "Never")
                .Pow("SUB_VIDEO", "FBD9AA66-9553-4097-BA44-ED6E9D65EAB8", 0, "Adaptive brightness", "Off");
            T("gd_wifi_roam", GD, "Wi-Fi: roam only on very weak signal (Intel / Killer)", "Sets 'Roaming Aggressiveness' to Lowest on every Wi-Fi adapter whose driver has the setting. The adapter stops background scans for other access points while the current signal is good. These scans are the main cause of periodic ping spikes on Intel AX2xx cards. ArchTweaker skips adapters without the setting. Undo restores the driver's value (default Medium).")
                .Info("Intel's own guidance: Lowest means a roaming scan only starts when the signal from the current AP is very low. If you walk around with a laptop on a mesh network, keep Medium.")
                .Dev("Wi-Fi RoamAggressiveness", Devices.WifiKeys, "RoamAggressiveness", RegistryValueKind.String, "0", true)
                .Dev("Wi-Fi *RoamAggressiveness", Devices.WifiKeys, "*RoamAggressiveness", RegistryValueKind.String, "0", true)
                .NeedsReboot().In(game);
        }
        const string SUBDISK = "SUB_DISK";

        static readonly Dictionary<string, object[]> Gains15 = new Dictionary<string, object[]>
        {
            { "pp_nvme_apst", new object[] { 1, "Removes the NVMe wake-up from deeper power states (≈1–25 ms for the deepest ones, drive-dependent). Fewer first-access stalls when streaming assets. No effect on throughput." } },
            { "pp_standby", new object[] { 0, "Stops wake-ups and background sync while the PC is in standby. No in-use speed effect." } },
            { "pp_display", new object[] { 0, "Convenience: the screen doesn't dim. No speed effect." } },
            { "gd_wifi_roam", new object[] { 2, "Wi-Fi only: removes periodic roaming-scan ping spikes (typically 20–200 ms every 30–120 s on Intel AX2xx). Average ping unchanged. Nothing on Ethernet." } },
        };
    }

    public static partial class Devices
    {
        public static IEnumerable<string> WifiKeys()
        {
            return NicKeys().Where(k => { object t = Reg.Get(Hive.HKLM, k, "*IfType"); return t is int && (int)t == 71; });
        }
    }
}
