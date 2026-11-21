// Tests for src/Catalog.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using ArchTweaker;

static class CatalogTests
{
    // 3.39: Customize page values
    [Test] public static void CustomSettings_AreWellFormed()
    {
        var ids = new HashSet<string>();
        foreach (var s in CustomSettings.All)
        {
            T.True(ids.Add(s.Id), s.Id + " is unique");
            T.True(s.H == Hive.HKCU, s.Id + " only touches the current user");
            T.True(!string.IsNullOrEmpty(s.AppliesWhen) && !string.IsNullOrEmpty(s.Group), s.Id + " says when it applies");
            if (s.IsChoice)
            {
                T.Eq(s.Labels.Length, s.Values.Length, s.Id + " one label per value");
                T.True(s.Values.Distinct().Count() == s.Values.Length && s.Values.Contains(s.Default), s.Id + " values distinct, default among them");
            }
            else T.True(s.Min <= s.Default && s.Default <= s.Max, s.Id + " default in range");
        }
    }

    [Test] public static void CustomSettings_ParseAndClamp()
    {
        var delay = CustomSettings.All.First(s => s.Id == "menu_delay");
        T.Eq(delay.Parse("250"), 250, "string number read");
        T.Eq(delay.Parse(" 9999 "), 1000, "clamped to the maximum");
        T.Eq(delay.Parse("abc"), 400, "garbage gives the default");
        T.Eq(delay.Parse(null), 400, "missing gives the default");
        T.Eq((string)delay.ToRegistry(-5), "0", "written as REG_SZ, clamped");
        var align = CustomSettings.All.First(s => s.Id == "taskbar_align");
        T.Eq(align.Parse(0), 0, "DWORD read"); T.Eq(align.Parse(7), 1, "unknown choice gives the default");
        T.True(align.ToRegistry(0) is int, "DWORD written as int");
    }

    [Test] public static void AllTweaks_HaveUniqueIds()
    {
        var dupes = Catalog.All.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        T.Eq(dupes.Count, 0, "no duplicate tweak ids" + (dupes.Count > 0 ? ": " + string.Join(", ", dupes) : ""));
    }

    [Test] public static void AllTweaks_HaveIdTitleAndCategory()
    {
        bool bad = Catalog.All.Any(t => string.IsNullOrEmpty(t.Id) || string.IsNullOrEmpty(t.Title) || string.IsNullOrEmpty(t.Category));
        T.False(bad, "every tweak has a non-empty id, title and category");
    }

    [Test] public static void Presets_HaveNoEmptyNames()
    {
        bool bad = Catalog.All.Any(t => t.Presets != null && t.Presets.Any(string.IsNullOrEmpty));
        T.False(bad, "no empty preset names");
    }

    [Test] public static void DangerZone_IsTheLastCategory()
    {
        T.Eq(Catalog.Categories.Last(), DangerZone.CAT, "Danger zone is the last tweak page");
        T.False(Catalog.Categories.Contains("Security (advanced)"), "old category name is gone");
    }

    [Test] public static void DangerZone_HoldsEveryKnownDangerousTweak()
    {
        foreach (var id in DangerZone.Moves.Keys.Concat(new[] { "dz_sac", "dz_bitlocker", "dz_audiooem" }))
        {
            var t = Catalog.All.FirstOrDefault(x => x.Id == id);
            T.True(t != null, id + " exists");
            if (t == null) continue;
            T.Eq(t.Category, DangerZone.CAT, id + " is in the Danger zone");
        }
    }

    [Test] public static void DangerZone_TweaksAreAdvanced_InNoPreset_AndSayWhatBreaks()
    {
        var dz = Catalog.All.Where(t => t.Dangerous).ToList();
        T.True(dz.Count >= 18, "at least 18 Danger zone tweaks");
        foreach (var t in dz)
        {
            T.Eq(t.Risk, Risk.Advanced, t.Id + " is Advanced");
            T.Eq(t.Presets.Length, 0, t.Id + " is in no preset");
            T.True(!string.IsNullOrEmpty(t.Breaks) && t.Breaks.Length > 20, t.Id + " says what it breaks");
        }
        T.False(Catalog.All.Any(t => !t.Dangerous && t.Presets.Length > 0 && t.Risk == Risk.Advanced && DangerZone.Moves.ContainsKey(t.Id)), "no moved tweak left in a preset");
    }

