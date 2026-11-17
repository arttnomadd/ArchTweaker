// ArchTweaker: Tweaks: scheduled tasks, services and interrupt coalescing (3.21).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class Catalog
    {
        // 3.21: from the synez gap list and the ALLSHI packs, the ones not in the catalog yet.
        static void AddV321()
        {
            string TS = Cat.TasksServices, IL = Cat.Input, N = Cat.Network;

            var ps = T("task_diskscan", TS, "Background disk scans off", "Turns off the scheduled tasks that scan NTFS and ReFS volumes for errors in the background (Chkdsk ProactiveScan and Data Integrity Scan). They can read the disk while you play. Windows still runs chkdsk at start when it finds a volume marked dirty.")
                .Tasks(@"\Microsoft\Windows\Chkdsk\ProactiveScan", @"\Microsoft\Windows\Data Integrity Scan\Data Integrity Scan", @"\Microsoft\Windows\Data Integrity Scan\Data Integrity Scan for Crash Recovery");
            ps.Off = "Scheduled background disk checks."; ps.Breaks = "A slowly failing disk shows its errors later, at the next chkdsk.";

            var sc = T("task_silentcleanup", TS, "Automatic low-disk cleanup off", "Turns off the DiskCleanup\\SilentCleanup task. It starts Disk Cleanup by itself when a drive is nearly full, which can stall disk access for minutes.")
                .Tasks(@"\Microsoft\Windows\DiskCleanup\SilentCleanup");
            sc.Off = "Automatic cleanup when a drive is nearly full."; sc.Breaks = "A full drive stays full until you clean it (Cleanup page or Storage Sense).";

            var sp = T("task_speechmodel", TS, "Speech model downloads off", "Turns off the task that downloads speech recognition models in the background. Only voice typing and speech recognition use them.")
                .Tasks(@"\Microsoft\Windows\Speech\SpeechModelDownloadTask");
            sp.Off = "Background downloads of speech models."; sp.Breaks = "Voice typing and speech recognition can use older models.";

            var wre = T("task_verifywinre", TS, "Recovery environment check off", "Turns off the VerifyWinRE task, which checks the recovery environment on a schedule and can rebuild it.")
                .Tasks(@"\Microsoft\Windows\RecoveryEnvironment\VerifyWinRE").R(Risk.Moderate);
            wre.Off = "Scheduled checks of the recovery environment (WinRE)."; wre.Breaks = "You notice a broken recovery environment only when you need it. Check with: reagentc /info.";

            var up = T("svc_upnp", TS, "UPnP device discovery off", "Sets the SSDP Discovery and UPnP Device Host services to Disabled. They look for media servers, TVs and printers on the network, and answer their broadcasts.")
                .Service("SSDPSRV", 4, 3).Service("upnphost", 4, 3);
            up.Off = "Discovery of UPnP devices on the network."; up.Breaks = "Casting to TVs, DLNA media servers and network discovery of UPnP devices stop working.";

            var wia = T("svc_stisvc", TS, "Windows Image Acquisition off", "Sets the Windows Image Acquisition (WIA) service to Disabled. Only scanners and some cameras use it.")
                .Service("stisvc", 4, 3);
            wia.Off = "The scanner and camera service."; wia.Breaks = "Scanners and cameras that use WIA stop working until you undo this.";

            var ct = T("svc_celltime", TS, "Cellular time off", "Sets the Cellular Time service to Disabled. It sets the clock from a mobile network, which a desktop PC doesn't have.")
                .Service("CellularTime", 4, 3)
                .Requires(() => Svc.Exists("CellularTime"), "the Cellular Time service");
            ct.Off = "Clock updates from a mobile network."; ct.Breaks = "Nothing on a PC without a mobile modem.";

            const string SM = @"SYSTEM\CurrentControlSet\Control\Session Manager";
            var co = T("il_coalesce0", IL, "Timer coalescing interval: 0", "Sets CoalescingTimerInterval to 0 in the kernel, Session Manager and Power keys. Windows can group timers to save power. With 0, it groups none. synez and several latency packs set it.")
                .Info("No published measurement shows a frame-time gain. Compare the Measure page (scheduling jitter) before and after, and undo it if nothing changes. Undo removes the values.")
                .Dw(LM, SM + @"\kernel", "CoalescingTimerInterval", 0, null).Dw(LM, SM, "CoalescingTimerInterval", 0, null).Dw(LM, @"SYSTEM\CurrentControlSet\Control\Power", "CoalescingTimerInterval", 0, null)
                .R(Risk.Moderate).NeedsReboot().Exp();
            co.Off = "Timer coalescing."; co.Breaks = "Slightly higher idle power use.";

            var df = T("net_dns_failcache", N, "DNS: don't cache network failures", "Sets NetFailureCacheTime to 0, so Windows does not remember for 30 s that the network was down. After the connection comes back, names resolve at once.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters", "NetFailureCacheTime", 0, null).NeedsReboot();
            df.Off = "The 30 s memory of network failures."; df.Breaks = "Nothing that you notice.";
        }
    }
}
