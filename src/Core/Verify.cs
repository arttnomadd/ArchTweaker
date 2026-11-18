// ArchTweaker 2.0: preview-diff, verify-after-apply, drift detection, and per-session .reg snapshot.
// All pure logic on top of the existing Op / Tweak / State model — no new UI framework required.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace ArchTweaker
{
    // One concrete change the next apply/revert will make (or a note that nothing will change).
    public class Change
    {
        public Tweak Tweak;
        public bool Applying;         // true = applying the tweak, false = reverting it
        public string Scope;          // "Registry", "Power plan", "Service", "Task", "Command", "Device", "Other"
        public string Target;         // key\name, service name, etc.
        public string From;           // current value / state (human)
        public string To;             // value / state after
        public bool NoOp;             // current already equals target
        public bool Conflict;         // hardware/OS mismatch advisory
        public string Note;           // conflict or extra note
    }

    public static class Preview
    {
        // Build a per-op diff for a planned apply/revert set.
        public static List<Change> Build(IEnumerable<KeyValuePair<Tweak, bool>> work)
        {
            var list = new List<Change>();
            foreach (var kv in work)
            {
                Tweak t = kv.Key; bool apply = kv.Value;
                string conflict = Catalog.ConflictNote(t);
                bool any = false;
                foreach (var op in t.Ops)
                {
                    var c = FromOp(t, op, apply);
                    if (c == null) continue;
                    any = true;
                    if (conflict != null) { c.Conflict = true; c.Note = conflict; }
                    list.Add(c);
                }
                // non-op tweaks (commands / extras tracked only by State)
                if (!any)
                    list.Add(new Change { Tweak = t, Applying = apply, Scope = "Other",
                        Target = t.Title, From = t.IsApplied() ? "applied" : "not applied",
                        To = apply ? "applied" : "not applied", NoOp = (t.IsApplied() == apply),
                        Conflict = conflict != null, Note = conflict });
            }
            return list;
        }

        static Change FromOp(Tweak t, Op op, bool apply)
        {
            var v = op as VOp;
            if (v != null)
            {
                object cur = Reg.Get(v.H, v.Path, v.Name);
                object tgt = apply ? v.On : v.Off;
                bool noop = Reg.Norm(cur) == Reg.Norm(tgt);
                return new Change
                {
                    Tweak = t, Applying = apply, Scope = "Registry",
                    Target = Op.HiveName(v.H) + "\\" + v.Path + " \\ " + (v.Name.Length == 0 ? "(Default)" : v.Name),
                    From = cur == null ? "(not set)" : Op.Fmt(cur, v.Kind),
                    To = tgt == null ? "(deleted → Windows default)" : Op.Fmt(tgt, v.Kind),
                    NoOp = noop
                };
            }
            // generic op: use its own description + current on/off state
            bool on;
            try { on = op.IsOn(); } catch { on = false; }
            return new Change
            {
                Tweak = t, Applying = apply, Scope = ScopeOf(op),
                Target = FirstLine(op.Describe()),
                From = on ? "applied" : "not applied",
                To = apply ? "applied" : "not applied",
                NoOp = (on == apply)
            };
        }

        static string ScopeOf(Op op)
        {
            string s = op.Source;
            if (s == "power") return "Power plan";
            string n = op.GetType().Name;
            if (n == "SvcOp") return "Service";
            if (n == "TaskOp") return "Task";
            if (n == "DeviceRegOp") return "Device";
            if (n == "KeyAddOp" || n == "KeyDelOp") return "Registry key";
            return "Other";
        }
        static string FirstLine(string s) { int i = s.IndexOf('\r'); if (i < 0) i = s.IndexOf('\n'); return i < 0 ? s : s.Substring(0, i); }

        public static string Summary(List<Change> cs)
        {
            int real = cs.Count(c => !c.NoOp), noop = cs.Count(c => c.NoOp), conf = cs.Count(c => c.Conflict);
            string s = real + " change" + (real == 1 ? "" : "s");
            if (noop > 0) s += ", " + noop + " already in the desired state";
            if (conf > 0) s += ", " + conf + " with a hardware/OS note";
            return s;
        }
    }

    // After apply/revert: re-read and confirm each tweak reached its intended state.
    public class VerifyResult
    {
        public Tweak Tweak; public bool WantedApplied; public bool Actual; public bool Ok { get { return WantedApplied == Actual; } }
    }

    public static class Verify
    {
        public static List<VerifyResult> Check(IEnumerable<KeyValuePair<Tweak, bool>> applied)
        {
            var r = new List<VerifyResult>();
            foreach (var kv in applied)
            {
                bool actual; try { actual = kv.Key.IsApplied(); } catch { actual = false; }
                r.Add(new VerifyResult { Tweak = kv.Key, WantedApplied = kv.Value, Actual = actual });
            }
            return r;
        }

        // Log a concise verification report; returns the count that did not take.
        public static int Report(List<VerifyResult> results)
        {
            var bad = results.Where(x => !x.Ok).ToList();
            if (bad.Count == 0) { Log.W("Verify: all {0} change(s) confirmed.", results.Count); return 0; }
            Log.W("Verify: {0} of {1} change(s) did NOT take as expected:", bad.Count, results.Count);
            foreach (var b in bad) Log.W("  ! {0} — wanted {1}, is {2}", b.Tweak.Title, b.WantedApplied ? "applied" : "reverted", b.Actual ? "applied" : "not applied");
            return bad.Count;
        }
    }

    // Compare the current machine state to a desired set of tweak ids (a profile).
    public class DriftItem { public Tweak Tweak; public bool DesiredApplied; public bool Actual; }

    public static class Drift
    {
        // desiredIds = tweaks the profile says should be ON. Returns everything that doesn't match (applicable only).
        public static List<DriftItem> Against(IEnumerable<string> desiredIds)
        {
            var want = new HashSet<string>(desiredIds, StringComparer.OrdinalIgnoreCase);
            var outl = new List<DriftItem>();
            foreach (var t in Catalog.All.Where(x => x.Applicable))
            {
                if (!want.Contains(t.Id)) continue;             // only the profile's ON tweaks matter for re-assert
                bool actual;
                try { actual = t.IsApplied(); }
                catch (Exception ex) { Log.W("Drift: skipping {0} — state check failed: {1}", t.Title, ex.Message); continue; }  // don't re-apply on a probe failure
                if (!actual) outl.Add(new DriftItem { Tweak = t, DesiredApplied = true, Actual = false });
            }
            return outl;
        }

        // Re-apply the drifted tweaks. Returns how many were re-applied.
        public static int Reassert(List<DriftItem> drift)
        {
            int n = 0;
            foreach (var d in drift.Where(x => x.DesiredApplied && !x.Actual))
            {
                // 3.5: Danger zone tweaks come back only while the Danger zone is unlocked (or /danger on the command line)
                if (d.Tweak.Dangerous && !DangerZone.Unlocked) { Log.W("Not re-applied (Danger zone is locked): {0}", d.Tweak.Title); continue; }
                try { d.Tweak.Apply(); Log.W("Re-asserted: {0}", d.Tweak.Title); n++; }
                catch (Exception ex) { Log.W("Re-assert FAILED {0}: {1}", d.Tweak.Title, ex.Message); }
            }
            if (n > 0) { try { Native.BroadcastSettingChange(); PowerCache.Invalidate(); Probe.Invalidate(); } catch { } }
            return n;
        }
    }

    // Belt-and-suspenders: export every registry key a planned apply will touch, to one timestamped .reg bundle.
    public static class RegSnapshot
    {
        static string Dir { get { string p = Path.Combine(Paths.Root, "snapshots"); Directory.CreateDirectory(p); return p; } }

        // 3.32: one line per value the apply touches, read straight from the registry. reg.exe export used to copy
        // the whole key with every subkey: a value directly under ...\Control exported about 30 MB.
        public class SnapValue { public Hive H; public string Path, Name; public bool KeyExists; public object Value; public RegistryValueKind Kind; }

        public static string Capture(IEnumerable<KeyValuePair<Tweak, bool>> work, string label)
        {
            var vals = new List<SnapValue>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in work)
                foreach (var v in kv.Key.Ops.OfType<VOp>())
                {
                    if (!seen.Add(v.H + "\\" + v.Path + "\\" + (v.Name ?? ""))) continue;
                    bool exists = Reg.KeyExists(v.H, v.Path);
                    object cur = exists ? Reg.Get(v.H, v.Path, v.Name) : null;
                    vals.Add(new SnapValue { H = v.H, Path = v.Path, Name = v.Name, KeyExists = exists, Value = cur, Kind = cur == null ? RegistryValueKind.Unknown : Reg.GetKind(v.H, v.Path, v.Name) });
                }
            if (vals.Count == 0) return null;
            string file = Path.Combine(Dir, (label ?? "apply") + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".reg");
            File.WriteAllText(file, Text(vals, label, DateTime.Now), new System.Text.UnicodeEncoding(false, true));
            Log.W("Registry snapshot saved: {0} ({1} value(s)). Double-click it to put those values back.", file, vals.Count);
            Prune(Dir, KEEP);
            return file;
        }


        // the .reg text: a value that did not exist is written as "name"=- (importing it deletes the value again);
        // a key that did not exist is only a comment, so importing never deletes a key with everything under it
        public static string Text(IEnumerable<SnapValue> vals, string label, DateTime when)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Windows Registry Editor Version 5.00\r\n");
            sb.Append("; ArchTweaker snapshot before ").Append(label ?? "apply").Append(" — ").Append(when.ToString("yyyy-MM-dd HH:mm:ss")).Append("\r\n");
            foreach (var g in vals.GroupBy(v => RegText.LongHive(v.H) + "\\" + v.Path, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("\r\n");
                if (!g.First().KeyExists) { sb.Append("; [").Append(g.Key).Append("] did not exist\r\n"); continue; }
                sb.Append("[").Append(g.Key).Append("]\r\n");
                foreach (var v in g)
                {
                    try { sb.Append(RegText.Line(v.Name, v.Value, v.Kind)).Append("\r\n"); }
                    catch (InvalidOperationException) { sb.Append("; skipped a value name with a line break\r\n"); }
                }
            }
            return sb.ToString();
        }

        // 3.32: before 3.31.1 a snapshot copied whole keys with every subkey, often about 30 MB each. The values
        // they hold are in the undo journal too, which is what Undo uses. A snapshot written since then is a few KB.
        public const long LargeBytes = 1L << 20;
        public static int CleanupLargeOnce()
        {
            const string Mine = @"Software\ArchTweaker", Flag = "SnapCleanV";
            try
            {
                if (Reg.Get(Hive.HKCU, Mine, Flag) != null) return 0;
                int n = DeleteLarge(Dir, LargeBytes);
                Reg.SetRaw(Hive.HKCU, Mine, Flag, RegistryValueKind.DWord, 1);
                return n;
            }
            catch (Exception ex) { Log.W("Snapshot cleanup: {0}", ex.Message); return 0; }
        }
        public static int DeleteLarge(string dir, long limit)
        {
            int n = 0; long bytes = 0;
            foreach (var f in new DirectoryInfo(dir).GetFiles("*.reg"))
                if (f.Length > limit)
                    try { long l = f.Length; f.Delete(); n++; bytes += l; } catch (Exception ex) { Log.W("Snapshot cleanup {0}: {1}", f.Name, ex.Message); }
            if (n > 0) Log.W("Deleted {0} large registry snapshot(s) from versions before 3.31.1: {1} freed. Undo uses the journal, not these files.", n, Units.Bytes(bytes));
            return n;
        }

        // 3.9: keep the newest snapshots only. The undo journal, not these files, is what Undo uses.
        public const int KEEP = 10;
        public static int Prune(string dir, int keep)
        {
            int n = 0;
            try
            {
                foreach (var f in new DirectoryInfo(dir).GetFiles("*.reg").OrderByDescending(x => x.LastWriteTimeUtc).Skip(keep))
                    try { f.Delete(); n++; } catch { }
                if (n > 0) Log.W("Deleted {0} old registry snapshot(s); the newest {1} stay.", n, keep);
            }
            catch (Exception ex) { Log.W("Snapshot prune {0}: {1}", dir, ex.Message); }
            return n;
        }
    }

    // ---------- What ArchTweaker has applied (2.7), for the feature-update report ----------
    // HKCU\Software\ArchTweaker\Applied: value name = tweak id, data = 1. Updated after every apply/undo.
    public static class AppliedSet
    {
        const string KEY = @"Software\ArchTweaker\Applied";
        public static List<string> Read()
        {
            try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.OpenSubKey(KEY)) return k == null ? new List<string>() : k.GetValueNames().ToList(); }
            catch { return new List<string>(); }
        }
        public static void Mark(Tweak t, bool applied)
        {
            try
            {
                using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(KEY))
                    if (applied) k.SetValue(t.Id, 1, RegistryValueKind.DWord); else k.DeleteValue(t.Id, false);
            }
            catch (Exception ex) { Log.W("Applied list: {0}", ex.Message); }
        }
        public static void Clear() { try { using (var b = Reg.Base(Hive.HKCU)) b.DeleteSubKeyTree(KEY, false); } catch (Exception ex) { Log.W("Applied list: could not clear: {0}", ex.Message); } }
    }

    // After a Windows feature update (build number change), lists the tweaks the update reset.
    public static class UpdateWatch
    {
        const string KEY = @"Software\ArchTweaker";
        public class Result { public int OldBuild, NewBuild; public List<DriftItem> Reset = new List<DriftItem>(); }

        // Returns null when the build hasn't changed (or on first run). Records the current build either way.
        public static Result Check()
        {
            int now = OSInfo.Build, before = 0;
            object o = Reg.Get(Hive.HKCU, KEY, "LastBuild");
            if (o != null) int.TryParse(o.ToString(), out before);
            try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(KEY)) k.SetValue("LastBuild", now.ToString()); }
            catch (Exception ex) { Log.W("Could not record the Windows build: {0}", ex.Message); }
            if (before == 0 || before == now) return null;
            var r = new Result { OldBuild = before, NewBuild = now };
            var ids = AppliedSet.Read();
            if (ids.Count > 0) r.Reset = Drift.Against(ids).Where(d => d.DesiredApplied && !d.Actual).ToList();
            Log.W("Windows build changed {0} → {1}: {2} of {3} applied tweak(s) were reset.", before, now, r.Reset.Count, ids.Count);
            return r;
        }
    }
}