    [Test] public static void DangerZone_ListsSecurityFirst()
    {
        var dz = Catalog.All.Where(t => t.Dangerous).Select(t => t.Id).ToList();
        T.Eq(dz.First(), "defender", "Defender first");
        T.True(dz.IndexOf("sec_firewall") < dz.IndexOf("pp_idle"), "security before power");
    }

    [Test] public static void DangerZone_StartsLocked_AndNotifiesOnChange()
    {
        T.False(DangerZone.Unlocked, "locked at start");
        int n = 0; Action h = () => n++;
        DangerZone.Changed += h;
        try { DangerZone.Unlocked = true; DangerZone.Unlocked = true; DangerZone.Unlocked = false; }
        finally { DangerZone.Changed -= h; }
        T.Eq(n, 2, "one notice per real change");
        T.False(DangerZone.Unlocked, "locked again");
    }

    [Test] public static void NewRegistryTweaks_UndoToWindowsDefault()
    {
        // values Windows doesn't create are deleted on undo, never written as a guessed default
        foreach (var id in new[] { "il_timers", "ui_snap", "priv_cloudsearch", "priv_paintai", "sys_storeauto", "net_ports", "dz_bitlocker" })
            foreach (var op in Catalog.All.First(x => x.Id == id).Ops.OfType<VOp>())
                T.True(op.Off == null, id + ": " + op.Name + " undo deletes the value");
        var lh = Catalog.All.First(x => x.Id == "ui_langhotkey").Ops.OfType<VOp>().ToDictionary(o => o.Name, o => (string)o.Off);
        T.Eq(lh["Language Hotkey"], "1", "Alt+Shift restored"); T.Eq(lh["Layout Hotkey"], "2", "Ctrl+Shift restored");
    }

    [Test] public static void NewTweaks_AreWiredCorrectly()
    {
        string[] ids = { "net_nicadv", "gd_msi_more", "il_mouse_default", "gd_nv_drs_texture", "gd_nv_drs_misc", "gd_nv_drs_vsync",
            "svc_tier_safe", "priv_phonelink", "priv_accountnotif", "priv_tips", "priv_langlist", "priv_pstelemetry", "priv_perm_personal", "priv_perm_files", "priv_perm_device", "dz_svc_warning", "dz_nv_rebar" };
        foreach (var id in ids)
        {
            var t = Catalog.All.FirstOrDefault(x => x.Id == id);
            T.True(t != null, id + " exists"); if (t == null) continue;
            T.True(t.Gain != "No measurable speed effect expected.", id + " has a gain text");
        }
        T.True(Catalog.All.First(x => x.Id == "dz_svc_warning").Dangerous && Catalog.All.First(x => x.Id == "dz_nv_rebar").Dangerous, "risky ones are in the Danger zone");
        T.True(Catalog.All.First(x => x.Id == "gd_nv_drs_misc").Ops.OfType<NvDrsOp>().All(o => o.ZeroOk), "Off = 0 counts as a value");
        T.True(Catalog.All.First(x => x.Id == "gd_nv_drs_frl").Ops.OfType<NvDrsOp>().All(o => !o.ZeroOk), "computed cap keeps 0 = no value");
        T.False(new[] { "UnistoreSvc", "UserDataSvc", "LxpSvc", "HvHost", "DsmSvc" }.Any(s => Catalog.All.First(x => x.Id == "svc_tier_safe").Ops.OfType<SvcPrevOp>().Any(o => o.Name == s)), "safe tier leaves out the services that break apps");
        var mouse = Catalog.All.First(x => x.Id == "il_mouse_default").Ops.OfType<VOp>().ToList();
        T.True(mouse.All(o => o.H == Hive.HKU && o.Path.StartsWith(".DEFAULT")), "written to the default profile");
    }

    [Test] public static void EveryRiskyTweak_SaysWhatItCanBreak()
    {
        foreach (var t in Catalog.All.Where(x => x.Risk != Risk.Safe || x.Dangerous || x.Experimental))
            T.True(!string.IsNullOrEmpty(t.Breaks), t.Id + " (" + t.Risk + ") says what it can break");
    }

