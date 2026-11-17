// ArchTweaker 1.7: online hosts blocklists.
// Curated sources (with mirrors), download + local cache, a parser for hosts / plain-domain / adblock formats,
// a safety allowlist, compact 9-names-per-line output and an optional weekly auto-update task.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ArchTweaker
{
    public class HostSource
    {
        public string Id, Name, Group, Desc, Home, License;
        public string[] Urls;           // primary first, then mirrors
        public int Approx;              // typical entry count (0 = unknown until first download)
        public bool Default;            // ticked on first use
        public string Caution;          // shown in amber when set
        public string[] Builtin;        // offline list, no download
    }

    public static class HostSources
    {
        public const string G_ADS = "Ads & trackers", G_WIN = "Windows & Office telemetry", G_SEC = "Malware & phishing", G_CAT = "Block categories";
        public static readonly string[] Groups = { G_ADS, G_WIN, G_SEC, G_CAT };

        const string SB = "https://raw.githubusercontent.com/StevenBlack/hosts/master/";
        // HaGeZi moved the Hosts format to a separate "legacy" repo (2026); the main repo no longer has /hosts.
        // 3.38: checked 16 Nov 2026: the legacy repo is built daily (light.txt "Last modified: 16 Nov 2026"); sizes below are
        // the entry counts of that day. Its bigger lists (Multi ≈404k, Pro ≈518k) are too large for a Windows hosts file.
        const string HZ = "https://raw.githubusercontent.com/hagezi/dns-blocklists-legacy/main/hosts/";
        const string HZJ = "https://cdn.jsdelivr.net/gh/hagezi/dns-blocklists-legacy@latest/hosts/";

        public static readonly List<HostSource> All = new List<HostSource>
        {
            // ---- ads & trackers
            new HostSource { Id = "adaway", Group = G_ADS, Name = "AdAway default", Approx = 6500, Default = true,
                Desc = "Small, conservative list of ad and tracking servers from the AdAway project. Rarely breaks anything.",
                Urls = new[] { "https://raw.githubusercontent.com/AdAway/adaway.github.io/master/hosts.txt", "https://adaway.org/hosts.txt" },
                Home = "https://adaway.org", License = "CC BY 3.0" },
            new HostSource { Id = "yoyo", Group = G_ADS, Name = "Peter Lowe's ad & tracking servers", Approx = 3800, Default = true,
                Desc = "Hand-maintained since 2001. Very low false-positive rate.",
                Urls = new[] { "https://pgl.yoyo.org/adservers/serverlist.php?hostformat=hosts&mimetype=plaintext&useip=0.0.0.0" },
                Home = "https://pgl.yoyo.org/adservers/" },
            new HostSource { Id = "pollock", Group = G_ADS, Name = "Dan Pollock's hosts", Approx = 11700, Default = true,
                Desc = "Ads, banners, tracking cookies, web bugs and some malware sites.",
                Urls = new[] { "https://someonewhocares.org/hosts/zero/hosts" },
                Home = "https://someonewhocares.org/hosts/" },
            new HostSource { Id = "stevenblack", Group = G_ADS, Name = "StevenBlack unified (adware + malware)", Approx = 76000,
                Desc = "Merges AdAway, Peter Lowe, Dan Pollock, URLhaus and more into one de-duplicated list. The most complete single hosts file. Already includes the three lists above.",
                Caution = "Large list. Fine on most PCs. If the first lookups after boot are slow, use the three small lists above.",
                Urls = new[] { SB + "hosts", "https://cdn.jsdelivr.net/gh/StevenBlack/hosts@master/hosts" },
                Home = "https://github.com/StevenBlack/hosts", License = "MIT" },
            new HostSource { Id = "hagezi_light", Group = G_ADS, Name = "HaGeZi Multi Light", Approx = 164000,
                Desc = "Ads, tracking, metrics and some badware, curated to avoid breaking sites (\"should not lead to any restrictions\").",
                Caution = "Very large (≈164k with subdomains): Windows' DNS Client may use high CPU and lookups can stall. Pick this OR StevenBlack, not both.",
                Urls = new[] { HZ + "light.txt", HZJ + "light.txt" },
                Home = "https://github.com/hagezi/dns-blocklists", License = "GPL-3.0" },

            // ---- Windows / Office
            new HostSource { Id = "telemetry_starter", Group = G_WIN, Name = "ArchTweaker telemetry starter", Approx = 20,
                Desc = "The 20 core Windows telemetry endpoints from earlier versions. Built in, no download.",
                Caution = "Contains Microsoft domains: Defender may report 'HostsFileHijack'.",
                Builtin = HostsModule.Default },
            new HostSource { Id = "wsb_spy", Group = G_WIN, Name = "WindowsSpyBlocker: spy", Approx = 350,
                Desc = "Windows telemetry hosts found by capturing real Windows traffic. Leaves Windows Update alone. It last changed in May 2022. HaGeZi Windows/Office below gets updates every day.",
                Caution = "Contains Microsoft domains: Defender may report 'HostsFileHijack'.",
                Urls = new[] { "https://raw.githubusercontent.com/crazy-max/WindowsSpyBlocker/master/data/hosts/spy.txt" },
                Home = "https://github.com/crazy-max/WindowsSpyBlocker", License = "MIT" },
            new HostSource { Id = "hagezi_winoffice", Group = G_WIN, Name = "HaGeZi Windows/Office tracker", Approx = 780,
                Desc = "Actively maintained list of Windows and Office telemetry (Application Insights, Office telemetry, Bing APIs…).",
                Caution = "Contains Microsoft domains: Defender may report 'HostsFileHijack'. Some Office/Store features may stop reporting status.",
                Urls = new[] { HZ + "native.winoffice.txt", HZJ + "native.winoffice.txt" },
                Home = "https://github.com/hagezi/dns-blocklists", License = "GPL-3.0" },

            // ---- security
            new HostSource { Id = "urlhaus", Group = G_SEC, Name = "URLhaus malware domains (online only)", Approx = 0, Default = true,
                Desc = "Domains that serve malware now, from abuse.ch URLhaus. The list has only active entries, so it stays small.",
                Urls = new[] { "https://malware-filter.gitlab.io/malware-filter/urlhaus-filter-hosts-online.txt", "https://curbengh.github.io/malware-filter/urlhaus-filter-hosts-online.txt", "https://malware-filter.pages.dev/urlhaus-filter-hosts-online.txt" },
                Home = "https://gitlab.com/malware-filter/urlhaus-filter" },
            new HostSource { Id = "phishing", Group = G_SEC, Name = "Phishing domains", Approx = 0,
                Desc = "Phishing sites from PhishTank, OpenPhish and others, through malware-filter.",
                Urls = new[] { "https://malware-filter.gitlab.io/phishing-filter/phishing-filter-hosts.txt", "https://curbengh.github.io/phishing-filter/phishing-filter-hosts.txt", "https://malware-filter.pages.dev/phishing-filter-hosts.txt" },
                Home = "https://gitlab.com/malware-filter/phishing-filter" },
            new HostSource { Id = "hagezi_doh", Group = G_SEC, Name = "Block browsers' encrypted DNS (DoH)", Approx = 4000,
                Desc = "Blocks DNS-over-HTTPS servers, so browsers fall back to Windows DNS. Use it if a site you blocked here still loads in the browser.",
                Caution = "Turns off 'secure DNS' in browsers and apps that rely on it.",
                Urls = new[] { HZ + "doh.txt", HZJ + "doh.txt" },
                Home = "https://github.com/hagezi/dns-blocklists", License = "GPL-3.0" },

            // ---- categories
            new HostSource { Id = "sb_gambling", Group = G_CAT, Name = "Gambling", Approx = 6700, Desc = "Betting and casino sites (StevenBlack extension).",
                Urls = new[] { SB + "alternates/gambling-only/hosts" }, Home = "https://github.com/StevenBlack/hosts", License = "MIT" },
            new HostSource { Id = "sb_social", Group = G_CAT, Name = "Social media", Approx = 3800, Desc = "Facebook, Instagram, TikTok, X and similar (StevenBlack extension).",
                Caution = "Blocks the social networks themselves, not only their trackers.",
                Urls = new[] { SB + "alternates/social-only/hosts" }, Home = "https://github.com/StevenBlack/hosts", License = "MIT" },
            new HostSource { Id = "sb_fakenews", Group = G_CAT, Name = "Fake news", Approx = 2200, Desc = "Known fake-news sites (StevenBlack extension).",
                Urls = new[] { SB + "alternates/fakenews-only/hosts" }, Home = "https://github.com/StevenBlack/hosts", License = "MIT" },
            new HostSource { Id = "sb_porn", Group = G_CAT, Name = "Adult content", Approx = 77000, Desc = "Adult sites (StevenBlack extension).",
                Caution = "Large list.",
                Urls = new[] { SB + "alternates/porn-only/hosts" }, Home = "https://github.com/StevenBlack/hosts", License = "MIT" },
        };

        public static HostSource Get(string id) { return All.FirstOrDefault(s => s.Id == id); }
    }

    public class HostsConfig
    {
        public List<string> Sources = new List<string>();
        public bool Compact = true, SkipMicrosoft, GamingSafe = true, AutoUpdate;
        public List<string> Allow = new List<string>(), Custom = new List<string>();
        public DateTime LastUpdate;

        static string Dir { get { string p = Path.Combine(Paths.Root, "hosts"); Directory.CreateDirectory(p); return p; } }
        public static string CacheDir { get { string p = Path.Combine(Dir, "cache"); Directory.CreateDirectory(p); return p; } }
        static string Cfg { get { return Path.Combine(Dir, "config.txt"); } }
        static string AllowFile { get { return Path.Combine(Dir, "allowlist.txt"); } }
        static string CustomFile { get { return Path.Combine(Dir, "my-blocklist.txt"); } }

        public static HostsConfig Load()
        {
            var c = new HostsConfig();
            Secure.DropUntrusted(Cfg, "the hosts settings"); Secure.DropUntrusted(AllowFile, "the hosts allowlist"); Secure.DropUntrusted(CustomFile, "the custom hosts blocklist");   // re-created on Save
            if (!File.Exists(Cfg)) { c.Sources = HostSources.All.Where(s => s.Default).Select(s => s.Id).ToList(); }
            else
                foreach (var l in File.ReadAllLines(Cfg))
                {
                    int i = l.IndexOf('='); if (i <= 0) continue;
                    string k = l.Substring(0, i).Trim(), v = l.Substring(i + 1).Trim();
                    if (k == "sources") c.Sources = v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    else if (k == "compact") c.Compact = v == "1";
                    else if (k == "skipms") c.SkipMicrosoft = v == "1";
                    else if (k == "gamingsafe") c.GamingSafe = v == "1";
                    else if (k == "auto") c.AutoUpdate = v == "1";
                    else if (k == "updated") { DateTime d; if (DateTime.TryParse(v, null, System.Globalization.DateTimeStyles.RoundtripKind, out d)) c.LastUpdate = d; }
                }
            if (File.Exists(AllowFile)) c.Allow = File.ReadAllLines(AllowFile).ToList();
            if (File.Exists(CustomFile)) c.Custom = File.ReadAllLines(CustomFile).ToList();
            return c;
        }

        public void Save()
        {
            File.WriteAllLines(Cfg, new[]
            {
                "sources=" + string.Join(",", Sources.ToArray()),
                "compact=" + (Compact ? 1 : 0), "skipms=" + (SkipMicrosoft ? 1 : 0), "gamingsafe=" + (GamingSafe ? 1 : 0), "auto=" + (AutoUpdate ? 1 : 0),
                "updated=" + (LastUpdate == DateTime.MinValue ? "" : LastUpdate.ToString("o")),
            });
            File.WriteAllLines(AllowFile, Allow);
            File.WriteAllLines(CustomFile, Custom);
        }
    }

    public class BuildResult
    {
        public int Total, Allowed, Microsoft;
        public Dictionary<string, int> PerSource = new Dictionary<string, int>();
        public List<string> Failed = new List<string>(), FromCache = new List<string>();
        public List<string> Domains = new List<string>();
    }

    public static class HostsEngine
    {
        // Never blocked, whatever a list says: keeps Windows Update, the network-status icon, sign-in and this updater working.
        public static readonly string[] Safety =
        {
            "msftconnecttest.com", "msftncsi.com",
            "windowsupdate.com", "update.microsoft.com", "delivery.mp.microsoft.com", "dl.delivery.mp.microsoft.com",
            "login.live.com", "login.microsoftonline.com", "account.live.com", "login.windows.net",
            "activation.sls.microsoft.com", "validation.sls.microsoft.com", "licensing.mp.microsoft.com",
            "github.com", "raw.githubusercontent.com", "objects.githubusercontent.com", "github.io", "cdn.jsdelivr.net",
            "gitlab.io", "pages.dev", "pgl.yoyo.org", "someonewhocares.org", "adaway.org",
        };
        // Optional: game launchers & voice chat, so an aggressive list can't break them.
        public static readonly string[] Gaming =
        {
            "steampowered.com", "steamcommunity.com", "steamcontent.com", "steamstatic.com", "steamserver.net",
            "epicgames.com", "epicgames.dev", "unrealengine.com", "battle.net", "blizzard.com",
            "riotgames.com", "leagueoflegends.com", "ea.com", "origin.com", "ubisoft.com", "ubi.com",
            "xboxlive.com", "discord.com", "discordapp.com", "discord.gg", "discordapp.net", "faceit.com", "battleye.com", "easy.ac",
        };
        static readonly string[] MsSuffixes = { "microsoft.com", "windows.com", "windows.net", "live.com", "msn.com", "bing.com", "bing.net", "office.com", "office.net", "office365.com", "skype.com", "msedge.net", "azure.com", "azureedge.net", "microsoftonline.com", "sharepoint.com", "onedrive.com", "xbox.com", "akadns.net", "nsatc.net", "applicationinsights.io", "visualstudio.com" };

        static readonly Regex Dom = new Regex(@"^(?=.{1,253}$)(?:[a-z0-9_](?:[a-z0-9_-]{0,61}[a-z0-9_])?\.)+[a-z0-9][a-z0-9-]{0,61}[a-z0-9]$", RegexOptions.Compiled);
        static readonly HashSet<string> Junk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "localhost", "localhost.localdomain", "local", "broadcasthost", "ip6-localhost", "ip6-loopback", "ip6-localnet", "ip6-mcastprefix", "ip6-allnodes", "ip6-allrouters", "ip6-allhosts", "0.0.0.0" };

        public static bool Suffix(string d, string s) { return d == s || d.EndsWith("." + s, StringComparison.Ordinal); }
        public static bool IsMicrosoft(string d) { return MsSuffixes.Any(s => Suffix(d, s)); }

        public static string Clean(string token)
        {
            string t = token.Trim().ToLowerInvariant().TrimEnd('.');
            if (t.StartsWith("||")) { t = t.Substring(2); int c = t.IndexOfAny(new[] { '^', '$', '/' }); if (c >= 0) t = t.Substring(0, c); }
            if (t.StartsWith("*.")) t = t.Substring(2);
            if (t.Length == 0 || Junk.Contains(t)) return null;
            if (t.All(ch => char.IsDigit(ch) || ch == '.' || ch == ':')) return null;   // IP address
            return Dom.IsMatch(t) ? t : null;
        }

        // Accepts hosts files ("0.0.0.0 a b"), plain domain lists, and simple adblock rules ("||a^").
        public static IEnumerable<string> Parse(string text)
        {
            foreach (var raw in text.Split('\n'))
            {
                string line = raw;
                if (line.Contains("##") || line.Contains("#@#") || line.Contains("#?#") || line.Contains("#$#")) continue;   // adblock cosmetic rules, not domains
                int h = line.IndexOf('#'); if (h >= 0) line = line.Substring(0, h);
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("!") || line.StartsWith("[") || line.StartsWith("@@")) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int start = 0;
                if (parts.Length >= 2 && (parts[0] == "0.0.0.0" || parts[0] == "127.0.0.1" || parts[0] == "::" || parts[0] == "::1" || parts[0] == "0" || parts[0].Contains(":") || Regex.IsMatch(parts[0], @"^\d+\.\d+\.\d+\.\d+$"))) start = 1;
                else if (parts.Length != 1) continue;
                for (int i = start; i < parts.Length; i++) { string d = Clean(parts[i]); if (d != null) yield return d; }
            }
        }

        public static string CacheFile(string id) { return Path.Combine(HostsConfig.CacheDir, id + ".txt"); }

        public static int CachedCount(string id)
        {
            try { string f = CacheFile(id); Secure.DropUntrusted(f, "a hosts source cache"); return File.Exists(f) ? Parse(File.ReadAllText(f)).Distinct().Count() : -1; } catch { return -1; }
        }
        public static DateTime CachedAt(string id) { string f = CacheFile(id); return File.Exists(f) ? File.GetLastWriteTime(f) : DateTime.MinValue; }

        // 3.38: the date the list's maintainer wrote into its header (HaGeZi "# Last modified: 16 Nov 2026 08:36 UTC",
        // StevenBlack "# Date: 15 November 2026 20:29:11 (UTC)", Dan Pollock "# Last updated: Sat, 17 Jun 2023 at …",
        // ISO "# Updated: 2026-11-08"). Only the first comment lines are read. null when the list has no date.
        static readonly Regex HeadDate = new Regex(@"^[#!]+\s*(?:last[ -]?(?:modified|updated)|updated|date|version)\s*:?\s*(?:[A-Za-z]{3},?\s*)?(?:(\d{4})-(\d{2})-(\d{2})|(\d{1,2})\s+([A-Za-z]{3,9})\.?\s+(\d{4}))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
        public static DateTime? ListDate(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int seen = 0;
            foreach (var raw in text.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0) continue;
                if (l[0] != '#' && l[0] != '!') { if (++seen > 3) break; continue; }   // the header ends where the entries start
                var m = HeadDate.Match(l);
                if (!m.Success) continue;
                try
                {
                    if (m.Groups[1].Success) return new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
                    int mo = Array.IndexOf(Months, m.Groups[5].Value.Substring(0, 3).ToLowerInvariant());
                    if (mo >= 0) return new DateTime(int.Parse(m.Groups[6].Value), mo + 1, int.Parse(m.Groups[4].Value));
                }
                catch (ArgumentException) { }   // 31 Feb and the like
            }
            return null;
        }
        // the header date of the downloaded copy (reads only the start of the file)
        public static DateTime? CachedListDate(string id)
        {
            try
            {
                string f = CacheFile(id); if (!File.Exists(f)) return null;
                var buf = new char[16384]; int n;
                using (var r = new StreamReader(f)) n = r.ReadBlock(buf, 0, buf.Length);
                return ListDate(new string(buf, 0, n));
            }
            catch { return null; }
        }
        // a list its maintainer has not updated for half a year
        public const int StaleDays = 183;
        public static bool Stale(DateTime? listDate, DateTime now) { return listDate.HasValue && (now - listDate.Value).TotalDays > StaleDays; }

        static string Download(string url)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)768; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "ArchTweaker/" + Program.Version + " (hosts updater)";
            req.Timeout = 45000; req.ReadWriteTimeout = 60000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        // Returns the source's text: fresh download (first working mirror), else the cached copy, else null.
        public static string Fetch(HostSource src, bool allowNetwork, out bool fromCache, out string error)
        {
            fromCache = false; error = null;
            if (src.Builtin != null) return string.Join("\n", src.Builtin);
            if (allowNetwork)
                foreach (var u in src.Urls)
                {
                    try
                    {
                        string t = Download(u);
                        int n = Parse(t).Take(5).Count();
                        if (n == 0) { error = "no domains in " + u; continue; }
                        File.WriteAllText(CacheFile(src.Id), t, new UTF8Encoding(false));
                        return t;
                    }
                    catch (Exception ex) { error = ex.Message; }
                }
            string f = CacheFile(src.Id);
            if (File.Exists(f)) { fromCache = true; return File.ReadAllText(f); }
            return null;
        }

        public static IEnumerable<string> Lines(List<string> l) { return l.Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("#")); }

        public static BuildResult Build(HostsConfig cfg, bool allowNetwork, Action<string> progress)
        {
            var res = new BuildResult();
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in cfg.Sources)
            {
                var src = HostSources.Get(id); if (src == null) continue;
                if (progress != null) progress(src.Builtin != null ? "Adding " + src.Name + "…" : "Downloading " + src.Name + "…");
                bool cached; string err;
                string text = Fetch(src, allowNetwork, out cached, out err);
                if (text == null) { res.Failed.Add(src.Name + (err != null ? " (" + err + ")" : "")); Log.W("  {0}: download failed and nothing cached — skipped. {1}", src.Name, err); continue; }
                if (cached) { res.FromCache.Add(src.Name); Log.W("  {0}: offline, using the copy from {1:yyyy-MM-dd}.", src.Name, CachedAt(src.Id)); }
                int before = set.Count, n = 0;
                foreach (var d in Parse(text)) { n++; set.Add(d); }
                res.PerSource[id] = n;
                Log.W("  {0}: {1:N0} entries ({2:N0} new)", src.Name, n, set.Count - before);
            }
            foreach (var c in Lines(cfg.Custom)) { var d = Clean(c); if (d != null) set.Add(d); }

            var allow = Safety.Concat(cfg.GamingSafe ? Gaming : new string[0]).Concat(Lines(cfg.Allow).Select(Clean).Where(x => x != null)).ToList();
            var custom = new HashSet<string>(Lines(cfg.Custom).Select(Clean).Where(x => x != null));
            foreach (var d in set)
            {
                if (!custom.Contains(d) && allow.Any(a => Suffix(d, a))) { res.Allowed++; continue; }
                bool ms = IsMicrosoft(d);
                if (ms && cfg.SkipMicrosoft && !custom.Contains(d)) { res.Allowed++; continue; }
                if (ms) res.Microsoft++;
                res.Domains.Add(d);
            }
            res.Domains.Sort(StringComparer.Ordinal);
            res.Total = res.Domains.Count;
            return res;
        }

        // Which sources (and the user's own lists) block this domain right now, from the cached copies.
        public static string Explain(HostsConfig cfg, string input)
        {
            string d = Clean(input ?? "");
            if (d == null) return "\"" + input + "\" isn't a valid domain name.";
            var hits = new List<string>();
            foreach (var id in cfg.Sources)
            {
                var src = HostSources.Get(id); if (src == null) continue;
                if (src.Builtin == null) Secure.DropUntrusted(CacheFile(id), "a hosts source cache");   // 3.2.1: never write planted entries into hosts
                string text = src.Builtin != null ? string.Join("\n", src.Builtin) : (File.Exists(CacheFile(id)) ? File.ReadAllText(CacheFile(id)) : null);
                if (text != null && Parse(text).Contains(d)) hits.Add(src.Name);
            }
            if (Lines(cfg.Custom).Select(Clean).Contains(d)) hits.Add("your own block list");
            string safe = Safety.FirstOrDefault(a => Suffix(d, a));
            string game = cfg.GamingSafe ? Gaming.FirstOrDefault(a => Suffix(d, a)) : null;
            string mine = Lines(cfg.Allow).Select(Clean).FirstOrDefault(a => a != null && Suffix(d, a));
            bool active = HostsModule.Current().Contains(d);
            var sb = new StringBuilder(d + ": ");
            sb.Append(active ? "BLOCKED in your hosts file now." : "not blocked by your hosts file.");
            if (hits.Count > 0) sb.Append(" Listed in: " + string.Join(", ", hits.ToArray()) + ".");
            else sb.Append(" Not in a selected list (exact match). A parent domain can still be in a list.");
            if (safe != null) sb.Append(" Always allowed by the safety list (" + safe + ").");
            else if (game != null) sb.Append(" Allowed by the game launcher safe list (" + game + ").");
            else if (mine != null) sb.Append(" Allowed by your allowlist (" + mine + ").");
            else if (hits.Count > 0 && cfg.SkipMicrosoft && IsMicrosoft(d)) sb.Append(" Skipped because 'leave out Microsoft domains' is on.");
            return sb.ToString();
        }

        // Build + write. Returns the result (null if nothing selected).
        public static BuildResult Update(HostsConfig cfg, bool allowNetwork, Action<string> progress)
        {
            Log.W("Hosts blocker: building from {0} source(s)…", cfg.Sources.Count);
            var r = Build(cfg, allowNetwork, progress);
            if (r.Total == 0 && cfg.Sources.Count + Lines(cfg.Custom).Count() == 0) { Log.W("Nothing selected. Pick at least one list."); return null; }
            var head = new List<string>
            {
                "# Built " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " by ArchTweaker " + Program.Version + ": " + r.Total.ToString("N0") + " domains",
                "# Sources: " + string.Join(", ", cfg.Sources.Select(id => { var s = HostSources.Get(id); return s == null ? id : s.Name; }).ToArray()),
                "# Edit the lists in ArchTweaker > Hosts blocker. Hand edits inside this block are replaced on the next update.",
            };
            HostsModule.ApplyBlock(r.Domains, head, cfg.Compact);
            cfg.LastUpdate = DateTime.Now; cfg.Save();
            Log.W("Hosts blocker: {0:N0} domains written{1}. {2:N0} skipped by allowlists.{3}", r.Total, cfg.Compact ? " (9 per line)" : "", r.Allowed,
                r.Microsoft > 0 ? " " + r.Microsoft + " Microsoft domain(s) included, so Defender may report 'HostsFileHijack': choose 'Allow on device', because 'Remove' resets the whole hosts file." : "");
            return r;
        }

        // ---- weekly auto-update (scheduled task, runs the installed copy with /hostsupdate)
        public const string TASK = @"ArchTweaker\HostsUpdate";
        public static string InstalledExe { get { return Path.Combine(Paths.Root, @"bin\ArchTweaker.exe"); } }
        public static bool TaskExists() { return Shell.Run("schtasks.exe", "/Query /TN \"" + TASK + "\"", true).IndexOf("HostsUpdate", StringComparison.OrdinalIgnoreCase) >= 0; }
        public static void InstallTask()
        {
            if (Secure.InstallExe() == null) { Log.Say("Weekly hosts update not scheduled: ArchTweaker couldn't secure the program copy for SYSTEM tasks (see log)."); return; }   // 3.2.1
            string o = Shell.Run("schtasks.exe", "/Create /F /TN \"" + TASK + "\" /SC WEEKLY /D SUN /ST 12:00 /RL HIGHEST /RU SYSTEM /TR \"\\\"" + InstalledExe + "\\\" /hostsupdate /silent\"", true).Trim();
            Log.W("Weekly hosts update task: {0}", o.Length > 0 ? o : "created");
        }
        public static void RemoveTask() { if (TaskExists()) Log.W("Weekly hosts update task removed: {0}", Shell.Run("schtasks.exe", "/Delete /F /TN \"" + TASK + "\"", true).Trim()); }
    }
}
