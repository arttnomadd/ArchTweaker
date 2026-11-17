// ArchTweaker: Windows build and edition validity for every tweak, undocumented labels, merged device power tweaks (3.7).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class Catalog
    {
        // ---------------- the post-build pipeline (order matters) ----------------
        static void PostBuild()
        {
            DangerZone.Sort(all);   // 3.4: known-dangerous tweaks move to the Danger zone, out of every preset
            MergeDevicePower();     // 3.7: before anything reads the merged tweaks' ops
            UndoNotes();            // 3.6.2: tweaks with nothing to undo say so
            Exclusions();           // 3.6.1: contradicting pairs switch each other off
            Covers();               // 3.7: a tweak that adds nothing while another is on
            MeasureFirst();         // 3.9: tweaks the research found unproven or harmful leave the presets
            Sort36();               // 3.6: Hardening category, experimental tweaks out of presets, ULPS only on older Radeons
            Validity();             // 3.7: build and edition limits, undocumented labels
            ExactUndo();            // 3.7: "keep the default" values restore what you had
            AttachEffects();        // 3.6: "Turns off" / "Can break" for every tweak
            AttachProbes();
            AttachGains();
        }

        // ---------------- validity: Windows build and edition ----------------
        // id → min build, max build (0 = none), why. Builds: 19045 Win10 22H2 · 22000 Win11 21H2 · 22621 22H2 · 22631 23H2 · 26100 24H2 · 26200 25H2
        internal static readonly object[][] BuildLimits =
        {
            new object[] { "chaticon", 22000, 22621, "In Windows 11 23H2, the Microsoft Teams app replaced Chat. Unpin or uninstall Teams instead." },
            new object[] { "cortana", 0, 22631, "Windows 11 24H2 removes the Cortana app." },
            new object[] { "copilot", 0, 22631, "This policy only turned off the old Copilot pane. On Windows 11 24H2 and newer Copilot is an app: uninstall it on the Apps page." },
            new object[] { "ui_copilotbtn", 0, 22631, "The Copilot taskbar button belonged to the old Copilot pane, which is gone in Windows 11 24H2." },
            new object[] { "recall", 26100, 0, "Recall exists only on Windows 11 24H2 and newer (Copilot+ PCs)." },
            new object[] { "il_timerres", 22000, 0, "On Windows 10 a program can raise the timer resolution only for itself, so a background service can't change it for games." },
        };
        // id → "pro" (Pro, Enterprise, Education) / "ent" (Enterprise, Education) / "note" (works everywhere, with a limit), note
        internal static readonly string[][] EditionLimits =
        {
            new[] { "telemetry", "note", "Diagnostic data level 0 works only on Enterprise and Education. Home and Pro keep the Required level. ArchTweaker turns off the DiagTrack service on every edition." },
            new[] { "ui_startrec", "pro", "Home ignores the policy. There, only the Start setting part applies." },
            new[] { "lockscreen", "ent", "Guaranteed on Enterprise and Education. Home and Pro may ignore it." },
            new[] { "recall", "pro", "Recall policies need Pro, Enterprise or Education." },
            new[] { "sys_featurelock", "pro", "Home ignores Windows Update policies." },
            new[] { "wunotify", "pro", "Home ignores Windows Update policies." },
            new[] { "nodrivers", "pro", "Home ignores Windows Update policies." },
            new[] { "noautoreboot", "pro", "Home ignores Windows Update policies." },
            new[] { "sys_storeauto", "ent", "Store policies are reliable only on Enterprise and Education." },
        };
        // no Microsoft or vendor documentation: the effect is claimed by tweak guides and changes between builds and drivers
        internal static readonly string[] UndocumentedIds =
        {
            "fso", "gd_nv_p0", "gd_nv_hdcp", "gd_nv_misc", "gd_nv_dlss_latest", "gd_nv_dlss_indicator", "gd_amd_ulps", "gd_amd_lowlat",
            "il_rawthrottle", "il_timers", "exp_serialtimer", "exp_latencytol", "pagingexec", "disp_iconcache", "gamebar", "rs_inputpreload", "gd_flip_composed",
        };

        static void Validity()
        {
            foreach (var r in BuildLimits)
            {
                var t = all.FirstOrDefault(x => x.Id == (string)r[0]); if (t == null) continue;
                t.MinBuild = (int)r[1]; t.MaxBuild = (int)r[2]; t.BuildWhy = (string)r[3];
            }
            foreach (var r in EditionLimits)
            {
                var t = all.FirstOrDefault(x => x.Id == r[0]); if (t == null) continue;
                t.EditionCode = r[1]; t.EditionNote = r[2];
            }
            foreach (var t in all.Where(x => UndocumentedIds.Contains(x.Id)))
            {
                t.Undocumented = true;
                t.Presets = t.Presets.Where(p => p != PRESET_SAFE).ToArray();   // never in Recommended
            }
        }

        // ---------------- device power: 9 tweaks → 4 ----------------
        // old id → the tweak that took it over (saved profiles still load)
        internal static readonly string[][] DeviceMerges =
        {
            new[] { "nicpower", "gd_nicpower" }, new[] { "net_nicpower2", "gd_nicpower" },
            new[] { "il_usbpower", "gd_usb_stack" },
            new[] { "gd_wmi_power", "gd_pcipower" },
        };
        static void MergeDevicePower()
        {
            foreach (var m in DeviceMerges)
            {
                var from = all.FirstOrDefault(x => x.Id == m[0]); var to = all.FirstOrDefault(x => x.Id == m[1]);
                if (from == null || to == null) continue;
                to.Ops.AddRange(from.Ops);
                to.ApplyCmds.AddRange(from.ApplyCmds); to.RevertCmds.AddRange(from.RevertCmds);
                to.Presets = to.Presets.Union(from.Presets).ToArray();
                to.Reboot |= from.Reboot;
                if (from.Risk > to.Risk) to.Risk = from.Risk;
                all.Remove(from);
                if (!Renamed.ContainsKey(m[0])) Renamed[m[0]] = m[1];
            }
            Retext("gd_nicpower", "Network adapters: no power saving, no Green Ethernet, no wake",
                "Every physical Ethernet / Wi-Fi adapter: Windows can't turn it off to save power. Energy-Efficient / Green Ethernet, Gigabit Lite and the driver's power-saving modes are off. The driver's power-down offloads are off too: ARP / NS offload while asleep, selective suspend, sleep on cable disconnect, automatic gigabit-off, PME wake. Disable-NetAdapterPowerManagement turns off Windows' own adapter power management. Fixes lag spikes and drop-outs after idle.",
                "Windows' and the driver's power saving on every physical network adapter. This includes power-off to save power, Energy-Efficient / Green Ethernet, gigabit-off, power-down offloads, sleep on disconnect and Wake-on-LAN.",
                "Wake-on-LAN, and some laptop battery life. The link can drop for a second while the settings change.");
            Retext("gd_usb_stack", "USB & HID: no power saving (drivers and each device)",
                "Driver-wide: the Windows USB driver never selectively suspends a port. HID devices (mice, keyboards, controllers) don't suspend or idle, and USB 3 hubs don't suspend. The xHCI controller doesn't use enhanced power management, and USB audio doesn't power down. Per device: every USB and HID device has enhanced power management, selective suspend and idle-in-working-state off. Adds to the power-plan USB setting, which some drivers ignore.",
                "USB selective suspend (driver-wide and per device), HID idle, USB 3 hub suspend, enhanced power management, idle-in-working-state, USB audio power saving and HID wake-up.",
                "Laptop battery life, and waking the PC with the keyboard or mouse from sleep.");
            Retext("gd_pcipower", "Devices: no power saving (PCI, ACPI and 'allow turn off')",
                "PCI and ACPI devices (GPU, NVMe, NIC, USB controllers) don't use enhanced power management or idle in working state. ArchTweaker also unticks 'Allow the computer to turn off this device to save power' on every device that has it. This includes hubs, audio, Bluetooth, network and more.",
                "Enhanced power management and idle-in-working-state on PCI / ACPI devices, and 'Allow the computer to turn off this device to save power' on every device.",
                "Several watts at idle; laptops lose battery life.");
        }
        static void Retext(string id, string title, string desc, string off, string breaks)
        {
            var t = all.FirstOrDefault(x => x.Id == id); if (t == null) return;
            t.Title = title; t.Desc = desc; t.Off = off; t.Breaks = breaks;
        }

        // ---------------- covered by ----------------
        // { tweak that covers, tweak it covers }: while the first is on, the second adds nothing
        internal static readonly string[][] CoverPairs =
        {
            new[] { "hibernate", "faststartup" },   // no hibernation = no Fast Startup
            new[] { "sec_uac", "uacdim" },          // UAC off = no prompts to dim
            new[] { "net_ipv6off", "preferipv4" },  // IPv6 off = nothing to prefer IPv4 over
        };
        static void Covers()
        {
            foreach (var p in CoverPairs)
            {
                var by = all.FirstOrDefault(x => x.Id == p[0]); var t = all.FirstOrDefault(x => x.Id == p[1]);
                if (by == null || t == null) continue;
                t.CoveredBy = t.CoveredBy.Concat(new[] { by.Id }).Distinct().ToArray();
            }
        }
        // the tweak that covers t and is on (or switched on in the same selection), else null
        public static Tweak CoveringNow(Tweak t, Func<Tweak, bool> isOn)
        {
            if (t.CoveredBy.Length == 0) return null;
            return all.FirstOrDefault(x => t.CoveredBy.Contains(x.Id) && isOn(x));
        }
        // drops picks that another pick (or an applied tweak) already covers
        public static List<Tweak> Uncovered(List<Tweak> picks)
        {
            var set = new HashSet<Tweak>(picks);
            return picks.Where(t => CoveringNow(t, x => set.Contains(x) || StateCache.Get(x) == true) == null).ToList();
        }

        // ---------------- exact undo ----------------
        // A value whose tweak value equals the Windows default (written to make sure it is set) used to be "undone"
        // by writing the default again, losing a custom value you had. It now restores exactly what you had.
        static void ExactUndo()
        {
            foreach (var t in all)
                for (int i = 0; i < t.Ops.Count; i++)
                {
                    var v = t.Ops[i] as VOp;
                    if (v == null || v.On == null || Reg.Norm(v.On) != Reg.Norm(v.Off)) continue;
                    t.Ops[i] = new KeepOp { H = v.H, Path = v.Path, Name = v.Name, Kind = v.Kind, Value = v.On };
                }
        }
    }
}