    [Test] public static void TweaksThatDisableThings_SayWhatTheyTurnOff()
    {
        // every tweak whose title starts with Disable / Turn off / Remove / Hide / No / Block says what it turns off
        var words = new[] { "Disable", "Turn off", "Remove", "Hide", "No ", "Block", "Stop", "Don't" };
        foreach (var t in Catalog.All.Where(x => !x.Dangerous && words.Any(w => x.Title.StartsWith(w, StringComparison.Ordinal))))
            T.True(!string.IsNullOrEmpty(t.Off), t.Id + " says what it turns off");
    }

    [Test] public static void Hardening_IsACategory_WithTheSecurityTweaks()
    {
        T.True(Catalog.Categories.Contains(Catalog.HARDENING), "Hardening category exists");
        T.True(Array.IndexOf(Catalog.Categories, Catalog.HARDENING) < Array.IndexOf(Catalog.Categories, DangerZone.CAT), "before the Danger zone");
        foreach (var id in Catalog.ToHardening.Concat(new[] { "hd_ntlm", "hd_tls", "hd_dnsclient", "hd_icmpredirect", "hd_rdp", "hd_autorun", "hd_lmhosts" }))
            T.Eq(Catalog.All.First(x => x.Id == id).Category, Catalog.HARDENING, id + " in Hardening");
        T.True(Catalog.All.First(x => x.Id == "dz_ntlm_out").Dangerous, "blocking all NTLM is in the Danger zone");
    }

    [Test] public static void Experimental_AreInNoPreset_AndNeverPicked()
    {
        var exp = Catalog.All.Where(t => t.Experimental).ToList();
        T.True(exp.Count >= 5, "at least 5 experimental tweaks");
        foreach (var t in exp) T.Eq(t.Presets.Length, 0, t.Id + " in no preset");
        T.True(new[] { "exp_serialtimer", "exp_latencytol", "dz_nv_rmpower", "gd_amd_lowlat", "dz_dev_hpet" }.All(id => Catalog.All.First(x => x.Id == id).Experimental), "tier-3 tweaks are experimental");
    }

    [Test] public static void NewTweaks_Exist_WithGains()
    {
        foreach (var id in new[] { "priv_autologgers", "gd_usb_stack", "gd_dev_virtual", "gd_dev_nvaudio", "gd_laptop_display", "gd_nv_dlss_latest", "gd_nv_dlss_indicator", "hd_ntlm", "hd_tls", "hd_dnsclient", "hd_icmpredirect", "hd_rdp", "hd_autorun", "hd_lmhosts", "dz_ntlm_out", "exp_serialtimer", "exp_latencytol", "dz_nv_rmpower", "gd_amd_lowlat", "dz_dev_hpet" })
        {
            var t = Catalog.All.FirstOrDefault(x => x.Id == id);
            T.True(t != null, id + " exists"); if (t == null) continue;
            T.True(t.Gain != "No measurable speed effect expected.", id + " has its own gain text");
        }
        T.True(Catalog.All.First(x => x.Id == "gd_nv_dlss_latest").Ops.OfType<NvDrsOp>().Any(o => o.Id == 0x10E41DF3 && o.ValueFn() == 0xFFFFFF), "DLSS preset = latest");
        T.Eq(Catalog.All.First(x => x.Id == "gd_amd_ulps").RequirementText, "an AMD GPU older than RX 7000", "ULPS limited to older Radeons");
    }

    [Test] public static void NoTwoTweaks_WriteTheSameTarget()
    {
        var owner = new Dictionary<string, string>();
        foreach (var t in Catalog.All)
            foreach (var k in Catalog.Targets(t).Distinct())
            {
                string other;
                if (owner.TryGetValue(k, out other)) T.True(false, k + " written by both " + other + " and " + t.Id);
                else owner[k] = t.Id;
            }
        T.True(owner.Count > 500, "targets collected (" + owner.Count + ")");
    }

