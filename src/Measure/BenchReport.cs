// ArchTweaker: benchmark HTML report (3.32 split from Benchmark.cs).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ArchTweaker
{
    public static class BenchReport
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string N(double v, string f) { return v.ToString(f, Inv); }
        static string Try(Func<string> f) { try { return f() ?? "?"; } catch { return "?"; } }

        public static string Write(BenchResult res, BenchRun prev)
        {
            string html = Html(res, prev);
            string file = Path.Combine(Bench.ReportDir, "bench_" + res.Run.When.ToString("yyyyMMdd-HHmmss", Inv) + ".html");
            File.WriteAllText(file, html, new UTF8Encoding(false));
            Log.W("Benchmark report written: {0}", file);
            return file;
        }

        // one card: name, value, unit, and the change against the previous run when there is one
        static void Card(StringBuilder sb, string name, double now, double? was, string fmt, string unit, bool higherBetter)
        {
            if (now < 0) return;
            sb.Append("<div class=card><div class=cn>" + ReportHtml.Esc(name) + "</div><div class=cv>" + N(now, fmt) + "<span class=cu> " + ReportHtml.Esc(unit) + "</span></div>");
            if (was.HasValue && was.Value > 0)
            {
                double d = (now - was.Value) / was.Value * 100.0;
                bool call = Math.Abs(d) >= 5, better = higherBetter ? d > 0 : d < 0;
                string cls = !call ? "mut" : better ? "ok" : "bad";
                sb.Append("<div class='cd " + cls + "'>" + (!call ? "≈" : d > 0 ? "▲" : "▼") + " " + N(Math.Abs(d), "0") + " %  <span class=mut>was " + N(was.Value, fmt) + "</span></div>");
            }
            sb.Append("</div>");
        }

        public static string Html(BenchResult res, BenchRun prev)
        {
            var r = res.Run; var f = res.Frames;
            var sb = new StringBuilder();
            sb.Append(@"<!doctype html><html><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""><title>ArchTweaker benchmark " + ReportHtml.Esc(r.When.ToString("yyyy-MM-dd HH:mm", Inv)) + @"</title><style>
:root{--bg:#131316;--panel:#1c1c21;--line:#2a2a31;--grid:#26262c;--text:#e9e9ee;--mut:#9a9aa6;--acc:#ff3b47;--ok:#43c478;--bad:#ff6b6b;--warn:#e6b23a}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 'Segoe UI',system-ui,sans-serif;padding:32px;max-width:1100px}
h1{font-size:22px;margin:0 0 2px}h2{font-size:15px;color:var(--text);margin:30px 0 10px;border-bottom:1px solid var(--line);padding-bottom:6px;font-weight:600}
.sub{color:var(--mut);margin-bottom:6px}.mut{color:var(--mut)}.ok{color:var(--ok)}.bad{color:var(--bad)}.warn{color:var(--warn)}
.cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(190px,1fr));gap:10px}
.card{background:var(--panel);border:1px solid var(--line);border-radius:6px;padding:12px 14px}
.cn{color:var(--mut);font-size:12px}.cv{font-size:24px;font-weight:600;font-variant-numeric:tabular-nums}.cu{font-size:12px;color:var(--mut);font-weight:400}.cd{font-size:12px;font-variant-numeric:tabular-nums}
table{border-collapse:collapse;width:100%}td,th{text-align:left;padding:5px 10px;border-bottom:1px solid var(--line);vertical-align:top}th{color:var(--mut);font-weight:600;font-size:12px}
td.n,th.n{text-align:right;font-variant-numeric:tabular-nums}.k{color:var(--mut);width:200px}
.tag{display:inline-block;background:var(--panel);border:1px solid var(--line);border-radius:4px;padding:1px 7px;margin:2px;font-size:12px}
.bar{display:inline-block;height:10px;border-radius:2px;background:var(--acc);vertical-align:middle;min-width:2px}
.chart{position:relative;background:var(--panel);border:1px solid var(--line);border-radius:6px;padding:10px}
.chart svg{display:block;width:100%;height:auto}.tip{position:absolute;pointer-events:none;background:#0b0b0d;border:1px solid var(--line);border-radius:4px;padding:3px 8px;font-size:12px;display:none;white-space:nowrap}
.note{color:var(--mut);font-size:12px;margin-top:6px}
</style></head><body>");
            sb.Append("<h1>Benchmark</h1><div class=sub>" + ReportHtml.Esc(r.When.ToString("yyyy-MM-dd HH:mm", Inv)) + " · ArchTweaker " + ReportHtml.Esc(Program.Version)
                + (r.Game.Length > 0 ? " · " + ReportHtml.Esc(r.Game) : "") + (r.Plan.Length > 0 ? " · power plan " + ReportHtml.Esc(r.Plan) : "") + "</div>");
            if (prev != null) sb.Append("<div class=sub>Changes are against the run of " + ReportHtml.Esc(prev.When.ToString("yyyy-MM-dd HH:mm", Inv)) + ". Changes under 5 % are run-to-run noise.</div>");
            Func<Func<BenchRun, double>, double?> was = g => prev == null ? (double?)null : g(prev);

            // ---- frames
            if (r.Game.Length > 0)
            {
                sb.Append("<h2>Frames: " + ReportHtml.Esc(r.Game) + " (" + r.FrameSecs + " s)</h2>");
                if (f == null) sb.Append("<div class=warn>No frame data. " + ReportHtml.Esc(res.FrameError ?? "") + "</div>");
                else
                {
                    sb.Append("<div class=cards>");
                    Card(sb, "Average FPS", r.AvgFps, was(p => p.AvgFps), "0.0", "FPS", true);
                    Card(sb, "1 % low", r.P1Fps, was(p => p.P1Fps), "0.0", "FPS", true);
                    Card(sb, "0.1 % low", r.P01Fps, was(p => p.P01Fps), "0.0", "FPS", true);
                    Card(sb, "Frame time, 99th percentile", r.P99Ms, was(p => p.P99Ms), "0.00", "ms", false);
                    Card(sb, "Stutters", r.Stutters, was(p => p.Stutters), "0", "frames", false);
                    if (f.CpuBusyMs >= 0) Card(sb, "CPU busy per frame", f.CpuBusyMs, null, "0.00", "ms", false);
                    if (f.GpuBusyMs >= 0) Card(sb, "GPU busy per frame", f.GpuBusyMs, null, "0.00", "ms", false);
                    if (f.DisplayLatencyMs >= 0) Card(sb, "Display latency", f.DisplayLatencyMs, null, "0.00", "ms", false);
                    sb.Append("</div>");
                    FrameChart(sb, f);
                    sb.Append("<table style='max-width:620px;margin-top:12px'><tr><th>Frames</th><th class=n>Median</th><th class=n>99th pct</th><th class=n>99.9th pct</th><th class=n>Worst</th></tr><tr><td>"
                        + f.Frames + "</td><td class=n>" + N(f.MedianMs, "0.00") + " ms</td><td class=n>" + N(f.P99Ms, "0.00") + " ms</td><td class=n>" + N(f.P999Ms, "0.00") + " ms</td><td class=n>" + N(f.MaxMs, "0.00") + " ms</td></tr></table>");
                    sb.Append("<div class=note>A stutter is a frame longer than 2.5 times the median frame. 1 % and 0.1 % low are the average FPS of the slowest 1 % and 0.1 % of frames."
                        + (f.PresentMode.Length > 0 ? " Present mode: " + ReportHtml.Esc(f.PresentMode) + "." : "") + (f.Runtime.Length > 0 ? " API: " + ReportHtml.Esc(f.Runtime) + "." : "") + "</div>");
                }
            }
            else sb.Append("<div class=note>No game was selected, so this run has no frame times.</div>");

            // ---- system latency
            sb.Append("<h2>System latency</h2><div class=cards>");
            Card(sb, "Worst DPC", r.WorstDpcUs, was(p => p.WorstDpcUs), "0", "µs", false);
            Card(sb, "Worst ISR", r.WorstIsrUs, was(p => p.WorstIsrUs), "0", "µs", false);
            Card(sb, "Spikes ≥ 500 µs", r.TraceSecs > 0 ? r.Spikes * 60.0 / r.TraceSecs : -1, was(p => p.TraceSecs > 0 ? p.Spikes * 60.0 / p.TraceSecs : 0), "0.0", "/min", false);
            Card(sb, "Timer resolution", r.TimerMs, was(p => p.TimerMs), "0.000", "ms", false);
            Card(sb, "Sleep(1), 99th percentile", r.SleepP99, was(p => p.SleepP99), "0.00", "ms", false);
            Card(sb, "Scheduling jitter (max)", r.JitterUs, was(p => p.JitterUs), "0.0", "µs", false);
            Card(sb, "Time in DPC", r.DpcPct, was(p => p.DpcPct), "0.00", "%", false);
            Card(sb, "Time in ISR", r.IsrPct, was(p => p.IsrPct), "0.00", "%", false);
            sb.Append("</div>");
            if (res.TraceError != null) sb.Append("<div class='note warn'>Driver trace: " + ReportHtml.Esc(res.TraceError) + "</div>");

            // ---- drivers
            var top = (res.Drivers ?? new List<DriverStat>()).OrderByDescending(d => d.MaxUs).Take(10).ToList();
            if (top.Count > 0)
            {
                double mx = Math.Max(1, top.Max(d => d.MaxUs));
                sb.Append("<h2>Slowest drivers (" + r.TraceSecs + " s)</h2><table><tr><th>Driver</th><th>Worst DPC/ISR</th><th class=n>DPCs</th><th class=n>ISRs</th><th class=n>≥ 500 µs</th><th class=n>≥ 1 ms</th></tr>");
                foreach (var d in top)
                {
                    int lv = DriverTrace.Level(d.MaxUs);
                    string cls = lv == 2 ? "bad" : lv == 1 ? "warn" : "";
                    sb.Append("<tr><td>" + ReportHtml.Esc(d.Driver) + "</td><td><span class=bar style='width:" + N(Math.Max(1, 260 * d.MaxUs / mx), "0") + "px'></span> <span class='" + cls + "'>" + N(d.MaxUs, "0") + " µs</span></td>"
                        + "<td class=n>" + d.Dpcs + "</td><td class=n>" + d.Isrs + "</td><td class=n>" + d.Over500 + "</td><td class=n>" + d.Over1000 + "</td></tr>");
                }
                sb.Append("</table><div class=note>Over 500 µs can cause audio crackle and frame hitches. Over 1000 µs is bad.</div>");
            }

            // ---- specs
            sb.Append("<h2>System</h2><table>");
            Action<string, string> row = (k, v) => sb.Append("<tr><td class=k>" + ReportHtml.Esc(k) + "</td><td>" + ReportHtml.Esc(v) + "</td></tr>");
            row("OS", Try(() => OSInfo.Name));
            row("CPU", Try(() => HwProbe.Cpu != null ? HwProbe.Cpu.Name : "?"));
            row("GPU", Try(() => HwProbe.GpuName()));
            row("Memory", Try(() => { var m = HwProbe.RamSpeed(); return m != null && m.Length > 0 ? m[0] + " MT/s" + (m.Length > 1 ? " (rated " + m[1] + ")" : "") : "?"; }));
            row("Display", Try(() => { var m = HwProbe.Refresh(); return m != null && m.Length > 0 ? string.Join(", ", m.Select(x => x + " Hz").ToArray()) : "?"; }));
            row("Power plan", r.Plan.Length > 0 ? r.Plan : "?");
            row("Frame capture", Try(() => PresentMon.Describe() ?? "PresentMon not found"));
            sb.Append("</table>");

            // ---- applied tweaks
            var ids = new HashSet<string>(Ids());
            var applied = Catalog.All.Where(t => ids.Contains(t.Id)).ToList();
            sb.Append("<h2>Applied tweaks <span class=mut>(" + applied.Count + ")</span></h2>");
            if (applied.Count == 0) sb.Append("<div class=note>ArchTweaker has applied no tweaks on this PC.</div>");
            foreach (var g in applied.GroupBy(t => t.Category))
            {
                sb.Append("<div style='margin:8px 0'><b>" + ReportHtml.Esc(g.Key) + "</b><br>");
                foreach (var t in g) sb.Append("<span class=tag>" + ReportHtml.Esc(t.Title) + "</span>");
                sb.Append("</div>");
            }
            sb.Append("</body></html>");
            return sb.ToString();
        }
        static List<string> Ids() { try { return AppliedSet.Read() ?? new List<string>(); } catch { return new List<string>(); } }

        // Frame time over the capture. Buckets keep their longest frame, so a stutter is never averaged away.
        public static List<double> Buckets(List<double> ms, int n)
        {
            var o = new List<double>();
            if (ms == null || ms.Count == 0) return o;
            if (ms.Count <= n) return ms.ToList();
            double per = (double)ms.Count / n;
            for (int i = 0; i < n; i++)
            {
                int a = (int)(i * per), b = Math.Max(a + 1, (int)((i + 1) * per));
                double m = 0; for (int j = a; j < b && j < ms.Count; j++) m = Math.Max(m, ms[j]);
                o.Add(m);
            }
            return o;
        }
        static void FrameChart(StringBuilder sb, FrameStats f)
        {
            var b = Buckets(f.Ms, 900);
            if (b.Count < 2) return;
            const int W = 1000, H = 260, L = 46, R = 10, T = 12, B = 26;
            double top = TrendChart.Nice(Math.Max(f.P999Ms * 1.25, f.MedianMs * 2));
            double max = b.Max(); bool clip = max > top;
            Func<double, double> y = v => T + (H - T - B) * (1 - Math.Min(v, top) / top);
            Func<int, double> x = i => L + (W - L - R) * (double)i / (b.Count - 1);
            sb.Append("<h2>Frame time</h2><div class=chart id=fc><svg viewBox='0 0 " + W + " " + H + "' role=img aria-label='Frame time over the capture'>");
            for (int k = 0; k <= 4; k++)
            {
                double v = top * k / 4; double yy = y(v);
                sb.Append("<line x1=" + L + " x2=" + (W - R) + " y1=" + N(yy, "0.0") + " y2=" + N(yy, "0.0") + " stroke='var(--grid)' stroke-width=1 />");
                sb.Append("<text x=" + (L - 6) + " y=" + N(yy + 4, "0.0") + " fill='var(--mut)' font-size=11 text-anchor=end>" + N(v, v < 10 ? "0.#" : "0") + "</text>");
            }
            double ym = y(f.MedianMs);
            sb.Append("<line x1=" + L + " x2=" + (W - R) + " y1=" + N(ym, "0.0") + " y2=" + N(ym, "0.0") + " stroke='var(--mut)' stroke-dasharray='4 4' stroke-width=1 />");
            var pts = new StringBuilder();
            for (int i = 0; i < b.Count; i++) pts.Append(N(x(i), "0.0")).Append(',').Append(N(y(b[i]), "0.0")).Append(' ');
            sb.Append("<polyline fill=none stroke='var(--acc)' stroke-width=1.5 stroke-linejoin=round points='" + pts.ToString().Trim() + "' />");
            sb.Append("<text x=" + L + " y=" + (H - 6) + " fill='var(--mut)' font-size=11>start</text><text x=" + (W - R) + " y=" + (H - 6) + " fill='var(--mut)' font-size=11 text-anchor=end>end</text>");
            sb.Append("<line id=fx y1=" + T + " y2=" + (H - B) + " stroke='var(--mut)' stroke-width=1 visibility=hidden /></svg><div class=tip id=ft></div></div>");
            sb.Append("<div class=note>ms per frame, lower is better. The dashed line is the median frame (" + N(f.MedianMs, "0.00") + " ms). Each point is the longest frame in its slice of the capture." + (clip ? " Frames over " + N(top, "0") + " ms reach the top edge (worst " + N(max, "0.0") + " ms)." : "") + "</div>");
            sb.Append("<script>(function(){var d=[" + string.Join(",", b.Select(v => N(v, "0.00")).ToArray()) + "],W=" + W + ",L=" + L + ",R=" + R
                + ",c=document.getElementById('fc'),s=c.querySelector('svg'),t=document.getElementById('ft'),l=document.getElementById('fx');"
                + "s.addEventListener('mousemove',function(e){var r=s.getBoundingClientRect(),vx=(e.clientX-r.left)*W/r.width,i=Math.round((vx-L)/(W-L-R)*(d.length-1));"
                + "if(i<0||i>=d.length){t.style.display='none';l.setAttribute('visibility','hidden');return;}var px=L+(W-L-R)*i/(d.length-1);l.setAttribute('x1',px);l.setAttribute('x2',px);l.setAttribute('visibility','visible');"
                + "t.textContent=d[i].toFixed(2)+' ms';t.style.display='block';t.style.left=(e.clientX-c.getBoundingClientRect().left+12)+'px';t.style.top='14px';});"
                + "s.addEventListener('mouseleave',function(){t.style.display='none';l.setAttribute('visibility','hidden');});})();</script>");
        }
    }

    // Newest benchmark against the one before it with the same game.
}
