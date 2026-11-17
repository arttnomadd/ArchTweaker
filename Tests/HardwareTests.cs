// Tests for src/Hardware/.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class HardwareTests
{
    [Test] public static void DxToken_ParseJoin_RoundTripsAndKeepsOtherTokens()
    {
        var l = (System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>)T.Call(typeof(DxTokenOp), "Parse", "SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;");
        T.Eq(l.Count, 2, "two tokens parsed");
        T.Eq((string)T.Call(typeof(DxTokenOp), "Join", l), "SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;", "join restores the exact string");
    }

    [Test] public static void DxToken_Parse_IgnoresJunkSegments()
    {
        var l = (System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>)T.Call(typeof(DxTokenOp), "Parse", ";;=x;AutoHDREnable=1;novalue;");
        T.Eq(l.Count(kv => kv.Value != null), 1, "one well-formed key=value");
        T.Eq((string)T.Call(typeof(DxTokenOp), "Join", l), "=x;AutoHDREnable=1;novalue;", "unknown segments kept verbatim on rewrite");
    }

    [Test] public static void Catalog_ContainsDisplayTweaks()
    {
        foreach (var id in new[] { "windowedopt", "disp_dx_vrr", "disp_dx_autohdr", "disp_iconsonly", "disp_wallpaper_quality" })
            T.True(Catalog.All.Any(x => x.Id == id), id + " is in the catalog");
        T.False(Catalog.All.Any(x => x.Id == "disp_dx_windowed"), "duplicate windowed-games tweak removed");
        var w = Catalog.All.First(x => x.Id == "windowedopt");
        T.True(w.Ops.All(op => !(op is VOp)) && w.Ops.Any(op => op is DxTokenOp), "windowedopt edits only its DirectX token");
    }

    [Test] public static void NvDrs_StructLayoutMatchesSdk()
    {
        T.True(NvDrs.SETTING_SIZE == 12320 && NvDrs.SETTING_VER == 0x13020, "NVDRS_SETTING_V1 size/version");
        T.True(NvDrs.OFF_CURRENT == 4100 + 20 + 4100, "current value follows the predefined union");
        T.False(Catalog.All.Any(t => t.Id == "cmd_compactos"), "Compact OS removed");
        T.True(Type.GetType("ArchTweaker.RegScan, " + typeof(Catalog).Assembly.FullName) == null, "registry scan removed");
        T.True(Type.GetType("ArchTweaker.GameProfiles, " + typeof(Catalog).Assembly.FullName) == null && Type.GetType("ArchTweaker.UnityCfg, " + typeof(Catalog).Assembly.FullName) == null, "game profiles and game configs removed");
    }

    [Test] public static void Devices_SkipVirtualAdapters()
    {
        T.True(Devices.IsVirtualNic("WAN Miniport (IP)"), "miniport");
        T.True(Devices.IsVirtualNic("Hyper-V Virtual Ethernet Adapter"), "hyper-v");
        T.True(Devices.IsVirtualNic("WireGuard Tunnel"), "wireguard");
        T.False(Devices.IsVirtualNic("Intel(R) Ethernet Controller I225-V"), "I225 is physical");
        T.False(Devices.IsVirtualNic("Realtek PCIe 2.5GbE Family Controller"), "Realtek is physical");
    }

    [Test] public static void NicAdv_WritesOnlyWhatTheDriverAccepts()
    {
        const string K = @"SOFTWARE\ArchTweakerTest\Nic\0001";
        try
        {
            Reg.Set(Hive.HKLM, K + @"\Ndi\Params\*InterruptModeration\enum", "0", Microsoft.Win32.RegistryValueKind.String, "Disabled");
            Reg.Set(Hive.HKLM, K + @"\Ndi\Params\*InterruptModeration\enum", "1", Microsoft.Win32.RegistryValueKind.String, "Enabled");
            Reg.Set(Hive.HKLM, K + @"\Ndi\Params\*FlowControl\enum", "3", Microsoft.Win32.RegistryValueKind.String, "Rx & Tx");   // no "0" offered
            Reg.Set(Hive.HKLM, K + @"\Ndi\Params\*JumboPacket", "min", Microsoft.Win32.RegistryValueKind.String, "1514");
            Reg.Set(Hive.HKLM, K + @"\Ndi\Params\*ReceiveBuffers", "min", Microsoft.Win32.RegistryValueKind.String, "80");
            Reg.Set(Hive.HKLM, K + @"\Ndi\Params\*ReceiveBuffers", "max", Microsoft.Win32.RegistryValueKind.String, "4096");
            Reg.Set(Hive.HKLM, K, "*ReceiveBuffers", Microsoft.Win32.RegistryValueKind.String, "2048");
            Reg.Set(Hive.HKLM, K + @"\Ndi\Params\*TransmitBuffers", "default", Microsoft.Win32.RegistryValueKind.String, "256");
            var plan = NicAdvOp.PlanFor(new[] { K }, Catalog.NicLatencyRules).ToDictionary(x => x[1], x => x[2]);
            T.False(plan.ContainsKey("*InterruptModeration"), "3.9: interrupt moderation is its own measure-first tweak");
            var itr = NicAdvOp.PlanFor(new[] { K }, new[] { new[] { "*InterruptModeration", "=0" } }).ToDictionary(x => x[1], x => x[2]);
            T.Eq(itr["*InterruptModeration"], "0", "enum offers 0");
            T.False(plan.ContainsKey("*FlowControl"), "flow control skipped: driver has no 0");
            T.Eq(plan["*JumboPacket"], "1514", "jumbo at driver minimum");
            T.Eq(plan["*ReceiveBuffers"], "1024", "receive buffers capped");
            T.False(plan.ContainsKey("*TransmitBuffers"), "under the cap: left alone");
            T.False(plan.ContainsKey("ITR"), "keyword the driver lacks: skipped");
        }
        finally { Reg.DeleteTreeRaw(Hive.HKLM, @"SOFTWARE\ArchTweakerTest"); }
    }

    [Test] public static void Ghosts_NeverOfferMachineOrSoftwareDevices()
    {
        T.False(Ghosts.Offer(@"ROOT\LEGACY_BEEP\0000", "LegacyDriver"), "root-enumerated");
        T.False(Ghosts.Offer(@"ACPI\PNP0A08\0", "System"), "system class");
        T.True(Ghosts.Offer(@"USBSTOR\DISK&VEN_SANDISK\123", "DiskDrive"), "old USB stick");
        T.True(Ghosts.Offer(@"DISPLAY\DEL4321\5&1", "Monitor"), "old monitor");
    }

    [Test] public static void DevCtl_MatchesByIdPrefixOrName()
    {
        var rdp = new DevInfo { Id = @"ROOT\RDPBUS\0000", Name = "Remote Desktop Device Redirector Bus" };
        var nva = new DevInfo { Id = @"ROOT\UNNAMED_DEVICE\0001", Name = "NVIDIA Virtual Audio Device (Wave Extensible) (WDM)" };
        var gpu = new DevInfo { Id = @"PCI\VEN_10DE&DEV_2208\4&1", Name = "NVIDIA GeForce RTX 3080 Ti" };
        T.True(DevCtl.Matches(rdp, new[] { @"ROOT\RDPBUS\" }, null), "id prefix");
        T.True(DevCtl.Matches(nva, null, new[] { "NVIDIA Virtual Audio Device" }), "name");
        T.False(DevCtl.Matches(gpu, new[] { @"ROOT\RDPBUS\" }, new[] { "NVIDIA Virtual Audio Device" }), "the GPU never matches");
    }

    [Test] public static void Radeon_NewGenerationsAreDetected()
    {
        T.True(Devices.IsNewRadeon("AMD Radeon RX 7900 XTX"), "RX 7900");
        T.True(Devices.IsNewRadeon("AMD Radeon RX 9070 XT"), "RX 9070");
        T.True(Devices.IsNewRadeon("AMD Radeon 780M Graphics"), "780M iGPU");
        T.False(Devices.IsNewRadeon("AMD Radeon RX 6800 XT"), "RX 6800 is older");
        T.False(Devices.IsNewRadeon("AMD Radeon RX 580"), "RX 580 is older");
    }

    [Test] public static void Nip_ParsesBaseProfileDwordsOnly()
    {
        string xml = "<?xml version=\"1.0\" encoding=\"utf-16\"?><ArrayOfProfile><Profile><ProfileName>Base Profile</ProfileName><Settings>" +
            "<ProfileSetting><SettingNameInfo>Vertical Sync</SettingNameInfo><SettingID>11041231</SettingID><SettingValue>138504007</SettingValue><ValueType>Dword</ValueType></ProfileSetting>" +
            "<ProfileSetting><SettingNameInfo>Shader cache</SettingNameInfo><SettingID>11306135</SettingID><SettingValue>4294967295</SettingValue><ValueType>Dword</ValueType></ProfileSetting>" +
            "<ProfileSetting><SettingNameInfo>A string</SettingNameInfo><SettingID>1</SettingID><SettingValue>abc</SettingValue><ValueType>String</ValueType></ProfileSetting>" +
            "</Settings></Profile><Profile><ProfileName>Some Game</ProfileName><Settings><ProfileSetting><SettingID>2</SettingID><SettingValue>1</SettingValue><ValueType>Dword</ValueType></ProfileSetting></Settings></Profile></ArrayOfProfile>";
        var s = NipFiles.Parse(xml);
        T.Eq(s.Count, 2, "two Base Profile DWORDs");
        T.Eq(s[0].Id, 0x00A879CFu, "VSync id"); T.Eq(s[1].Value, 0xFFFFFFFFu, "full 32-bit value");
        var back = NipFiles.Parse(NipFiles.Build(s));
        T.True(back.Count == 2 && back[1].Id == s[1].Id && back[1].Value == s[1].Value, "export round-trips");
    }

    [Test] public static void V39_OldNicDriver_InboxOrOld()
    {
        var now = new DateTime(2026, 11, 10);
        T.True(Advisor.OldNicDriver("Intel I225-V", "net2ic68.inf", "9-15-2019", now) != null, "inbox 2019 driver flagged");
        T.True(Advisor.OldNicDriver("Intel I225-V", "net2ic68.inf", "9-15-2019", now).Contains("built into Windows"), "says inbox");
        T.True(Advisor.OldNicDriver("Intel I225-V", "oem12.inf", "3-10-2026", now) == null, "current OEM driver is fine");
        T.True(Advisor.OldNicDriver("Realtek 2.5GbE", "oem7.inf", "1-5-2021", now) != null, "OEM driver older than 3 years flagged");
    }

    [Test] public static void NvDrs_NotSupportedAndNotFound_AreSkippedNotFailed()
    {
        T.True(NvDrs.Unsupported(new NvApiException("x", -104)), "-104 NOT_SUPPORTED skipped");
        T.True(NvDrs.Unsupported(new NvApiException("x", -160)), "-160 SETTING_NOT_FOUND skipped");
        T.True(!NvDrs.Unsupported(new NvApiException("x", -1)), "-1 is a real error");
        T.True(!NvDrs.Unsupported(new InvalidOperationException("x")), "other errors are real");
    }

    [Test] public static void DriverAdvice_KnowsCommonDrivers()
    {
        foreach (var d in new[] { "nvlddmkm.sys", "ndis.sys", "stornvme.sys", "USBXHCI.SYS", "HDAudBus.sys", "ACPI.sys", "ntoskrnl.exe", "Wdf01000.sys", "e1d68x64.sys", "rt640x64.sys", "Netwtw14.sys" })
            T.True(DriverAdvice.For(d) != null, d + " has advice");
        T.True(DriverAdvice.For("somevendor.sys") == null, "unknown driver: no made-up advice");
        foreach (var d in new[] { "nvlddmkm.sys", "ndis.sys", "usbxhci.sys" }) T.True(DriverAdvice.For(d).Ids.Length > 0, d + " points at tweaks");
    }

    [Test] public static void IrqTuner_Helpers()
    {
        T.Eq(IrqTuner.InstanceId(@"SYSTEM\CurrentControlSet\Enum\PCI\VEN_8086&DEV_15F3\3&1&0&E0"), @"PCI\VEN_8086&DEV_15F3\3&1&0&E0", "instance id");
        T.Eq(IrqTuner.DriverFile(@"\SystemRoot\System32\drivers\e2f.sys", "e2fexpress"), "e2f.sys", "driver from ImagePath");
        T.Eq(IrqTuner.DriverFile(@"System32\DRIVERS\USBXHCI.SYS", "USBXHCI"), "usbxhci.sys", "lower case");
        T.Eq(IrqTuner.DriverFile("", "HDAudBus"), "hdaudbus.sys", "service name fall-back");
        var cpus = IrqTuner.PinCpus(0xFFFF, true, 24);
        T.True(cpus.Count == 2 && cpus[0] == 2 && cpus[1] == 4, "SMT: CPU 2 and 4 (skip core 0)");
        var c2 = IrqTuner.PinCpus(0xFF, false, 8);
        T.True(c2.Count == 2 && c2[0] == 1 && c2[1] == 2, "no SMT: CPU 1 and 2");
        var nic = new IrqDevice { Key = "k", Class = "Net", HasMsiKey = true, Msi = false };
        var cf = IrqTuner.Configs(nic, cpus);
        T.True(cf.Count == 4 && cf[0].Msi == 1 && cf[1].Priority == 3 && cf[2].Mask == (1UL << 2), "MSI, priority, two CPUs");
        T.Eq(IrqTuner.Configs(new IrqDevice { Class = "SCSIAdapter", HasMsiKey = true }, cpus).Count, 0, "storage never touched");
        T.True(!IrqTuner.Configs(new IrqDevice { Class = "USB", HasMsiKey = true, Msi = true }, cpus).Any(c => c.Msi.HasValue), "MSI already on: not tried");
        var t1 = new IrqTrial { Base = { 10, 10.2, 9.9 }, Try = { 8, 8.1, 7.9 }, WorstBase = 300, WorstTry = 280 };
        IrqTuner.Decide(t1); T.True(t1.Kept, "20 % faster: kept");
        var t2 = new IrqTrial { Base = { 10, 10.2, 9.9 }, Try = { 8, 8.1, 7.9 }, WorstBase = 300, WorstTry = 600 };
        IrqTuner.Decide(t2); T.True(!t2.Kept, "worse worst case: not kept");
        var t3 = new IrqTrial { Base = { 10, 10.2, 9.9 }, Try = { 9.9, 10.1, 10 }, WorstBase = 300, WorstTry = 300 };
        IrqTuner.Decide(t3); T.True(!t3.Kept && t3.Note == "within noise", "noise: not kept");
        var d = new IrqDevice { Key = @"SYSTEM\CurrentControlSet\Enum\PCI\X\Y" };
        string k; IrqConfig rc;
        T.True(IrqTuner.ParseLine(IrqTuner.Line(d, new IrqConfig { Msi = 1, Policy = 4, Priority = -1, Mask = 0x10 }), out k, out rc), "backup line read back");
        T.True(k == d.Key && rc.Msi == 1 && rc.Policy == 4 && rc.Priority == -1 && rc.Mask == 0x10, "backup fields");
        T.True(IrqTuner.ParseLine(IrqTuner.Line(d, new IrqConfig { Msi = null, Policy = -1, Priority = -1, Mask = 0 }), out k, out rc) && rc.Msi == -1 && rc.Mask == 0, "absent values");
        T.True(!IrqTuner.ParseLine("broken", out k, out rc), "damaged line");
    }

    [Test] public static void HwAudit_Helpers()
    {
        T.Eq(HwAudit.LinkGen(4), "Gen 4 (16 GT/s)", "gen 4"); T.Eq(HwAudit.LinkGen(0), "?", "unknown");
        var h = HwAudit.LastTwoHex("Power Setting GUID: 0cc5b647\r\n  Minimum Possible Setting: 0x00000000\r\n  Maximum Possible Setting: 0x00000064\r\n  Текущий индекс (сеть): 0x00000064\r\n  Текущий индекс (батарея): 0x0000000a");
        T.True(h != null && h[0] == 100 && h[1] == 10, "AC and DC are the last two hex values, any language");
        T.Eq(HwAudit.LastTwoHex("nothing"), null, "no values");
        var off = new List<ulong[]> { new ulong[] { 0xF6000000, 0xF6FFFFFF }, new ulong[] { 0xE0000000, 0xEFFFFFFF } };
        var on = new List<ulong[]> { new ulong[] { 0xF6000000, 0xF6FFFFFF }, new ulong[] { 0x6000000000, 0x63FFFFFFFF } };
        T.True(HwAudit.RebarFromRanges(off) == false, "256 MB window: ReBAR off"); T.True(HwAudit.RebarFromRanges(on) == true, "16 GB window: ReBAR on");
        T.Eq(HwAudit.LargestMb(on), 16384L, "16 GB in MB"); T.True(HwAudit.RebarFromRanges(new List<ulong[]>()) == null, "no ranges: unknown");
        T.True(HwAudit.RamLevel(4000, 4800) == Level.Fix, "below rated: fix"); T.True(HwAudit.RamLevel(4800, 4800) == Level.Tip, "at JEDEC: check XMP");
        T.True(HwAudit.RamLevel(6000, 4800) == Level.Good, "above JEDEC: profile on"); T.True(HwAudit.RamLevel(0, 4800) == Level.Info, "unknown");
        foreach (var id in new[] { "task_diskscan", "task_silentcleanup", "task_speechmodel", "task_verifywinre", "svc_upnp", "svc_stisvc", "svc_celltime", "il_coalesce0", "net_dns_failcache" })
        {
            var t = Catalog.All.FirstOrDefault(x => x.Id == id);
            T.True(t != null && t.Ops.Count > 0 && !string.IsNullOrEmpty(t.Breaks) && !string.IsNullOrEmpty(t.Off), "3.21 tweak " + id + " is complete");
        }
        T.True(Catalog.All.First(x => x.Id == "il_coalesce0").Experimental, "coalescing is experimental");
    }

    [Test] public static void Irq_ClassOf()
    {
        T.Eq(Irq.ClassOf("Net", null), "Net", "Class value wins");
        T.Eq(Irq.ClassOf(null, "{4D36E968-E325-11CE-BFC1-08002BE10318}"), "Display", "ClassGUID only (26H1): display");
        T.Eq(Irq.ClassOf("", "{36fc9e60-c465-11cf-8056-444553540000}"), "USB", "ClassGUID only: USB");
        T.Eq(Irq.ClassOf(null, "{00000000-0000-0000-0000-000000000000}"), "", "unknown class");
    }
}