    [Test] public static void Duplicates_AreGone()
    {
        foreach (var id in new[] { "bgapps", "gd_usb_suspend", "cmd_platformclock" })
            T.True(Catalog.All.All(t => t.Id != id), id + " removed");
        T.False(Catalog.All.First(t => t.Id == "svc_misc").Ops.OfType<SvcOp>().Any(o => o.Name == "lfsvc"), "lfsvc only in 'location'");
        T.True(Catalog.All.First(t => t.Id == "gd_usb_stack").Ops.OfType<VOp>().Any(o => o.Name == "DisableSelectiveSuspend"), "USB DisableSelectiveSuspend merged");
        T.True(Catalog.All.First(t => t.Id == "cmd_timerclean").ApplyCmds.Any(c => string.Join(" ", c).Contains("useplatformclock")), "useplatformclock merged");
        T.True(Catalog.All.First(t => t.Id == "cmd_timerclean").Presets.Contains(Catalog.PRESET_GAMING), "timer cleanup keeps the Gaming preset");
    }

    [Test] public static void MergedIds_LoadAsTheirReplacement()
    {
        string f = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllLines(f, new[] { "# old profile", "bgapps", "priv_backgroundapps", "gd_usb_suspend", "cmd_platformclock", "nagle" });
            var ids = Profile.Read(f).ToList();
            T.Eq(string.Join(",", ids.ToArray()), "priv_backgroundapps,gd_usb_stack,cmd_timerclean,nagle", "renamed and de-duplicated");
            foreach (var kv in Catalog.Renamed) T.True(Catalog.All.Any(t => t.Id == kv.Value), kv.Value + " exists");
        }
        finally { System.IO.File.Delete(f); }
    }

    [Test] public static void ContradictingPairs_ExcludeEachOther()
    {
        foreach (var p in Catalog.ExcludePairs)
        {
            var a = Catalog.All.First(t => t.Id == p[0]); var b = Catalog.All.First(t => t.Id == p[1]);
            T.True(a.Excludes.Contains(b.Id) && b.Excludes.Contains(a.Id), p[0] + " <-> " + p[1]);
            T.False(a.Presets.Intersect(b.Presets).Any(), p[0] + " and " + p[1] + " never in the same preset");
        }
        var auto = Advisor.RecommendedIds().ToList();
        foreach (var p in Catalog.ExcludePairs) T.False(auto.Contains(p[0]) && auto.Contains(p[1]), "auto pick never takes both of " + p[0] + " / " + p[1]);
    }

    [Test] public static void StartTweaks_HaveDistinctNames()
    {
        T.Eq(Catalog.All.First(t => t.Id == "startrec").Title, "Start & Quick Access: no recent files", "startrec renamed");
    }

    [Test] public static void EveryRiskyTweak_SaysWhatItTurnsOff()
    {
        foreach (var t in Catalog.All.Where(x => x.Risk != Risk.Safe || x.Dangerous || x.Experimental))
            T.True(!string.IsNullOrEmpty(t.Off), t.Id + " says what it turns off");
    }

    [Test] public static void BuildLimits_HideObsoleteTweaks()
    {
        foreach (var r in Catalog.BuildLimits) T.True(Catalog.All.Any(x => x.Id == (string)r[0]), r[0] + " exists");
        T.False(Catalog.All.Any(x => x.Id == "wifisense"), "Wi-Fi Sense removed (gone since Windows 10 1803)");
        var cortana = Catalog.All.First(x => x.Id == "cortana");
        T.Eq(cortana.MaxBuild, 22631, "Cortana up to 23H2");
        try
        {
            OSInfo.BuildOverride = 26100;   // 3.35: no registry write; the real build number stays untouched
            T.False(cortana.BuildOk, "not on 24H2"); T.False(cortana.Applicable, "hidden on 24H2");
            T.Eq(cortana.UnavailableReason, "Windows 11 24H2 removes the Cortana app.", "says why");
            T.True(Catalog.All.First(x => x.Id == "recall").BuildOk, "Recall on 24H2");
            OSInfo.BuildOverride = 22631;
            T.True(cortana.BuildOk, "still on 23H2"); T.False(Catalog.All.First(x => x.Id == "recall").BuildOk, "no Recall on 23H2");
            T.False(Catalog.All.First(x => x.Id == "chaticon").BuildOk, "Chat icon gone in 23H2");
        }
        finally { OSInfo.BuildOverride = 0; }
    }

    [Test] public static void ChangedKeys_FromThe24H2Check()
    {
        var recall = Catalog.Targets(Catalog.All.First(x => x.Id == "recall")).ToList();
        T.True(recall.Any(k => k.EndsWith("|allowrecallenablement")), "Recall: AllowRecallEnablement added");
        var doh = Catalog.Targets(Catalog.All.First(x => x.Id == "doh")).ToList();
        T.True(doh.Any(k => k.EndsWith("|dohpolicy")) && !doh.Any(k => k.EndsWith("|enableautodoh")), "DoH: DoHPolicy, not the Insider EnableAutoDoh");
        T.False(Catalog.Targets(Catalog.All.First(x => x.Id == "edgeprelaunch")).Any(k => k.Contains("allowprelaunch")), "legacy Edge value gone");
        T.False(Catalog.Targets(Catalog.All.First(x => x.Id == "websearch")).Any(k => k.EndsWith("|bingsearchenabled")), "BingSearchEnabled gone");
    }

    [Test] public static void Editions_AndUndocumented()
    {
        foreach (var r in Catalog.EditionLimits) T.True(Catalog.All.Any(x => x.Id == r[0]) && Catalog.All.First(x => x.Id == r[0]).EditionNote != null, r[0] + " has an edition note");
        foreach (var id in Catalog.UndocumentedIds)
        {
            var t = Catalog.All.FirstOrDefault(x => x.Id == id); if (t == null) continue;
            T.True(t.Undocumented, id + " labelled"); T.False(t.Presets.Contains(Catalog.PRESET_SAFE), id + " not in Recommended");
        }
    }

    [Test] public static void CoveredBy_SkipsRedundantPicks()
    {
        var fs = Catalog.All.First(x => x.Id == "faststartup"); var hib = Catalog.All.First(x => x.Id == "hibernate");
        T.True(fs.CoveredBy.Contains("hibernate"), "hibernation off covers Fast Startup");
        var picks = Catalog.Uncovered(new List<Tweak> { fs, hib });
        T.True(picks.Contains(hib) && !picks.Contains(fs), "a selection with both drops Fast Startup");
        T.False(Catalog.ExcludePairs.Any(p => p.Contains("uacdim") || p.Contains("preferipv4")), "covered pairs are no longer exclusions");
    }

    [Test] public static void Effects_AndGains_PointAtRealTweaks()
    {
        var ids = new HashSet<string>(Catalog.All.Select(t => t.Id));
        foreach (var id in Catalog.EffectIds()) T.True(ids.Contains(id) || Catalog.Renamed.ContainsKey(id), "effects entry " + id + " belongs to a tweak");
    }

    [Test] public static void V39_MeasureFirst_NotInPresets()
    {
        foreach (var id in new[] { "pp_idle", "cmd_dynamictick", "dz_nv_rebar", "net_itr_off" })
        {
            var t = Catalog.All.FirstOrDefault(x => x.Id == id);
            T.True(t != null, id + " exists");
            T.True(t.Experimental, id + " is measure-first");
            T.True(t.Presets == null || t.Presets.Length == 0, id + " is in no preset");
        }
        var nic = Catalog.All.First(x => x.Id == "net_nicadv");
        T.True(!nic.Ops.OfType<NicAdvOp>().Any(o => o.Rules.Any(r => r[0] == "*InterruptModeration")), "net_nicadv leaves interrupt moderation alone");
    }

    // 3.15: the research tweaks exist, stay out of every preset, and the hybrid-only one needs E-cores
    [Test] public static void V315_Tweaks()
    {
        foreach (var id in new[] { "gd_audio_idle", "net_rss_place", "il_rawthrottle20", "exp_reserved_ecores" })
        {
            var t = Catalog.All.FirstOrDefault(x => x.Id == id);
            T.True(t != null, id + " is in the catalog");
            if (t != null) T.Eq(t.Presets.Length, 0, id + " is in no preset");
        }
        var rs = Catalog.All.First(x => x.Id == "exp_reserved_ecores");
        T.True(rs.Experimental && rs.Reboot, "reserved E-cores is experimental and needs a restart");
        if (!HwProbe.Cpu.Hybrid) T.True(!rs.Applicable, "not offered without E-cores");
        var au = Catalog.All.First(x => x.Id == "gd_audio_idle");
        T.Eq(au.Ops.OfType<DeviceRegOp>().Count(), 2, "both idle timers");
        T.True(au.Ops.OfType<DeviceRegOp>().All(o => o.OnlyIfExists && Reg.Norm(o.Value) == "00-00-00-00"), "four zero bytes, only where the driver has the value");
        T.True(Devices.AudioPowerKeys().All(k => k.EndsWith(@"\PowerSettings")), "idle timers live under PowerSettings");
    }

    // 3.17: every curated evidence entry points at a real tweak and has a source
    [Test] public static void EvidenceMap_Valid()
    {
        foreach (var kv in EvidenceMap.Map)
        {
            T.True(Catalog.All.Any(t => t.Id == kv.Key), "evidence id exists: " + kv.Key);
            T.True(!string.IsNullOrEmpty(kv.Value.Source), "source named: " + kv.Key);
            T.True(kv.Value.Url == null || kv.Value.Url.StartsWith("https://"), "https link: " + kv.Key);
        }
        var t1 = Catalog.All.First(t => t.Id == "lastaccess");
        T.True(Evidence.For(t1)[0].StartsWith("Documented"), "a curated entry wins over the general rule");
        T.Eq(EvidenceMap.For(Catalog.All.First(t => !EvidenceMap.Map.ContainsKey(t.Id))), null, "no entry, no badge");
    }

    [Test] public static void V317_Misc()
    {
        T.Eq(TrendChart.Nice(0.7), 1.0, "nice step"); T.Eq(TrendChart.Nice(140), 200.0, "nice step 140 → 200"); T.Eq(TrendChart.Nice(4.2), 5.0, "nice step 4.2 → 5");
        T.True(Changelog.Text.StartsWith("## " + Program.Version + ":"), "What's new starts with this version");
        T.Eq(Tour.Steps.Length, 5, "five tour steps");
        T.True(Tour.Steps.All(x => x.Length == 3 && x[2].Length > 20), "each step has a title, a glyph and text");
    }

    [Test] public static void Audio_Tweaks()
    {
        T.True(Catalog.All.Any(t => t.Id == "au_enhancements_off") && Catalog.All.Any(t => t.Id == "au_mmcss_audio"), "audio tweaks in the catalog");
        T.True(Mode.ProPage("Power tuner"), "power tuner is a Pro page");
    }

    [Test] public static void Catalog_EveryCategoryHasASidebarPage()
    {
        var bad = Catalog.All.Where(t => !Catalog.Categories.Contains(t.Category)).Select(t => t.Id + " (" + t.Category + ")").ToList();
        T.True(bad.Count == 0, "tweaks in a category without a sidebar page: " + string.Join(", ", bad.ToArray()));
    }

    // 3.30
    [Test] public static void Cfg_ChangesOnlyItsNibble()
    {
        var v = new byte[24]; for (int i = 0; i < v.Length; i++) v[i] = (byte)(0x11 * (i % 15));
        var off = CfgPolicy.WithNibble(v, 2);
        T.Eq(CfgPolicy.Nibble(off), 2, "CFG off");
        for (int i = 0; i < v.Length; i++) if (i != 5) T.Eq(off[i], v[i], "byte " + i + " unchanged");
        T.Eq(off[5] & 0xF0, v[5] & 0xF0, "high nibble of byte 5 unchanged");
        T.Eq(CfgPolicy.Nibble(CfgPolicy.WithNibble(off, CfgPolicy.Nibble(v))), CfgPolicy.Nibble(v), "undo puts back the old nibble");
        T.Eq(CfgPolicy.WithNibble(null, 2).Length, 24, "missing value: 24 bytes");
        T.Eq(CfgPolicy.Nibble(null), 0, "missing value: default");
        var t = Catalog.All.First(x => x.Id == "dz_cfg");
        T.Eq(t.Category, DangerZone.CAT, "CFG off is in the Danger zone");
    }

    [Test] public static void BrowserPolicies_OnlyWhenInstalled()
    {
        foreach (var id in new[] { "chromebg", "bravebg" })
        {
            var t = Catalog.All.First(x => x.Id == id);
            T.True(t.Requirement != null, id + " needs the browser");
        }
    }
}
