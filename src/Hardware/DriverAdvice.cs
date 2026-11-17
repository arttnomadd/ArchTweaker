// ArchTweaker: What a driver in the slow-driver scan is, and which tweaks address it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- what a slow driver is and what to try ----------------
    public static class DriverAdvice
    {
        public sealed class Advice { public string What, Try; public string[] Ids; }
        static readonly object[][] Table =
        {
            new object[] { @"^nvlddmkm", "NVIDIA graphics driver", "Short spikes while the driver loads or the GPU changes clocks are normal. Constant high values: MSI mode, P0 state, update or clean-install the driver.", new[] { "gd_msi", "gd_nv_p0", "mpo" } },
            new object[] { @"^(amdkmdag|atikmdag|amdkmpfd|amdfendr)", "AMD graphics driver", "MSI mode. On cards older than RX 7000, ULPS off. Update the driver.", new[] { "gd_msi", "gd_amd_ulps" } },
            new object[] { @"^igdkmd", "Intel graphics driver", "Intel latency mode; update the driver.", new[] { "gd_intel_latency" } },
            new object[] { @"^dxgkrnl", "DirectX graphics kernel", "Usually follows the GPU driver: check that driver first. Hardware-accelerated GPU scheduling changes how often it runs.", new[] { "hags", "gd_msi" } },
            new object[] { @"^(ndis|tcpip|netio|e1[dirxq]\w*|e2f\w*|rt6[48]\w*|rt68\w*|rtux\w*|netwtw\w*|netwbw\w*|athw\w*|bcmwl\w*|kille\w*|mtkwl\w*|rtwlan\w*|vwifi\w*|ixgb\w*|mlx\w*)\.sys$", "Network adapter driver or network stack", "Network power saving and interrupt moderation. Spread the load over cores (RSS). MSI for the adapter. Wi-Fi drivers spike while they scan, so use Ethernet for games.", new[] { "gd_nicpower", "net_nicadv", "cmd_rss", "gd_msi_more" } },
            new object[] { @"^(storport|stornvme|storahci|iastor\w*|nvme\w*|amdsata|amdxata|secnvme|samsungnvme\w*)\.sys$", "Storage driver (NVMe / SATA)", "NVMe power states and disk power saving. MSI for the controller. Spikes during big file copies are normal.", new[] { "pp_nvme_apst", "pp_disk", "gd_msi_more" } },
            new object[] { @"^(usbxhci|usbport|usbhub\w*|ucx01000|iusb3\w*|amdxhc\w*|asmtxhci)\.sys$", "USB controller", "USB power saving (drivers, devices and the power plan). MSI for the controller. A high-polling-rate mouse adds work here.", new[] { "gd_usb_stack", "pp_usb", "gd_msi_more" } },
            new object[] { @"^(hidclass|hidusb|mouhid|kbdhid|mouclass|kbdclass)\.sys$", "Mouse / keyboard (HID)", "USB and HID power saving; input buffer size. A 4-8 kHz polling rate costs DPC time.", new[] { "gd_usb_stack", "il_queue" } },
            new object[] { @"^(hdaudbus|portcls|ks|ksthunk|rtkvhd\w*|intcaudiobus|intcoed|nvhda\w*|usbaudio\w*|atihdw\w*|amdacpbus)\.sys$", "Audio driver", "Audio device power saving; MMCSS latency settings. Disable audio outputs you don't use (HDMI audio of the GPU).", new[] { "gd_audio_power", "mmcss_latency", "gd_dev_nvaudio" } },
            new object[] { @"^(acpi|intelppm|amdppm|processr|pci)\.sys$", "Power management / ACPI / PCI", "CPU and PCIe power saving: processor latency settings, PCIe link power, idle resiliency.", new[] { "pp_latency", "pp_pcie", "pp_deepsleep", "gd_pcipower" } },
            new object[] { @"^(ntoskrnl|hal)\.(exe|dll|sys)$", "Windows kernel (timers, scheduling)", "Timer work. Dynamic tick off and the 0.5 ms timer service change how often it runs.", new[] { "cmd_dynamictick", "il_timerres" } },
            new object[] { @"^wdf01000\.sys$", "Driver framework", "Runs code for another driver (often USB, touchpad or RGB software). Close RGB / peripheral apps and scan again to see which one.", new string[0] },
            new object[] { @"^unknown$", "Unknown (address not resolved)", "Windows didn't show which driver owns this code. Scan again. If it stays unknown, a driver loaded after the scan began.", new string[0] },
        };
        public static Advice For(string driver)
        {
            string d = (driver ?? "").ToLowerInvariant();
            foreach (var r in Table)
                if (Regex.IsMatch(d, (string)r[0]))
                    return new Advice { What = (string)r[1], Try = (string)r[2], Ids = ((string[])r[3]).Where(id => Catalog.All.Any(t => t.Id == id)).ToArray() };
            return null;
        }
    }
}
