// ArchTweaker: shared head and style for the tuner HTML reports (Interrupt tuner, Power tuner, Tuning assistant,
// Tune everything). Benchmark and the health report add their own chart and card styles.
using System.Net;

namespace ArchTweaker
{
    static class ReportHtml
    {
        public const string Css =
            ":root{--bg:#131316;--panel:#1c1c21;--line:#2a2a31;--text:#e9e9ee;--mut:#9a9aa6;--ok:#43c478;--bad:#ff6b6b}"
            + "body{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 'Segoe UI',system-ui,sans-serif;padding:32px;max-width:1100px}"
            + "h1{font-size:22px;margin:0 0 4px}h2{font-size:15px;margin:26px 0 6px}.mut{color:var(--mut)}.ok{color:var(--ok)}.bad{color:var(--bad)}a{color:var(--bad)}"
            + "table{border-collapse:collapse;width:100%;margin-top:16px}td,th{text-align:left;padding:6px 10px;border-bottom:1px solid var(--line)}"
            + "th{color:var(--mut);font-size:12px}td.n,th.n{text-align:right;font-variant-numeric:tabular-nums}";

        public static string Esc(string t) { return WebUtility.HtmlEncode(t ?? ""); }

        // "<!doctype html>…<body>" with the shared style; the title is escaped here.
        public static string Head(string title)
        {
            return "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>"
                + Esc(title) + "</title><style>" + Css + "</style></head><body>";
        }
    }
}
