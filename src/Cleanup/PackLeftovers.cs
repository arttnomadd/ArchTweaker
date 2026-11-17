// ArchTweaker: Leftovers from other tweak packs, and their dialog.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- leftover tweaks from other packs ----------------
    public class PackLeftover
    {
        public string Title, Why; public bool Harmful; public Hive H = Hive.HKLM; public string Path; public string[] Names; public Func<string, bool> Bad; public object FixValue; public RegistryValueKind FixKind;
        public List<string> Found = new List<string>();
        public string Shown { get { return string.Join(", ", Found.ToArray()); } }
    }

    public static class Leftovers
    {
        const string KERNEL = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
        const string GD = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        static readonly Func<string, bool> Any_ = v => true, Zero = v => v == "0", One = v => v == "1";

        public static List<PackLeftover> Checks()
        {
            return new List<PackLeftover>
            {
                new PackLeftover { Title = "No page file", Harmful = true, Path = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", Names = new[] { "PagingFiles" }, Bad = v => v.Trim().Length == 0,
                    FixValue = new[] { @"?:\pagefile.sys" }, FixKind = RegistryValueKind.MultiString,
                    Why = "Games that reserve a lot of memory crash with 'out of memory', and Windows can't save crash dumps. Fix: back to a system-managed page file." },
                new PackLeftover { Title = "Defender switched off by an old policy", Harmful = true, Path = @"SOFTWARE\Policies\Microsoft\Windows Defender", Names = new[] { "DisableAntiSpyware", "DisableAntiVirus" }, Bad = One,
                    Why = "Current Defender ignores it, but the leftover policy can block Defender updates and confuse Windows Security. Fix: ArchTweaker removes the values." },
                new PackLeftover { Title = "Automatic updates blocked by policy", Harmful = true, Path = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", Names = new[] { "NoAutoUpdate" }, Bad = One,
                    Why = "Windows never installs security fixes by itself. Fix: ArchTweaker removes the value (use 'Windows Update: notify before download' if you want control)." },
                new PackLeftover { Title = "GPU hang recovery (TDR) turned off", Harmful = true, Path = GD, Names = new[] { "TdrLevel" }, Bad = Zero,
                    Why = "A GPU hang freezes the whole PC, and the driver doesn't reset in a few seconds. Fix: ArchTweaker removes the value." },
                new PackLeftover { Title = "DPC watchdog turned off", Harmful = true, Path = KERNEL, Names = new[] { "DpcWatchdogPeriod", "DPCTimeout", "DpcTimeout", "DpcSoftTimeout", "DpcCumulativeSoftTimeout", "DpcWatchdogProfileOffset", "PassiveWatchdogTimeout" }, Bad = Any_,
                    Why = "A stuck driver freezes the PC for good, and you get no blue screen that names it. Fix: ArchTweaker removes the values." },
                new PackLeftover { Title = "Threaded DPCs turned off", Harmful = true, Path = KERNEL, Names = new[] { "ThreadDpcEnable" }, Bad = Zero,
                    Why = "Threaded DPCs then run as ordinary DPCs. Microsoft's driver documentation says each ordinary DPC increases system latency. Fix: ArchTweaker removes the value." },
                new PackLeftover { Title = "Low-QoS timer resolution override", Harmful = true, Path = KERNEL, Names = new[] { "DisableLowQosTimerResolution" }, Bad = Any_,
                    Why = "A 2025 timer guide spread this value and then withdrew it: it caused blue screens (stop code 0x139) on some PCs. Fix: ArchTweaker removes the value." },
                new PackLeftover { Title = "Custom DPC and interrupt rates", Harmful = true, Path = KERNEL, Names = new[] { "MinimumDpcRate", "DpcRequestRate", "IdealDpcRate", "InterruptRate", "InterruptRequestRate", "MaximumDpcQueueDepth", "DpcQueueDepth", "AdjustDpcThreshold", "DpcTimeLimit", "DpcTimeCount", "DpcLastCount", "InterruptLastCount" }, Bad = Any_,
                    Why = "Values like 0x99999999 from 'KernelFixed' packs change how Windows batches DPCs. They can raise latency or cause stalls. Fix: ArchTweaker removes the values." },
                new PackLeftover { Title = "Extra kernel worker threads", Harmful = true, Path = @"SYSTEM\CurrentControlSet\Control\Session Manager\Executive", Names = new[] { "AdditionalCriticalWorkerThreads", "AdditionalDelayedWorkerThreads" }, Bad = Any_,
                    Why = "Windows sizes its worker threads itself. Extra threads use kernel memory for nothing. Fix: ArchTweaker removes the values." },
                new PackLeftover { Title = "Server-style file cache (LargeSystemCache)", Harmful = true, Path = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", Names = new[] { "LargeSystemCache" }, Bad = One, FixValue = 0, FixKind = RegistryValueKind.DWord,
                    Why = "Windows keeps file data in RAM at the expense of games and apps. Fix: set back to 0 (desktop default)." },
                new PackLeftover { Title = "Faked graphics-kernel values", Harmful = true, Path = GD, Names = new[] { "DxgKrnlVersion", "MinDxgKrnlVersion", "DisableDwmVSync", "DWM_BUFFER_COUNT", "FlipQueuePolicy", "FlipModel", "DwmFlipPolicy", "DwmQueuePolicy", "MaxFrameLatency", "HwFlipPolicy", "HwLegacyFlipPolicy", "ContextNoPatchMode", "EnablePerformanceMode", "LowLatencyMode" }, Bad = Any_,
                    Why = "Values from 'Chiefs Special' style packs. Windows doesn't read most of them. The version overrides can break hardware GPU scheduling and video playback. Fix: ArchTweaker removes the values." },
                new PackLeftover { Title = "GPU scheduler overrides", Harmful = true, Path = GD + @"\Scheduler", Names = new[] { "HwIndependentFlip", "FlipOverrideMode", "QueuedPresentLimit", "ForegroundPriorityBoost", "EnablePreemption" }, Bad = Any_,
                    Why = "Can cause stutter, black screens on Alt+Tab or broken independent flip. Fix: ArchTweaker removes the values." },
                new PackLeftover { Title = "Desktop compositor (DWM) overrides", Harmful = true, Path = @"SOFTWARE\Microsoft\Windows\Dwm", Names = new[] { "BufferCount", "MaxBufferCount", "Composition", "CompositionPolicy", "DisableAdvancedDirectFlip", "DisallowNonDrawListRendering", "FlipQueuePolicy", "MaxOutstandingFrames", "DisableDrawListCaching", "DisableLockingMemory", "DisableDeviceBitmaps", "DisableDeviceBitmapsForMultiAdapter", "DisableHologramCompositor", "DisableProjectedShadows", "DisableProjectedShadowsRendering", "OverlayMinFPS" }, Bad = Any_,
                    Why = "From 'DWMFixed' packs: can cause flicker or black windows, or turn off multiplane overlays and direct flip. Fix: ArchTweaker removes the values." },
                new PackLeftover { Title = "Modern Standby watchdogs turned off", Harmful = true, Path = @"SYSTEM\CurrentControlSet\Control\Power", Names = new[] { "PdcLockWatchdog", "PdcLockWatchdogTimeout", "PdcCriticalTransitionTimeout", "PdcCsEntryAction", "PdcWcmTransitionTimeout", "PdcCriticalActivatorTimeout", "PdcActivatorClientResponseTimeout", "PdcCriticalActivatorAction", "PdcEnforceSystemIdleTimeoutOnConsoleLock" }, Bad = Any_,
                    Why = "A laptop can hang when it goes into or out of sleep, and not recover. Fix: ArchTweaker removes the values." },
                new PackLeftover { Title = "Invented environment variables", Harmful = false, Path = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", Names = new[] { "CONFIG_HZ", "HZ", "KERNEL_HZ", "CPU_MAX_PENDING_INTERRUPTS", "CPU_MAX_PENDING_IO", "DWM_MAX_BUFFER_AGE", "DWM_MAXIMUM_BUFFER_AGE", "DIRECT_GPU", "DIRECT_CPU" }, Bad = Any_,
                    Why = "Linux-style names from 'VariablesFixed' packs. Windows never reads them. They only clutter every program's environment. Fix: ArchTweaker removes them." },
                new PackLeftover { Title = "Kernel values Windows doesn't read", Harmful = false, Path = KERNEL, Names = new[] { "TimerResolution", "ClockRate", "Clock Rate", "QuantumLength", "QuantumSize", "CyclesPerClockQuantum", "MinimumIncrement", "MaximumIncrement", "IdleHalt", "ReadyThreadCount", "QuantumEnd", "PriorityState", "CpuThrottle", "PerfBoostPolicy", "PerfBootPerformance", "MaximumKernelWorkerThreads", "MaximumSharedReadyQueueSize", "DpcRoutineActive", "PendingTickFlags", "TimerInterruptDelay", "SchedulerMaximumLatency", "TSCDeadline", "UsePlatformClock", "KdDisable", "LockPagesInMemory" }, Bad = Any_,
                    Why = "Placebo values from tweak packs: they have no effect. Fix: removed, to keep the kernel key clean." },
                new PackLeftover { Title = "Other placebo values", Harmful = false, Path = @"SYSTEM\CurrentControlSet\Services\i8042prt\Parameters", Names = new[] { "PollingIterations" }, Bad = Any_,
                    Why = "The PS/2 driver ignores it on USB keyboards and mice. Fix: removed." },
            };
        }

        // values an applied ArchTweaker tweak owns are never reported
        static HashSet<string> Owned()
        {
            var h = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in Catalog.All)
            {
                bool on; try { on = StateCache.Get(t) == true; } catch { on = false; }
                if (!on) continue;
                foreach (var o in t.Ops.OfType<VOp>()) h.Add(o.Path + "|" + o.Name);
            }
            return h;
        }

        public static List<PackLeftover> Scan()
        {
            var owned = Owned(); var l = new List<PackLeftover>();
            foreach (var c in Checks())
            {
                foreach (var n in c.Names)
                {
                    if (owned.Contains(c.Path + "|" + n)) continue;
                    var v = Reg.Get(c.H, c.Path, n);
                    if (v == null) continue;
                    string s = v is string[] ? string.Join("", (string[])v) : Reg.Norm(v);
                    if (c.Bad(s)) c.Found.Add(n);
                }
                if (c.Found.Count > 0) l.Add(c);
            }
            return l;
        }

        // Every change is recorded in the undo journal, so 'Undo everything' brings the old values back.
        public static int Fix(IEnumerable<PackLeftover> items)
        {
            int n = 0;
            foreach (var c in items)
                foreach (var name in c.Found)
                {
                    try
                    {
                        if (c.FixValue != null) Reg.Set(c.H, c.Path, name, c.FixKind, c.FixValue); else Reg.DeleteValue(c.H, c.Path, name);
                        Log.W("Leftover fixed: {0}\\{1}", c.Path, name); n++;
                    }
                    catch (Exception ex) { Log.W("Leftover {0}: {1}", name, ex.Message); }
                }
            return n;
        }
    }

}
