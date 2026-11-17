// ArchTweaker: Tweak self-test: every op reads and writes what it claims.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- self-test: apply, check, undo, put back, for every plain registry tweak ----------------
    // 3.7: the self-test covers every tweak whose changes can be put back exactly: registry values and keys,
    // services, scheduled tasks, power-plan settings, NVIDIA profile settings, device registry and DirectX tokens.
    // Tweaks that run commands, disable devices or change network adapters are only read (state + how it is detected).
    public static class SelfTest
    {
        public enum Kind { Full, ReadOnly }

        static bool Reversible(Op o)
        {
            return o is VOp || o is KeepOp || o is KeyAddOp || o is KeyDelOp || o is SvcOp || o is SvcPrevOp || o is TaskOp
                || o is PowerOp || o is NvDrsOp || o is DeviceRegOp || o is DxTokenOp;
        }

        // null = apply/check/undo is safe here; otherwise why not
        public static string WhyNot(Tweak t)
        {
            if (t.Dangerous) return "Danger zone";
            if (!t.Applicable) return "not available on this PC";
            if (t.UndoNote != null) return "no undo";
            if (t.Ops.Count == 0) return "runs an action";
            if (t.ApplyCmds.Count > 0 || t.ApplyExtra != null) return "runs commands";
            var bad = t.Ops.FirstOrDefault(o => !Reversible(o));
            if (bad != null) return bad.Source;
            return null;
        }
        public static bool Testable(Tweak t) { return WhyNot(t) == null; }

        // what the op changes, read before the test so it can be written back exactly
        sealed class Snap { public Action Restore; public Func<string> Check; }
        static IEnumerable<Snap> Take(Op o)
        {
            var vo = o as VOp;
            if (vo != null) { yield return RegSnap(vo.H, vo.Path, vo.Name); yield break; }
            var ko = o as KeepOp;
            if (ko != null) { yield return RegSnap(ko.H, ko.Path, ko.Name); yield break; }
            var so = o as SvcOp;
            if (so != null && Svc.Exists(so.Name))
            {
                int st = Svc.GetStart(so.Name); bool dl = Svc.GetDelayed(so.Name); string n = so.Name;
                yield return new Snap { Restore = () => Svc.SetStart(n, st, dl), Check = () => Svc.GetStart(n) == st ? null : "service " + n + " start not back to " + st };
                yield break;
            }
            var dx = o as DxTokenOp;
            if (dx != null) yield return RegSnap(Hive.HKCU, DxTokenOp.Key, DxTokenOp.Value);
            // PowerOp, NvDrsOp, SvcPrevOp, DeviceRegOp, TaskOp, KeyAddOp, KeyDelOp save the original themselves and undo restores it
        }
        static Snap RegSnap(Hive h, string path, string name)
        {
            object v = Reg.Get(h, path, name);
            var k = v != null ? Reg.GetKind(h, path, name) : RegistryValueKind.Unknown;
            return new Snap
            {
                Restore = () => { if (v == null) Reg.DeleteValue(h, path, name); else Reg.Set(h, path, name, k, v); },
                Check = () => Reg.Norm(Reg.Get(h, path, name)) == Reg.Norm(v) ? null : "value " + name + " not back to its original"
            };
        }

        public static string Run(IEnumerable<Tweak> tweaks, Kind kind, out int pass, out int fail, out int read)
        {
            pass = fail = read = 0;
            var sb = new StringBuilder();
            sb.AppendLine("ArchTweaker " + Program.Version + " self-test (" + (kind == Kind.Full ? "full" : "read-only") + ") · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · " + OSInfo.Name + " · " + OSInfo.Edition);
            sb.AppendLine("PASS = applied, detected, undone, original values back.  READ = only the current state was read (with how it is read).  SKIP = not run.");
            sb.AppendLine();
            foreach (var g in tweaks.GroupBy(t => t.Category))
            {
                sb.AppendLine("== " + g.Key);
                foreach (var t in g)
                {
                    string why = WhyNot(t);
                    if (t.Dangerous || !t.Applicable) { sb.AppendLine("SKIP  " + t.Id + "  (" + why + (t.Applicable ? "" : ": " + t.UnavailableReason) + ")"); continue; }
                    bool wasOn; string rerr = null;
                    try { wasOn = t.IsApplied(); } catch (Exception ex) { wasOn = false; rerr = ex.Message; }
                    if (rerr != null) { fail++; sb.AppendLine("FAIL  " + t.Id + "  state check threw: " + rerr); continue; }
                    if (kind == Kind.ReadOnly || why != null)
                    {
                        read++;
                        sb.AppendLine("READ  " + t.Id + "  " + (wasOn ? "on" : "off") + "  · " + t.DetectSource + (why != null && kind == Kind.Full ? "  (not changed: " + why + ")" : ""));
                        continue;
                    }
                    if (wasOn) { sb.AppendLine("SKIP  " + t.Id + "  (already applied)"); continue; }
                    var snaps = t.Ops.SelectMany(Take).ToList();
                    string err = null;
                    try
                    {
                        t.Apply();
                        StateCache.Clear();
                        if (!t.IsApplied())
                        {
                            var off = t.Ops.Where(o => { try { return !o.IsOn(); } catch { return true; } }).Select(o => o.Describe().Split('\n')[0].Trim()).Take(3).ToArray();
                            err = "not detected as applied after Apply" + (off.Length > 0 ? ": " + string.Join(" | ", off) : "");
                        }
                        t.Revert();
                        StateCache.Clear();
                        if (err == null && t.IsApplied()) err = "still detected as applied after Undo";
                    }
                    catch (Exception ex) { err = ex.Message; }
                    finally
                    {
                        foreach (var x in snaps) { try { x.Restore(); } catch (Exception ex) { err = (err ?? "") + " · restore: " + ex.Message; } }
                        State.Set(t.Id, false);
                        StateCache.Clear();
                    }
                    if (err == null) foreach (var x in snaps) { string c = x.Check(); if (c != null) { err = c; break; } }
                    if (err == null) { pass++; sb.AppendLine("PASS  " + t.Id); }
                    else { fail++; sb.AppendLine("FAIL  " + t.Id + "  " + err); }
                }
                sb.AppendLine();
            }
            sb.AppendLine(pass + " passed, " + fail + " failed, " + read + " read only");
            return sb.ToString();
        }

        // 3.5 signature (unit tests, older callers): full test
        public static string Run(IEnumerable<Tweak> tweaks, out int pass, out int fail) { int r; return Run(tweaks, Kind.Full, out pass, out fail, out r); }

        public static string RunToFile(Kind kind, out int pass, out int fail, out int read)
        {
            StateCache.Clear();
            string report = Run(Catalog.All, kind, out pass, out fail, out read);
            string file = Path.Combine(Paths.Root, "selftest-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            try { File.WriteAllText(file, report); } catch (Exception ex) { Log.W("Self-test report: {0}", ex.Message); }
            StateCache.Clear();
            return file;
        }
        public static string RunToFile(out int pass, out int fail) { int r; return RunToFile(Kind.Full, out pass, out fail, out r); }
    }
}
