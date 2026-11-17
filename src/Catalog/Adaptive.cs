// ArchTweaker 2.0: adaptive-catalog advisories and hardware fingerprinting for portable profiles.
using System;
using System.Linq;

namespace ArchTweaker
{
    public static partial class Catalog
    {
        // Advisory note when a tweak looks mismatched to this machine/OS. null = no concern.
        // Non-fatal — the tweak can still be applied; this just warns in the preview.
        public static string ConflictNote(Tweak t)
        {
            try
            {
                if (!t.BuildOk) return t.BuildWhy;   // 3.7
                if (t.Win11Only && !OSInfo.IsWin11) return "Windows 11 only — no effect on this build.";
                if (t.Win10Only && OSInfo.IsWin11) return "Windows 10 only — no effect on Windows 11.";
                if (t.Requirement != null && !t.RequirementMet)
                    return (string.IsNullOrEmpty(t.RequirementText) ? "Requirement" : t.RequirementText) + " not detected.";

                bool gpuish = t.Category == Cat.Gpu || t.Id.StartsWith("gd_");
                if (gpuish)
                {
                    string s = (t.Id + " " + t.Title).ToLowerInvariant();
                    if ((s.Contains("nvidia") || s.Contains("nv_")) && !Devices.HasNvidia()) return "No NVIDIA GPU detected.";
                    if ((s.Contains("amd") || s.Contains("radeon")) && !Devices.HasAmdGpu()) return "No AMD GPU detected.";
                    if (s.Contains("intel") && !Devices.HasIntelGpu()) return "No Intel GPU detected.";
                }
            }
            catch { }
            return null;
        }
    }

    // A short, stable description of the machine, stamped into exported profiles so an import
    // can warn when a profile was built on different hardware.
    public static class HwFingerprint
    {
        public static string Current()
        {
            string cpu = "", gpu = "", os = "";
            try { cpu = HwProbe.Cpu != null ? HwProbe.Cpu.Name : ""; } catch { }
            try { gpu = HwProbe.GpuName(); } catch { }
            try { os = OSInfo.Name + " build " + OSInfo.Build; } catch { }
            return "cpu=" + Clean(cpu) + "; gpu=" + Clean(gpu) + "; os=" + Clean(os);
        }
        static string Clean(string s) { return (s ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ").Trim(); }

        // Human diff between a stored fingerprint line and this machine. null = same / unknown.
        public static string Diff(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return null;
            string now = Current();
            if (stored.Trim() == now.Trim()) return null;
            return "Profile made on: " + stored.Trim() + "\r\nThis machine is:     " + now.Trim();
        }
    }
}
