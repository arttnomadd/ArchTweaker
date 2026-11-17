// ArchTweaker 2.0: self-contained HTML health report — specs, applied tweaks, drift, cleanup potential.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ArchTweaker
{
    public static class HealthReport
    {
        static string Dir { get { string p = Path.Combine(Paths.Root, "reports"); Directory.CreateDirectory(p); return p; } }

        public static string Generate(bool includeCleanup)
        {
            var sb = new StringBuilder();
            sb.Append(@"<!doctype html><html><head><meta charset=""utf-8""><title>ArchTweaker health report</title><style>
:root{--bg:#131316;--panel:#1c1c21;--line:#2a2a31;--text:#e9e9ee;--mut:#9a9aa6;--acc:#ff3b47;--ok:#43c478;--warn:#e6b23a}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 'Segoe UI',system-ui,sans-serif;padding:32px}
h1{font-size:22px;margin:0 0 2px}h2{font-size:15px;color:var(--acc);margin:28px 0 10px;border-bottom:1px solid var(--line);padding-bottom:6px}
.sub{color:var(--mut);margin-bottom:18px}table{border-collapse:collapse;width:100%;max-width:900px}td,th{text-align:left;padding:6px 10px;border-bottom:1px solid var(--line);vertical-align:top}
th{color:var(--mut);font-weight:600}.k{color:var(--mut);width:220px}.tag{display:inline-block;background:var(--panel);border:1px solid var(--line);border-radius:4px;padding:1px 7px;margin:2px;font-size:12px}
.ok{color:var(--ok)}.warn{color:var(--warn)}.big{font-size:28px;font-weight:700}.cols{display:flex;gap:40px;flex-wrap:wrap}.card{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:14px 18px;min-width:180px}
small{color:var(--mut)}
.chart{max-width:720px;margin:6px 0}.row{display:flex;align-items:center;gap:10px;margin:3px 0}
.rl{width:170px;color:var(--mut);font-size:13px;text-align:right;flex:none}
.rt{height:14px;background:var(--panel);border-radius:4px;flex:none;min-width:2px}
.rf{display:block;height:14px;background:var(--acc);border-radius:4px;min-width:2px}
.rv{font-size:13px;color:var(--text);font-variant-numeric:tabular-nums}.rd{color:var(--mut)}</style></head><body>");
            sb.Append("<h1>ArchTweaker health report</h1>");
            sb.Append("<div class=sub>Generated " + ReportHtml.Esc(DateTime.Now.ToString("yyyy-MM-dd HH:mm")) + " · ArchTweaker " + ReportHtml.Esc(Program.Version) + "</div>");

            // ---- specs
            sb.Append("<h2>System</h2><table>");
            Row(sb, "OS", Try(() => OSInfo.Name));   // 3.18: Name already has the build
            Row(sb, "CPU", Try(() => HwProbe.Cpu != null ? HwProbe.Cpu.Name : "?"));
            Row(sb, "GPU", Try(() => HwProbe.GpuName()));
            Row(sb, "Memory", Try(() => { var r = HwProbe.RamSpeed(); return r != null && r.Length > 0 ? r[0] + " MT/s" + (r.Length > 1 ? " (rated " + r[1] + ")" : "") : "?"; }));
            Row(sb, "Display", Try(() => { var r = HwProbe.Refresh(); return r != null && r.Length > 0 ? string.Join(", ", r.Select(x => x + " Hz").ToArray()) : "?"; }));
            Row(sb, "System drive", Try(() => HwProbe.HasNvme() ? "NVMe SSD" : "SATA/other"));
            Row(sb, "Timer resolution", Try(() => TimerRes.CurrentMs().ToString("0.000") + " ms"));
            sb.Append("</table>");

            // ---- applied tweaks
            var applied = SafeList(() => Catalog.All.Where(t => t.Applicable && t.IsApplied()).ToList());
            sb.Append("<h2>Applied tweaks &nbsp;<small>" + applied.Count + " of " + Catalog.All.Count(t => t.Applicable) + " applicable</small></h2>");
            foreach (var g in applied.GroupBy(t => t.Category))
            {
                sb.Append("<div style='margin:10px 0'><b>" + ReportHtml.Esc(g.Key) + "</b><br>");
                foreach (var t in g) sb.Append("<span class=tag>" + ReportHtml.Esc(t.Title) + "</span>");
                sb.Append("</div>");
            }
            if (applied.Count == 0) sb.Append("<div class=sub>No tweaks applied.</div>");

            // ---- chart: applied per category (single-series magnitude → horizontal bars, one hue, direct labels)
            try
            {
                var applicableByCat = Catalog.All.Where(t => t.Applicable).GroupBy(t => t.Category)
                    .Select(g => new { Cat = g.Key, Total = g.Count(), On = g.Count(t => { try { return t.IsApplied(); } catch { return false; } }) })
                    .Where(x => x.Total > 0).OrderByDescending(x => x.On).ThenByDescending(x => x.Total).ToList();
                if (applicableByCat.Count > 0)
                {
                    int maxTotal = applicableByCat.Max(x => x.Total);
                    sb.Append("<h2>Applied by category</h2><div class=chart>");
                    foreach (var c in applicableByCat)
                    {
                        int trackPct = (int)Math.Round(100.0 * c.Total / maxTotal);
                        sb.Append("<div class=row title='" + ReportHtml.Esc(c.Cat + ": " + c.On + " of " + c.Total + " applied") + "'>"
                            + "<span class=rl>" + ReportHtml.Esc(c.Cat) + "</span>"
                            + "<span class=rt style='width:" + trackPct + "%'><span class=rf style='width:" + (trackPct == 0 ? 0 : (int)Math.Round(100.0 * c.On / c.Total)) + "%'></span></span>"
                            + "<span class=rv>" + c.On + "<span class=rd>/" + c.Total + "</span></span></div>");
                    }
                    sb.Append("</div>");
                }
            }
            catch { }

            // ---- drift vs active profile
            string ap = ActiveProfile();
            if (ap != null && File.Exists(ap))
            {
                var desired = SafeList(() => Profile.Read(ap).ToList());
                var drift = SafeList(() => Drift.Against(desired).ToList());
                sb.Append("<h2>Profile drift</h2>");
                sb.Append("<div class=sub>Active profile: " + ReportHtml.Esc(Path.GetFileName(ap)) + " (" + desired.Count + " tweaks)</div>");
                if (drift.Count == 0) sb.Append("<div class='ok'>In sync — every tweak in the profile is applied.</div>");
                else
                {
                    sb.Append("<div class='warn'>" + drift.Count + " tweak(s) from the profile are not applied (Windows may have reset them):</div>");
                    foreach (var d in drift) sb.Append("<span class=tag>" + ReportHtml.Esc(d.Tweak.Title) + "</span>");
                }
            }

            // ---- cleanup potential
            if (includeCleanup)
            {
                sb.Append("<h2>Cleanup potential</h2><div class=cols>");
                long junk = 0;
                try { var ts = CleanupModule.Targets(); foreach (var t in ts) { CleanupModule.Measure(t); junk += Math.Max(0, t.Size); } } catch { }
                sb.Append("<div class=card><div class=big>" + Fmt(junk) + "</div><small>temp / cache / logs (Cleanup page)</small></div>");
                sb.Append("</div><small>Run Deep clean for per-application leftovers from the Winapp2 ruleset.</small>");
            }

            sb.Append("<h2>Backups</h2><div class=sub>Undo journal, registry snapshots and leftover backups are in " + ReportHtml.Esc(Paths.Root) + ".</div>");
            sb.Append("</body></html>");

            string file = Path.Combine(Dir, "health_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".html");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            Log.W("Health report written: {0}", file);
            return file;
        }

        // Stored in HKLM (machine-wide) so the scheduled re-assert task can read it whatever account it runs under.
        public static string ActiveProfile()
        {
            object o = Reg.Get(Hive.HKLM, @"Software\ArchTweaker\Profile", "Active");
            // 3.2.1: no HKCU fallback: the elevated re-assert task must not follow a value any process of the user can write
            return o == null ? null : o.ToString();
        }
        public static void SetActiveProfile(string path)
        {
            using (var b = Reg.Base(Hive.HKLM)) using (var k = b.CreateSubKey(@"Software\ArchTweaker\Profile"))
                k.SetValue("Active", path ?? "", Microsoft.Win32.RegistryValueKind.String);
        }

        static void Row(StringBuilder sb, string k, string v) { sb.Append("<tr><td class=k>" + ReportHtml.Esc(k) + "</td><td>" + ReportHtml.Esc(v) + "</td></tr>"); }
        static string Try(Func<string> f) { try { return f() ?? "?"; } catch { return "?"; } }
        static List<T> SafeList<T>(Func<List<T>> f) { try { return f() ?? new List<T>(); } catch { return new List<T>(); } }
        static string Fmt(long b) { return Units.Bytes(b); }
    }

    // ---------- Export diagnostics (2.7): one zip to attach when something breaks ----------
    public static class Diagnostics
    {
        // Writes ArchTweaker-diagnostics-<date>.zip to the desktop and returns its path (throws on failure).
        public static string Export()
        {
            string name = "ArchTweaker-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip";
            string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name);
            using (var fs = new FileStream(dest, FileMode.CreateNew))
            using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
            {
                AddFile(zip, Paths.LogFile, "archtweaker.log");
                AddFile(zip, Paths.Journal, "undo.journal");
                AddFile(zip, Overlay.FilePath, "overlay.xml");
                AddText(zip, "health-report.html", Safe(() => HealthReport.Generate(false)));
                AddText(zip, "system.txt", Safe(() =>
                    "ArchTweaker " + Program.Version + "\r\nWindows " + OSInfo.Build + "\r\n" + HwFingerprint.Current() + "\r\n" +
                    "CPU: " + HwProbe.Cpu.Name + " (" + HwProbe.Cpu.PCores + "P/" + HwProbe.Cpu.ECores + "E, " + HwProbe.Cpu.Threads + " threads)\r\n" +
                    "NVIDIA driver API: " + (NvDrs.Available ? "yes" : "no") + "\r\n" +
                    "Display scale: " + (Theme.Dpi * 100 / 96) + " %\r\n" +
                    "Applied by ArchTweaker: " + string.Join(", ", AppliedSet.Read().ToArray()) + "\r\n"));
            }
            Log.W("Diagnostics exported: {0}", dest);
            return dest;
        }
        static string Safe(Func<string> f) { try { return f(); } catch (Exception ex) { return "(failed: " + ex.Message + ")"; } }
        static void AddFile(System.IO.Compression.ZipArchive z, string path, string entry)
        {
            if (!File.Exists(path)) return;
            var e = z.CreateEntry(entry);
            using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var dst = e.Open()) src.CopyTo(dst);
        }
        static void AddText(System.IO.Compression.ZipArchive z, string entry, string text)
        {
            var e = z.CreateEntry(entry);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(text);
        }
    }
}
