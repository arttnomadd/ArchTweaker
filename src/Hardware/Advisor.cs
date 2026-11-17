// ArchTweaker: PC advisor (1.5): turns the hardware probe into findings, each with the exact tweak IDs that address it.
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
    // ================= ADVISOR =================
    public enum Level { Fix, Tip, Good, Info }

    public class Finding
    {
        public Level Level; public string Title, Text; public string[] Ids = new string[0];
        public List<Tweak> Pending
        {
            get
            {
                var set = new HashSet<string>(Ids, StringComparer.OrdinalIgnoreCase);
                return Catalog.All.Where(t => set.Contains(t.Id) && t.Applicable && Mode.Allows(t) && !StateCache.Applied(t)).ToList();
            }
        }
    }

    public static partial class Advisor
    {
        public static string Summary = "";

        public static List<Finding> Run()
        {
            var f = new List<Finding>();
            var cpu = HwProbe.Cpu;
            var links = HwProbe.Links();
            int[] ram = HwProbe.RamSpeed(), hz = HwProbe.Refresh();
            string gpu = HwProbe.GpuName();
            bool nv = gpu.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;

            var sb = new StringBuilder();
            sb.Append(cpu.Name.Length > 0 ? cpu.Name : "Unknown CPU");
            sb.Append(cpu.Hybrid ? string.Format(" ({0}P + {1}E cores, {2} threads)", cpu.PCores, cpu.ECores, cpu.Threads) : string.Format(" ({0} cores, {1} threads)", cpu.PCores > 0 ? cpu.PCores : cpu.Threads, cpu.Threads));
            if (gpu.Length > 0) sb.Append(" with ").Append(gpu);
            if (hz != null) sb.Append(" at ").Append(hz[0]).Append(" Hz");
            var up = links.FirstOrDefault(x => x.Up);
            if (up != null) sb.Append(up.Wireless ? ", online over Wi-Fi" : ", online over Ethernet");
            Summary = sb.ToString();

            // ---- CPU / power plan
            string[] core = { "pp_plan", "pp_minmax", "pp_epp", "pp_boost", "pp_parking", "pp_latency" };
            f.Add(Make(Level.Fix, "Your CPU still saves power between frames",
                "EPP 0, aggressive boost and no core parking are the CPU settings with the largest measured effect on 1 % lows. They are in the active power plan, so ArchTweaker makes the plan first.",
                core, "The ArchTweaker power plan and its CPU settings are active."));
            if (cpu.Hybrid)
                f.Add(Make(Level.Tip, "Hybrid CPU with E-cores active",
                    string.Format("{0} P-cores and {1} E-cores are online. Preferring P-cores for all threads stops Windows from placing game threads on slow cores.", cpu.PCores, cpu.ECores),
                    new[] { "pp_hybrid" }, "Game threads already prefer P-cores."));
            else if (cpu.ECoresDisabled)
                f.Add(Info("E-cores are disabled in BIOS",
                    string.Format("Only {0} P-cores ({1} threads) are online, so the scheduler has no slow cores for threads. 'Prefer P-cores' has no effect and is not selected. For Unity games, set job-worker-count = {2}.", cpu.PCores, cpu.Threads, Math.Max(1, cpu.PCores - 1))));

            // ---- timer
            f.Add(Make(Level.Tip, "Timer resolution",
                "A 0.5 ms system timer makes Sleep()-based frame limiters precise. Check the result with Tools → Measure Sleep(1) precision.",
                new[] { "il_timerres" }, "The 0.5 ms timer service is running."));

            // ---- network
            var wifiUp = links.FirstOrDefault(x => x.Up && x.Wireless);
            var wiredIdle = links.FirstOrDefault(x => !x.Wireless && !x.Up);
            if (wifiUp != null && wiredIdle != null)
                f.Add(Make(Level.Fix, "You're online over Wi-Fi while a wired port is free",
                    string.Format("{0} is not in use. A cable removes Wi-Fi background-scan spikes (often 20–200 ms about every minute). No tweak can fully hide them. Until then, the Wi-Fi tweaks below keep the adapter at full power and stop scans for other access points.", wiredIdle.Desc),
                    new[] { "pp_wifi", "gd_wifi_roam", "gd_nicpower" }, "Wi-Fi power saving and roaming scans are already reduced. Plug in the cable for the remaining gain."));
            else if (wifiUp != null)
                f.Add(Make(Level.Tip, "Wi-Fi is your only connection",
                    "The adapter is set to never power-save and to roam only when the signal is very weak, which removes most periodic ping spikes.",
                    new[] { "pp_wifi", "gd_wifi_roam", "gd_nicpower" }, "Wi-Fi tweaks for steady ping are on."));
            else if (links.Any(x => x.Up))
            {
                var e = links.First(x => x.Up);
                f.Add(Make(Level.Tip, "Wired connection" + (e.Mbps > 0 ? " at " + (e.Mbps >= 1000 ? (e.Mbps / 1000.0).ToString("0.#") + " Gbps" : e.Mbps + " Mbps") : ""),
                    "Turning off Energy-Efficient Ethernet and adapter power saving removes the small wake-up spikes they add.",
                    new[] { "gd_nicpower", "nicpower", "net_nicpower2" }, "Adapter power saving and EEE are off."));
                // 3.5: an idle Wi-Fi adapter keeps scanning even while the cable carries the traffic
                if (!e.Wireless && links.Any(x => x.Wireless && !x.Up))
                    f.Add(Info("Wi-Fi adapter is idle while you're on a cable", "An unused Wi-Fi adapter still scans for networks in the background, which shows up as periodic DPC spikes. If you never use Wi-Fi on this PC, disable the adapter in Device Manager (or turn Wi-Fi off in the BIOS).", Level.Tip));
            }

            // ---- RAM
            if (ram != null && ram[0] > 0 && ram[1] > 0)
            {
                if (ram[0] + 50 < ram[1])
                    f.Add(Info("RAM runs below its rated speed", string.Format("Configured {0} MT/s, rated {1} MT/s. Enable XMP/EXPO in BIOS. It's the largest single gain on this list for CPU-bound games (often 5–15 % in 1 % lows) and no Windows tweak can do it.", ram[0], ram[1]), Level.Fix));
                else if (ram[0] > ram[1] + 50)
                    f.Add(Info("RAM runs above its rated speed", string.Format("Configured {0} MT/s, rated {1} MT/s. Run a memory stability test (TM5, Karhu or OCCT) after BIOS updates, because instability looks like random game crashes.", ram[0], ram[1]), Level.Good));
                else
                    f.Add(Info("RAM runs at its rated speed", string.Format("{0} MT/s. If the kit supports XMP/EXPO and you haven't tuned it yet, that profile is free performance.", ram[0]), Level.Good));
            }

            // ---- display
            if (hz != null && hz[1] > hz[0] + 5)
                f.Add(Info("Your monitor runs below its maximum refresh rate", string.Format("Running at {0} Hz, the GPU reports up to {1} Hz. Set it in Settings → Display → Advanced display. Check the cable, too: DisplayPort, or HDMI 2.1 for high refresh at 1440p.", hz[0], hz[1]), Level.Fix));

            // ---- GPU
            if (nv)
                f.Add(Make(Level.Tip, "NVIDIA GPU",
                    "Hardware-accelerated GPU scheduling is necessary for DLSS Frame Generation and usually trims a little latency. MSI mode keeps GPU interrupts steady.",
                    new[] { "hags", "gd_msi", "gd_nv_misc" }, "HAGS, MSI mode and the NVIDIA basics are set."));

            // ---- storage
            if (HwProbe.HasNvme())
                f.Add(Make(Level.Tip, "NVMe SSD power states",
                    "Windows lets the SSD drop into low-power states after 200 ms of idle and accepts wake-up latency to do so. On a desktop that only costs first-read stalls.",
                    new[] { "pp_disk", "pp_nvme_apst", "pp_pcie" }, "The SSD and PCIe links never enter low-power states."));

            // ---- security / VBS
            var vbs = HwProbe.VbsRunning();
            if (vbs == true)
                f.Add(Make(Level.Fix, "Virtualization-based security is running",
                    "VBS / Memory Integrity costs about 5 % average FPS in published tests. Turning it off lowers kernel protection. Some anti-cheats on Windows 11 (Valorant, FACEIT) need it, so check your games first.",
                    new[] { "vbs" }, "VBS is off."));
            else if (vbs == false)
                f.Add(Info("Virtualization-based security is off", "No VBS overhead on this PC.", Level.Good));

            // ---- gaming basics
            f.Add(Make(Level.Tip, "Background recording and overlays",
                "Game DVR records constantly in the background and windowed-game optimizations give borderless games flip-model latency.",
                new[] { "gamedvr", "gamemode", "windowedopt" }, "Recording is off and windowed games use the flip model."));

            Advisor35(f, cpu);
            Advisor39(f);
            return f;
        }

        // 3.5: drivers known for DPC latency, BIOS settings no Windows tweak can reach, and leftover devices
        static void Advisor35(List<Finding> f, CpuTopo cpu)
        {
            try
            {
                if (new[] { "KillerNetworkService", "KillerAnalyticsService", "KillerWirelessService" }.Any(Svc.Exists))
                    f.Add(Make(Level.Fix, "Killer network software is installed",
                        "Killer's traffic-shaping and analytics services are a common source of ping spikes and DPC latency. With them off the card works as a plain adapter. Intel's own driver without the Killer suite is the cleaner option.",
                        new[] { "svc_killer" }, "Killer services are off."));
                var raid = new[] { "iaStorAC", "iaStorAVC", "iaStorVD", "rcraid", "rcbottom" }.Where(s => Svc.Exists(s) && Svc.GetStart(s) < 4).ToList();
                if (raid.Count > 0)
                {
                    bool vmd = raid.Contains("iaStorVD");
                    f.Add(Info(vmd ? "Intel VMD is on: the NVMe runs through Intel RST" : "Storage runs through a RAID / RST driver",
                        vmd ? "iaStorVD is active, so Windows' own NVMe driver (stornvme) doesn't run the SSD. The NVMe power tweaks in ArchTweaker don't reach the drive, and RST adds its own DPC work. With a single SSD, turn off 'VMD controller' in the BIOS. On this install, first run bcdedit /set {current} safeboot minimal, then turn off VMD, boot Safe Mode once and run bcdedit /deletevalue {current} safeboot. If you skip a step, Windows doesn't start. Turn VMD on again to get back in."
                            : string.Format("{0} is active. Intel RST and AMD RAIDXpert add their own DPC work to every disk access. With a single NVMe or SATA drive, AHCI mode in the BIOS with Windows' own driver (stornvme / storahci) is faster and lower-latency. Switch only after enabling safe-mode boot once, or Windows won't start.", string.Join(", ", raid.ToArray())),
                        vmd ? Level.Fix : Level.Tip));
                }
                // 3.30: Bluetooth services do nothing without a radio
                if (HwProbe.HasBluetooth() == false)
                    f.Add(Make(Level.Tip, "No Bluetooth adapter on this PC",
                        "Windows found no Bluetooth radio, so its services only use memory and start-up time. If you add a Bluetooth dongle later, undo this tweak first.",
                        new[] { "svc_bt" }, "Bluetooth services are off. This PC has no Bluetooth adapter."));
                if (OemAudioPresent())
                    f.Add(Info("Audio effect software is running", "Nahimic, Waves, Dolby or a similar effects service hooks into every sound stream. It is a known cause of game stutter. 'Disable OEM audio effect services' is in the Danger zone, because the vendor's sound app and its effects stop working.", Level.Tip));
            }
            catch (Exception ex) { Log.W("Advisor (drivers): {0}", ex.Message); }

            string n = (cpu.Name ?? "").ToLowerInvariant();
            if (n.Contains("intel"))
                f.Add(Info("BIOS checklist (Intel)", "Windows can't change these, so check them once in the BIOS. XMP on. Resizable BAR and Above 4G decoding on. PCIe native power management and native ASPM off. C-states off only if you accept higher idle power. Hyper-Threading and E-cores: test with your games before you turn them off."));
            else if (n.Contains("amd") || n.Contains("ryzen"))
                f.Add(Info("BIOS checklist (AMD)", "Windows can't change these, so check them once in the BIOS. EXPO on, Above 4G decoding and Resizable BAR on, and Power Down mode off. UCLK = MEMCLK (1:1) for DDR5-6000/6200, and a BIOS from 2025 or later. On X3D CPUs, leave the vendor 'game mode' off and let the chipset driver control core parking."));

            try
            {
                int ghosts = Ghosts.List().Count;
                if (ghosts >= 25)
                    f.Add(Info("Many disconnected devices are still registered", string.Format("Windows still keeps {0} devices that are not connected (old USB sticks, monitors, adapters). They're harmless but clutter Device Manager and the device scans. Drivers page → Ghost devices… removes them.", ghosts), Level.Tip));
            }
            catch { }   // SetupAPI not available (not Windows)

            // 3.6: harmful values left behind by other tweak packs
            try
            {
                var lo = Leftovers.Scan().Where(x => x.Harmful).ToList();
                if (lo.Count > 0)
                    f.Add(Info("Harmful leftovers from other tweak packs", string.Format("{0} problem(s) found, for example: {1}. Tools & backup → Leftover tweaks from other packs… explains each one and fixes them (Undo everything can bring them back).", lo.Count, string.Join("; ", lo.Take(3).Select(x => x.Title).ToArray())), Level.Fix));
            }
            catch (Exception ex) { Log.W("Advisor (leftovers): {0}", ex.Message); }
        }

        static bool OemAudioPresent()
        {
            return new[] { "NahimicService", "WavesSysSvc", "WavesAudioService", "DolbyDAX2API", "AsusSSSvc", "RtkAudioUniversalService" }.Any(s => Svc.Exists(s) && Svc.GetStart(s) < 4);
        }

        static Finding Make(Level lv, string title, string text, string[] ids, string doneText)
        {
            var fd = new Finding { Level = lv, Title = title, Text = text, Ids = ids };
            if (fd.Pending.Count == 0) { fd.Level = Level.Good; fd.Text = doneText; }
            return fd;
        }
        static Finding Info(string title, string text) { return new Finding { Level = Level.Info, Title = title, Text = text }; }
        static Finding Info(string title, string text, Level lv) { return new Finding { Level = lv, Title = title, Text = text }; }

        // IDs for the "This PC" preset
        public static IEnumerable<string> RecommendedIds()
        {
            return Run().SelectMany(x => x.Ids).Distinct().Where(id => !DangerZone.Is(id) && !Catalog.All.Any(t => t.Id == id && t.Experimental));   // 3.4: never an automatic pick
        }
    }
}
