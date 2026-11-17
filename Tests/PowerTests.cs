// Tests for src/Power/.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class PowerTests
{
    [Test] public static void ParseAliases_MapsSubgroupAndSettingAliases_ToGuids()
    {
        var m = PowerCache.ParseAliases(TestHelp.Qh);
        T.True(m.ContainsKey("SUB_PROCESSOR") && m["SUB_PROCESSOR"] == "54533251-82BE-4824-96C1-47B60B740D00", "subgroup alias");
        T.True(m.ContainsKey("PROCTHROTTLEMIN") && m["PROCTHROTTLEMIN"] == "893DEE8E-2BEF-41E0-89C6-B55D0929964C", "setting alias");
        T.True(m.ContainsKey("CPMINCORES1"), "class-1 alias");
        T.False(m.ContainsKey("SCHEME_BALANCED"), "the scheme line itself is not a setting alias");
    }

    [Test] public static void ParseAliases_IgnoresValueLines()
    {
        var m = PowerCache.ParseAliases(TestHelp.Qh);
        T.True(m.Count == 3, "exactly 3 aliases, got " + m.Count);
    }

    [Test] public static void PowerParseValues_ReadsAcDcIndexes()
    {
        string qh = "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)\n" +
            "  Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)\n" +
            "    Power Setting GUID: 893dee8e-2bef-41e0-89c6-b55d0929964c  (Minimum processor state)\n" +
            "      Current AC Power Setting Index: 0x00000064\n" +
            "      Current DC Power Setting Index: 0x00000005\n";
        var v = PowerCache.ParseValues(qh);
        uint[] x; T.True(v.TryGetValue("54533251-82BE-4824-96C1-47B60B740D00|893DEE8E-2BEF-41E0-89C6-B55D0929964C", out x) && x[0] == 100 && x[1] == 5, "AC 100 / DC 5");
    }

    [Test] public static void PlanCompare_ReadsLocalisedPowercfgOutput()
    {
        // Russian powercfg /qh shape: labels are translated, GUIDs, indentation and 0x values are not
        string a = "GUID схемы питания: 11111111-2222-3333-4444-555555555555  (Max)\n" +
                   "  GUID подгруппы: 54533251-82be-4824-96c1-47b60b740d00  (Управление питанием процессора)\n" +
                   "    GUID параметра питания: 893dee8e-2bef-41e0-89c6-b55d0929964c  (Минимальное состояние процессора)\n" +
                   "      Минимальное возможное значение: 0x00000000\n      Максимальное возможное значение: 0x00000064\n      Шаг: 0x00000001\n      Единицы: %\n" +
                   "    Индекс текущего значения при питании от сети: 0x00000005\n    Индекс текущего значения при питании от батареи: 0x00000005\n" +
                   "    GUID параметра питания: 94d3a615-a899-4ac5-ae2b-e4d8f634367f  (Политика охлаждения)\n      Возможное значение: 000\n" +
                   "    Индекс текущего значения при питании от сети: 0x00000001\n    Индекс текущего значения при питании от батареи: 0x00000000\n";
        var pa = PlanCompare.Parse(a);
        T.Eq(pa.Count, 2, "two settings");
        var min = pa.Values.First(x => x.Guid.StartsWith("893dee8e"));
        T.Eq(min.Ac, 5u, "AC index is the second-to-last hex"); T.Eq(min.Dc, 5u, "DC index is the last hex");
        var pb = PlanCompare.Parse(a.Replace("сети: 0x00000005", "сети: 0x00000064"));
        T.True(PlanCompare.Diff(pa, pb, "x.pow").Contains("5 → 100"), "difference reported");
        T.True(PlanCompare.Diff(pa, pa, "x.pow").StartsWith("No differences"), "identical plans");
    }

    [Test] public static void DuplicatePlans_FoundByName_ProtectedPlansKept()
    {
        string list = "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)\r\n" +
                      "Power Scheme GUID: 11111111-1111-1111-1111-111111111111  (Balanced)\r\n" +
                      "Power Scheme GUID: 22222222-2222-2222-2222-222222222222  (Gaming)\r\n" +
                      "Power Scheme GUID: 33333333-3333-3333-3333-333333333333  (Gaming) *\r\n" +
                      "Power Scheme GUID: 44444444-4444-4444-4444-444444444444  (Gaming)\r\n" +
                      "Power Scheme GUID: 55555555-5555-5555-5555-555555555555  (Mine)\r\n";
        var d = PowerPlans.Duplicates(list, "Power Scheme GUID: 33333333-3333-3333-3333-333333333333  (Gaming)");
        T.Eq(string.Join(",", d.Select(x => x.Key.Substring(0, 1)).OrderBy(x => x).ToArray()), "1,2,4", "copy of Balanced and the non-active Gaming copies");
        T.True(PowerPlans.Duplicates("Power Scheme GUID: 55555555-5555-5555-5555-555555555555  (Mine)", "").Count == 0, "no duplicates");
        T.True(Catalog.All.First(x => x.Id == "cmd_dupplans").Detect != null, "cmd_dupplans reads real state");
    }

    [Test] public static void DevicePower_MergedIntoFour()
    {
        foreach (var m in Catalog.DeviceMerges)
        {
            T.False(Catalog.All.Any(x => x.Id == m[0]), m[0] + " merged");
            T.Eq(Catalog.CurrentId(m[0]), m[1], m[0] + " loads as " + m[1]);
        }
        var nic = Catalog.All.First(x => x.Id == "gd_nicpower");
        T.True(nic.Ops.OfType<NicAdvOp>().Any() && nic.ApplyCmds.Count > 0 && nic.RevertCmds.Count > 0, "NIC: driver offloads and Windows adapter power management included");
        T.True(Catalog.All.First(x => x.Id == "gd_usb_stack").Ops.OfType<DeviceRegOp>().Any(), "USB: per-device settings included");
        T.True(Catalog.All.First(x => x.Id == "gd_pcipower").Ops.OfType<WmiPowerOp>().Any(), "devices: 'allow turn off' included");
        T.True(Catalog.All.First(x => x.Id == "gd_nicpower").Presets.Contains(Catalog.PRESET_GAMING), "merged tweak keeps the Gaming preset");
    }

    [Test] public static void PowerTuner_Decide()
    {
        var up = new PowerTrial { Base = { 100, 101, 100, 101 }, Try = { 110, 111, 110, 111 } };
        PowerTuner.Decide(up, true); T.True(up.Kept, "clear FPS gain kept");
        var noise = new PowerTrial { Base = { 100, 102, 99, 101 }, Try = { 101, 99, 102, 100 } };
        PowerTuner.Decide(noise, true); T.False(noise.Kept, "noise not kept"); T.Eq(noise.Note, "within noise", "noise note");
        var lat = new PowerTrial { Base = { 50, 51, 50, 51 }, Try = { 40, 41, 40, 41 } };
        PowerTuner.Decide(lat, false); T.True(lat.Kept, "lower wake-up delay kept");
        var worse = new PowerTrial { Base = { 40, 41, 40, 41 }, Try = { 50, 51, 50, 51 } };
        PowerTuner.Decide(worse, false); T.False(worse.Kept, "higher delay not kept"); T.Eq(worse.Note, "made it worse", "worse note");
        T.True(PowerTuner.Steps().All(st => st.Values.All(v => v.Length == st.Aliases.Length)), "one value per alias");
    }

    [Test] public static void PowerTuner_OnlyItsOwnSettings()
    {
        T.True(PowerTuner.KnownAlias("PERFCHECK") && PowerTuner.KnownAlias("PROCTHROTTLEMIN"), "its own settings");
        T.False(PowerTuner.KnownAlias("PERFCHECK 5 /setactive x"), "extra arguments refused");
        T.False(PowerTuner.KnownAlias("SUB_SLEEP"), "other settings refused");
    }
}
