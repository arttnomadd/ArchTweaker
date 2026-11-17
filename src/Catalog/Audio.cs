// ArchTweaker: Tweaks: audio enhancements and MMCSS audio priority.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static partial class Catalog
    {
        // Active playback devices: their FxProperties keys (HKLM).
        public static IEnumerable<string> AudioFxKeys()
        {
            const string R = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
            foreach (var id in Reg.SubKeys(Hive.HKLM, R))
            {
                object st = Reg.Get(Hive.HKLM, R + "\\" + id, "DeviceState");
                if (st is int && (int)st == 1) yield return R + "\\" + id + "\\FxProperties";
            }
        }

        static void AddV324()
        {
            var fx = T("au_enhancements_off", Cat.Gpu, "Audio enhancements off", "Turns off the audio enhancements on every active playback device, like the Settings switch \"Audio enhancements: Off\". The sound goes to the device without driver effects. This removes a small delay and some driver work.")
                .Dev("Audio enhancements", AudioFxKeys, "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5", RegistryValueKind.DWord, 1, false).NeedsRelogon()
                .Requires(() => AudioFxKeys().Any(), "an active playback device");   // 3.27.1: no device = nothing to do, not "applied"
            fx.Off = "Driver sound effects such as loudness, bass boost or virtual surround.";
            fx.Breaks = "Sound effects of the driver stop until you undo this.";

            var mm = T("au_mmcss_audio", Cat.Gpu, "Audio threads get high scheduling priority", "Sets the multimedia scheduler (MMCSS) tasks Audio and Pro Audio to the High scheduling category and high disk priority. Audio threads then get CPU time before other work, which helps against crackle when the PC is busy.")
                .Sz(LM, MMSP + @"\Tasks\Audio", "Scheduling Category", "High", "Medium")
                .Sz(LM, MMSP + @"\Tasks\Audio", "SFIO Priority", "High", "Normal")
                .Sz(LM, MMSP + @"\Tasks\Pro Audio", "SFIO Priority", "High", "Normal");
            mm.Off = "Nothing you would notice.";
            // OEM audio services: dz_audiooem (3.4) already covers them.
        }
    }
}
