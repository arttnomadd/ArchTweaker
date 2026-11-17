// ArchTweaker: The tweak model: Tweak, Risk and the basic operations (registry value, key add/delete, task, service).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ArchTweaker
{
    public enum Risk { Safe, Moderate, Advanced }

    public abstract class Op
    {
        public abstract bool IsOn();
        public abstract void Apply();
        public abstract void Revert();
        public abstract string Describe();
        public virtual string Source { get { return "registry"; } }
        public static string HiveName(Hive h) { return h.ToString(); }
        public static string Fmt(object v, RegistryValueKind k)
        {
            if (v == null) return "(value deleted)";
            if (v is byte[]) return "hex:" + BitConverter.ToString((byte[])v).Replace("-", ",");
            if (k == RegistryValueKind.DWord) { uint u = unchecked((uint)(int)v); return u + (u > 9 ? " (0x" + u.ToString("X") + ")" : "") + " dword"; }
            return "\"" + v + "\"";
        }
    }

    // Registry value: On value when tweak applied, Off value (Windows default) when reverted. Off == null => delete value.
    public class VOp : Op
    {
        public Hive H; public string Path, Name; public RegistryValueKind Kind; public object On, Off;
        public override bool IsOn() { return Reg.Norm(Reg.Get(H, Path, Name)) == Reg.Norm(On); }
        public override void Apply()
        {
            if (On == null) Reg.DeleteValue(H, Path, Name); else Reg.Set(H, Path, Name, Kind, On);
        }
        public override void Revert()
        {
            if (Off == null) Reg.DeleteValue(H, Path, Name); else Reg.Set(H, Path, Name, Kind, Off);
        }
        public override string Describe()
        {
            return "Registry: " + HiveName(H) + "\\" + Path + "\r\n      \"" + (Name.Length == 0 ? "(Default)" : Name) + "\" = " + Fmt(On, Kind) + "\r\n      undo: " + (Off == null ? "value deleted (Windows default)" : Fmt(Off, Kind));
        }
    }

    // A key we add (context-menu entries etc). Revert deletes the whole key.
    public class KeyAddOp : Op
    {
        public Hive H; public string Path;
        public override bool IsOn() { return Reg.KeyExists(H, Path); }
        public override void Apply() { Reg.EnsureKey(H, Path); }
        public override void Revert() { Reg.DeleteTreeRaw(H, Path); }
        public override string Source { get { return "registry key"; } }
        public override string Describe() { return "Registry key created: " + HiveName(H) + "\\" + Path + "\r\n      undo: key deleted"; }
    }

    // A key we remove. Applying exports a .reg backup first; reverting imports it.
    public class KeyDelOp : Op
    {
        public Hive H; public string Path;
        public bool Recreate;   // 3.6.2: with no backup, Undo creates the key again (empty) instead of giving up
        public override bool IsOn() { return !Reg.KeyExists(H, Path); }
        public override void Apply() { Reg.DeleteTreeBackedUp(H, Path); }
        public override void Revert()
        {
            var files = System.IO.Directory.GetFiles(Paths.Backups, "*.reg")
                .Where(f => Backups.Ok(f) && System.IO.File.ReadAllText(f).IndexOf(Path, StringComparison.OrdinalIgnoreCase) >= 0)   // 3.2.1: only admin-owned backups
                .OrderByDescending(f => f).ToList();
            if (files.Count > 0) Shell.Run("reg.exe", "import \"" + files[0] + "\" /reg:64", true);
            else if (Recreate) { Reg.EnsureKey(H, Path); Log.Say("No backup for {0}; created the key again.", Path); }
            else Log.W("No backup found for {0}; cannot restore.", Path);
        }
        public override string Source { get { return "registry key"; } }
        public override string Describe() { return "Registry key removed (exported to a .reg backup first): " + HiveName(H) + "\\" + Path + "\r\n      undo: backup re-imported"; }
    }

    // Scheduled task: reads the task XML directly (fast, works on any Windows language), toggles via schtasks.
    public class TaskOp : Op
    {
        public string Path;
        string File_ { get { return System.IO.Path.Combine(Environment.SystemDirectory, "Tasks") + Path; } }
        public bool Exists { get { return System.IO.File.Exists(File_); } }
        // parsed task XML is cached by file time: the file only changes when the task is enabled or disabled
        static readonly Dictionary<string, KeyValuePair<DateTime, bool>> xmlCache = new Dictionary<string, KeyValuePair<DateTime, bool>>(StringComparer.OrdinalIgnoreCase);
        public override bool IsOn()
        {
            if (!Exists) return true;
            try
            {
                DateTime mt = System.IO.File.GetLastWriteTimeUtc(File_);
                lock (xmlCache) { KeyValuePair<DateTime, bool> c; if (xmlCache.TryGetValue(File_, out c) && c.Key == mt) return c.Value; }
                var doc = new System.Xml.XmlDocument();
                doc.Load(File_);
                var n = doc.SelectSingleNode("/*[local-name()='Task']/*[local-name()='Settings']/*[local-name()='Enabled']");
                bool off = n != null && n.InnerText.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
                lock (xmlCache) xmlCache[File_] = new KeyValuePair<DateTime, bool>(mt, off);
                return off;
            }
            catch { return false; }
        }
        public override void Apply() { if (Exists) Shell.Run("schtasks.exe", "/Change /TN \"" + Path + "\" /Disable", true); }
        public override void Revert() { if (Exists) Shell.Run("schtasks.exe", "/Change /TN \"" + Path + "\" /Enable", true); }
        public override string Source { get { return "scheduled task"; } }
        public override string Describe() { return "Scheduled task disabled: " + Path + (Exists ? "" : "  (not present on this PC)") + "\r\n      schtasks /Change /TN \"" + Path + "\" /Disable   ·   undo: /Enable"; }
    }

    public class SvcOp : Op
    {
        public string Name; public int On, Off; public bool OffDelayed;
        public override bool IsOn() { return !Svc.Exists(Name) || Svc.GetStart(Name) == On; }
        public override void Apply() { Svc.SetStart(Name, On, false); }
        public override void Revert() { Svc.SetStart(Name, Off, OffDelayed); }
        public override string Source { get { return "service"; } }
        public override string Describe() { return "Service " + Name + ": startup → " + Svc.StartName(On, false) + (Svc.Exists(Name) ? " (now: " + Svc.StartName(Svc.GetStart(Name), Svc.GetDelayed(Name)) + ")" : " (not installed)") + "\r\n      undo: " + Svc.StartName(Off, OffDelayed); }
    }

    public class Tweak
    {
        public string Id, Category, Title, Desc;
        public string Origin;    // 3.0: where the tweak came from (null = ArchTweaker's own research)
        public Risk Risk = Risk.Safe;
        public bool Reboot, Win11Only, Win10Only, Relogon;
        public List<Op> Ops = new List<Op>();
        public Action ApplyExtra, RevertExtra;
        public Func<bool> Detect;
        public List<string[]> ApplyCmds = new List<string[]>(), RevertCmds = new List<string[]>();
        public List<string> Explain = new List<string>();
        public string DetectHow;
        public int Impact; public string Gain;
        public string ImpactName { get { return Impact >= 3 ? "HIGH" : Impact == 2 ? "MEDIUM" : Impact == 1 ? "LOW" : "NONE"; } }
        public string DetectSource
        {
            get
            {
                if (Detect != null) return DetectHow ?? "live check";
                if (Ops.Count == 0) return "ArchTweaker record (Windows has no readable state for this)";
                return string.Join(" + ", Ops.Select(o => o.Source).Distinct().ToArray());
            }
        }
        public string More;
        public string Breaks;    // 3.4: what this can break, in plain words (3.6: any tweak, not only the Danger zone)
        public string Off;       // 3.6: what this switches off, in plain words
        public int MinBuild, MaxBuild; public string BuildWhy;              // 3.7: Windows builds the tweak works on (0 = no limit) and why
        public string EditionCode, EditionNote;                             // 3.7: "pro" / "ent" / "note" and what the edition changes
        public bool Undocumented;                                           // 3.7: no Microsoft or vendor documentation
        public string[] CoveredBy = new string[0];                          // 3.7: tweaks that make this one add nothing while they are on
        public bool BuildOk { get { int b = OSInfo.Build; return b <= 0 || ((MinBuild == 0 || b >= MinBuild) && (MaxBuild == 0 || b <= MaxBuild)); } }
        public bool EditionOk { get { int e = OSInfo.EditionTier; return EditionCode == "pro" ? e >= 1 : EditionCode == "ent" ? e >= 2 : true; } }
        public string UnavailableReason
        {
            get
            {
                if (!BuildOk) return BuildWhy ?? "not for this Windows version";
                if (Win11Only && !OSInfo.IsWin11) return "Windows 11 only";
                if (Win10Only && OSInfo.IsWin11) return "Windows 10 only";
                if (!RequirementMet) return "needs " + RequirementText;
                return null;
            }
        }
        public string UndoNote;   // 3.6.2: "defaults" = it puts Windows defaults back, so there is nothing to undo; "permanent" = can't be undone
        public string[] Excludes = new string[0];   // 3.6.1: ids that can't be on at the same time as this one
        public bool Experimental; // 3.6: unproven; never in a preset or an automatic pick, meant for the A/B test page
        public bool Dangerous { get { return Category == DangerZone.CAT; } }
        public Func<bool> Requirement; public string RequirementText;
        bool? reqCache;
        public bool RequirementMet { get { if (Requirement == null) return true; if (!reqCache.HasValue) { try { reqCache = Requirement(); } catch { reqCache = false; } } return reqCache.Value; } }

        public string Details()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var o in Ops) sb.Append("• " + o.Describe() + "\r\n\r\n");
            foreach (var c in ApplyCmds) sb.Append("• Runs: " + (c[0] == "ps" ? "powershell  " : c[0] + " ") + c[1] + "\r\n\r\n");
            foreach (var c in RevertCmds) sb.Append("• Undo runs: " + (c[0] == "ps" ? "powershell  " : c[0] + " ") + c[1] + "\r\n\r\n");
            foreach (var e in Explain) sb.Append("• " + e + "\r\n\r\n");
            if (sb.Length == 0 && (ApplyExtra != null)) sb.Append("• Built-in action — see description above.\r\n");
            return sb.ToString();
        }
        public bool HasCommands { get { return ApplyCmds.Count > 0 || RevertCmds.Count > 0; } }
        public string[] Presets = new string[0];

        public bool Applicable
        {
            get { return BuildOk && !(Win11Only && !OSInfo.IsWin11) && !(Win10Only && OSInfo.IsWin11) && RequirementMet; }
        }

        public bool IsApplied()
        {
            try
            {
                if (Detect != null) return Detect();
                if (Ops.Count == 0) return State.Get(Id);
                var devs = Ops.OfType<DeviceRegOp>().ToList();
                if (devs.Count > 0 && devs.Count == Ops.Count && devs.All(d => d.Count == 0)) return false;   // no matching hardware at all
                var pows = Ops.OfType<PowerOp>().ToList();
                if (pows.Count > 0 && pows.Count == Ops.Count && pows.All(p => !p.Exists)) return false;
                return Ops.All(o => o.IsOn());
            }
            catch { return false; }
        }

        public void Apply()
        {
            StateCache.Clear();
            foreach (var o in Ops) o.Apply();
            RunCmds(ApplyCmds);
            if (ApplyExtra != null) ApplyExtra();
            if (Ops.Any(o => o is PowerOp)) PowerCache.Activate();
            State.Set(Id, true);
        }

        public void Revert()
        {
            StateCache.Clear();
            for (int i = Ops.Count - 1; i >= 0; i--) Ops[i].Revert();
            RunCmds(RevertCmds);
            if (RevertExtra != null) RevertExtra();
            if (Ops.Any(o => o is PowerOp)) PowerCache.Activate();
            State.Set(Id, false);
        }

        static void RunCmds(List<string[]> cmds)
        {
            foreach (var c in cmds)
            {
                string o = c[0] == "ps" ? Shell.PS(c[1]) : Shell.Run(c[0], c[1], true);
                string first = (o ?? "").Trim().Split('\n')[0].Trim();
                Log.W("  > {0} {1}{2}", c[0] == "ps" ? "powershell" : c[0], c[1].Length > 90 ? c[1].Substring(0, 90) + "…" : c[1], first.Length > 0 ? "  →  " + first : "");
            }
        }

        // fluent builders
        public Tweak Dw(Hive h, string p, string n, int on, object off) { Ops.Add(new VOp { H = h, Path = p, Name = n, Kind = RegistryValueKind.DWord, On = on, Off = off }); return this; }
        public Tweak Sz(Hive h, string p, string n, string on, object off) { Ops.Add(new VOp { H = h, Path = p, Name = n, Kind = RegistryValueKind.String, On = on, Off = off }); return this; }
        public Tweak Key(Hive h, string p) { Ops.Add(new KeyAddOp { H = h, Path = p }); return this; }
        public Tweak DelKey(Hive h, string p) { Ops.Add(new KeyDelOp { H = h, Path = p }); return this; }
        public Tweak Service(string n, int on, int off) { Ops.Add(new SvcOp { Name = n, On = on, Off = off }); return this; }
        public Tweak Service(string n, int on, int off, bool offDelayed) { Ops.Add(new SvcOp { Name = n, On = on, Off = off, OffDelayed = offDelayed }); return this; }
        public Tweak Cmd(string file, string args) { ApplyCmds.Add(new[] { file, args }); return this; }
        public Tweak UndoCmd(string file, string args) { RevertCmds.Add(new[] { file, args }); return this; }
        public Tweak PS(string script) { ApplyCmds.Add(new[] { "ps", script }); return this; }
        public Tweak UndoPS(string script) { RevertCmds.Add(new[] { "ps", script }); return this; }
        public Tweak Tasks(params string[] paths) { foreach (var p in paths) Ops.Add(new TaskOp { Path = p }); return this; }
        public Tweak Bin(Hive h, string p, string n, byte[] on, byte[] off) { Ops.Add(new VOp { H = h, Path = p, Name = n, Kind = RegistryValueKind.Binary, On = on, Off = off }); return this; }
        public Tweak Pow(string sub, string setting, uint value, string label, string valueLabel) { Ops.Add(new PowerOp { Sub = sub, Setting = setting, Value = value, Label = label, ValueLabel = valueLabel }); return this; }
        public Tweak Dev(string label, Func<IEnumerable<string>> targets, string name, RegistryValueKind kind, object value, bool onlyIfExists) { Ops.Add(new DeviceRegOp { Label = label, Targets = targets, Name = name, Kind = kind, Value = value, OnlyIfExists = onlyIfExists }); return this; }
        public Tweak Nv(uint id, Func<uint> value, string label, string valueLabel) { Ops.Add(new NvDrsOp { Id = id, ValueFn = value, Label = label, ValueLabel = valueLabel }); return this; }
        public Tweak NvZ(uint id, uint value, string label, string valueLabel) { Ops.Add(new NvDrsOp { Id = id, ValueFn = () => value, Label = label, ValueLabel = valueLabel, ZeroOk = true }); return this; }
        public Tweak Explains(params string[] lines) { Explain.AddRange(lines); return this; }
        public Tweak Info(string more) { More = more; return this; }
        public Tweak Breaking(string what) { Breaks = what; return this; }
        public Tweak TurnsOff(string what) { Off = what; return this; }
        public Tweak Exp() { Experimental = true; return this; }
        public Tweak Requires(Func<bool> f, string text) { Requirement = f; RequirementText = text; return this; }
        public Tweak In(params string[] presets) { Presets = presets; return this; }
        public Tweak NeedsReboot() { Reboot = true; return this; }
        public Tweak NeedsRelogon() { Relogon = true; return this; }
        public Tweak W11() { Win11Only = true; return this; }
        public Tweak W10() { Win10Only = true; return this; }
        public Tweak R(Risk r) { Risk = r; return this; }
        public Tweak OnApply(Action a) { ApplyExtra = a; return this; }
        public Tweak OnRevert(Action a) { RevertExtra = a; return this; }
        public Tweak DetectBy(Func<bool> f) { Detect = f; return this; }
    }
}
