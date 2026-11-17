// ArchTweaker: App settings (3.30): changes settings inside Discord, Firefox and Spotify config files.
// Every file is backed up before its first change; "Put back the originals" restores those copies.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ArchTweaker
{
    public class AppSetting
    {
        public string Id, App, Title, Desc;
        public string[] Processes;
        public Func<List<string>> Files;          // the config files this setting edits (only those that exist)
        public Func<string, string> Apply;       // file text → new text
        public Func<string, bool> IsOn;
        public bool Default;                     // ticked when the dialog opens
    }

    public static class AppSettings
    {
        static string AppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        static string LocalAppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        // ---- text helpers (pure, tested)
        // Sets a top-level "key": value in a flat JSON object; adds the key when it is missing.
        public static string SetJson(string json, string key, string literal)
        {
            json = string.IsNullOrWhiteSpace(json) ? "{}" : json;
            var rx = new Regex("(\"" + Regex.Escape(key) + "\"\\s*:\\s*)(true|false|null|-?[0-9.]+|\"(?:[^\"\\\\]|\\\\.)*\")");
            if (rx.IsMatch(json)) return rx.Replace(json, m => m.Groups[1].Value + literal, 1);
            int b = json.IndexOf('{');
            if (b < 0) return json;
            bool empty = json.Substring(b + 1).Trim().StartsWith("}", StringComparison.Ordinal);
            return json.Substring(0, b + 1) + "\n  \"" + key + "\": " + literal + (empty ? "\n" : ",") + json.Substring(b + 1);
        }
        public static bool JsonIs(string json, string key, string literal)
        {
            var m = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*([^,}\\s]+)");
            return m.Success && m.Groups[1].Value == literal;
        }
        // key=value lines (Spotify prefs)
        public static string SetLine(string text, string key, string value)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();
            int i = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
            if (i >= 0) lines[i] = key + "=" + value;
            else if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.Insert(lines.Count - 1, key + "=" + value);
            else lines.Add(key + "=" + value);
            return string.Join("\n", lines.ToArray());
        }
        public static bool LineIs(string text, string key, string value)
        {
            return (text ?? "").Replace("\r\n", "\n").Split('\n').Any(l => l == key + "=" + value);
        }
        // a named block in a Firefox user.js; replaced as a whole
        public static string SetBlock(string text, string id, IEnumerable<string> prefLines)
        {
            string b = "// ArchTweaker " + id + " begin", e = "// ArchTweaker " + id + " end";
            string t = RemoveBlock(text, id).TrimEnd();
            var sb = new StringBuilder(t);
            if (t.Length > 0) sb.Append("\n");
            sb.Append(b).Append("\n");
            foreach (var l in prefLines) sb.Append(l).Append("\n");
            sb.Append(e).Append("\n");
            return sb.ToString();
        }
        public static string RemoveBlock(string text, string id)
        {
            string b = "// ArchTweaker " + id + " begin", e = "// ArchTweaker " + id + " end";
            text = text ?? "";
            int i = text.IndexOf(b, StringComparison.Ordinal); if (i < 0) return text;
            int j = text.IndexOf(e, i, StringComparison.Ordinal); if (j < 0) return text;
            j += e.Length; while (j < text.Length && (text[j] == '\r' || text[j] == '\n')) j++;
            return text.Substring(0, i) + text.Substring(j);
        }
        public static bool HasBlock(string text, string id) { return (text ?? "").Contains("// ArchTweaker " + id + " begin"); }
        static string Pref(string name, string literal) { return "user_pref(\"" + name + "\", " + literal + ");"; }

        // ---- where the files are
        static List<string> Existing(params string[] paths) { return paths.Where(File.Exists).ToList(); }
        static List<string> DiscordFiles()
        {
            return Existing(Path.Combine(AppData, @"discord\settings.json"), Path.Combine(AppData, @"discordptb\settings.json"), Path.Combine(AppData, @"discordcanary\settings.json"));
        }
        // user.js lives next to prefs.js; it is created when missing, in every profile that has a prefs.js
        static List<string> FirefoxFiles()
        {
            var l = new List<string>();
            try
            {
                string root = Path.Combine(AppData, @"Mozilla\Firefox\Profiles");
                if (Directory.Exists(root))
                    foreach (var d in Directory.GetDirectories(root))
                        if (File.Exists(Path.Combine(d, "prefs.js"))) l.Add(Path.Combine(d, "user.js"));
            }
            catch { }
            return l;
        }
        static List<string> SpotifyFiles()
        {
            var l = Existing(Path.Combine(AppData, @"Spotify\prefs"));
            try
            {
                string pk = Path.Combine(LocalAppData, "Packages");
                if (Directory.Exists(pk))
                    foreach (var d in Directory.GetDirectories(pk, "SpotifyAB.SpotifyMusic_*"))
                    {
                        string p = Path.Combine(d, @"LocalState\Spotify\prefs");
                        if (File.Exists(p)) l.Add(p);
                    }
            }
            catch { }
            return l;
        }

        static readonly string[] FfTelemetry =
        {
            Pref("toolkit.telemetry.enabled", "false"), Pref("toolkit.telemetry.unified", "false"), Pref("datareporting.healthreport.uploadEnabled", "false"),
            Pref("datareporting.policy.dataSubmissionEnabled", "false"), Pref("app.shield.optoutstudies.enabled", "false"), Pref("browser.discovery.enabled", "false"),
        };

        public static readonly List<AppSetting> All = new List<AppSetting>
        {
            new AppSetting { Id = "discord_startup", App = "Discord", Processes = new[] { "Discord", "DiscordPTB", "DiscordCanary" }, Files = DiscordFiles, Default = true,
                Title = "Discord: don't start with Windows", Desc = "Discord no longer opens at sign-in. Start it yourself when you need it.",
                Apply = t => SetJson(t, "OPEN_ON_STARTUP", "false"), IsOn = t => JsonIs(t, "OPEN_ON_STARTUP", "false") },
            new AppSetting { Id = "discord_hwaccel", App = "Discord", Processes = new[] { "Discord", "DiscordPTB", "DiscordCanary" }, Files = DiscordFiles,
                Title = "Discord: hardware acceleration off", Desc = "Discord draws with the CPU, so it doesn't compete with the game for the GPU. Streams and video calls can use more CPU.",
                Apply = t => SetJson(t, "enableHardwareAcceleration", "false"), IsOn = t => JsonIs(t, "enableHardwareAcceleration", "false") },
            new AppSetting { Id = "ff_telemetry", App = "Firefox", Processes = new[] { "firefox" }, Files = FirefoxFiles, Default = true,
                Title = "Firefox: telemetry and studies off", Desc = "Firefox stops sending usage data and joining Mozilla studies. Written to user.js, so Firefox keeps it.",
                Apply = t => SetBlock(t, "ff_telemetry", FfTelemetry), IsOn = t => HasBlock(t, "ff_telemetry") },
            new AppSetting { Id = "ff_hwaccel", App = "Firefox", Processes = new[] { "firefox" }, Files = FirefoxFiles,
                Title = "Firefox: hardware acceleration off", Desc = "Firefox draws pages with the CPU, so it doesn't compete with a game for the GPU. Video and busy pages use more CPU.",
                Apply = t => SetBlock(t, "ff_hwaccel", new[] { Pref("layers.acceleration.disabled", "true") }), IsOn = t => HasBlock(t, "ff_hwaccel") },
            new AppSetting { Id = "spotify_startup", App = "Spotify", Processes = new[] { "Spotify" }, Files = SpotifyFiles, Default = true,
                Title = "Spotify: don't start with Windows", Desc = "Spotify no longer opens at sign-in.",
                Apply = t => SetLine(t, "app.autostart-mode", "\"off\""), IsOn = t => LineIs(t, "app.autostart-mode", "\"off\"") },
            new AppSetting { Id = "spotify_hwaccel", App = "Spotify", Processes = new[] { "Spotify" }, Files = SpotifyFiles,
                Title = "Spotify: hardware acceleration off", Desc = "Spotify draws with the CPU, so it doesn't compete with the game for the GPU.",
                Apply = t => SetLine(t, "ui.hardware_acceleration", "false"), IsOn = t => LineIs(t, "ui.hardware_acceleration", "false") },
        };

        public static bool Installed(AppSetting s) { return s.Files().Count > 0; }
        public static bool Running(AppSetting s) { return s.Processes.Any(p => { try { return Process.GetProcessesByName(p).Length > 0; } catch { return false; } }); }
        public static bool IsOn(AppSetting s)
        {
            var f = s.Files();
            return f.Count > 0 && f.All(p => s.IsOn(File.Exists(p) ? File.ReadAllText(p) : ""));
        }

        // ---- backups: ProgramData\ArchTweaker\backup\apps, index.tsv = original path, copy (empty: the file did not exist), time
        static string Dir { get { string d = Path.Combine(Paths.Backups, "apps"); Directory.CreateDirectory(d); return d; } }
        static string Index { get { return Path.Combine(Dir, "index.tsv"); } }
        // ArchTweaker runs elevated: never follow a link a program could plant in the user profile
        static bool Plain(string path)
        {
            try
            {
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
                string d = Path.GetDirectoryName(path);
                return Directory.Exists(d) && (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0;
            }
            catch { return false; }
        }
        static bool OurCopy(string copy)
        {
            try
            {
                string full = Path.GetFullPath(copy), dir = Path.GetFullPath(Dir).TrimEnd('\\') + "\\";
                return File.Exists(full) && full.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && full.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) && Secure.TrustedData(full, "an app settings backup");
            }
            catch { return false; }
        }
        static void Backup(string path)
        {
            if (File.Exists(Index) && File.ReadAllLines(Index).Any(l => l.Split('\t')[0].Equals(path, StringComparison.OrdinalIgnoreCase))) return;   // the first copy is the original
            string copy = "";
            if (File.Exists(path))
            {
                copy = Path.Combine(Dir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bak");
                File.Copy(path, copy);
            }
            File.AppendAllText(Index, path + "\t" + copy + "\t" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
        }

        // returns one line per setting
        public static List<string> ApplyAll(IEnumerable<AppSetting> picks)
        {
            var res = new List<string>();
            foreach (var s in picks)
            {
                if (Running(s)) { res.Add(L.F("{0}: skipped, {1} is running. Close it and try again.", L.T(s.Title), s.App)); continue; }
                int n = 0, failed = 0;
                foreach (var f in s.Files())
                {
                    try
                    {
                        if (!Plain(f)) { failed++; res.Add(L.F("{0}: skipped {1}, it is a link.", L.T(s.Title), f)); continue; }
                        string t = File.Exists(f) ? File.ReadAllText(f) : "";
                        if (s.IsOn(t)) continue;
                        Backup(f);
                        File.WriteAllText(f, s.Apply(t), new UTF8Encoding(false));
                        n++;
                    }
                    catch (Exception ex) { failed++; res.Add(L.F("{0}: {1}", L.T(s.Title), ex.Message)); }
                }
                Log.W("App settings: {0} → {1} file(s), {2} failed", s.Id, n, failed);
                if (failed == 0) res.Add(L.F("{0}: done", L.T(s.Title)));
            }
            return res;
        }

        public static bool HasBackups { get { return File.Exists(Index) && File.ReadAllLines(Index).Any(l => l.Trim().Length > 0); } }
        // puts back each file as it was before ArchTweaker first changed it; a file that did not exist is deleted
        public static string RestoreAll()
        {
            if (!HasBackups) return L.T("There is nothing to put back.");
            var running = All.Where(Running).Select(s => s.App).Distinct().ToList();
            if (running.Count > 0) return L.F("Close {0} first. Nothing was changed.", string.Join(", ", running.ToArray()));
            int ok = 0, bad = 0; var keep = new List<string>();
            foreach (var line in File.ReadAllLines(Index).Where(l => l.Trim().Length > 0))
            {
                var p = line.Split('\t');
                try
                {
                    if (!Plain(p[0])) { bad++; keep.Add(line); continue; }
                    if (p.Length > 1 && p[1].Length > 0)
                    {
                        if (!OurCopy(p[1])) { bad++; keep.Add(line); continue; }
                        File.Copy(p[1], p[0], true); File.Delete(p[1]);
                    }
                    else if (File.Exists(p[0])) File.Delete(p[0]);
                    ok++;
                }
                catch (Exception ex) { bad++; keep.Add(line); Log.W("App settings restore {0}: {1}", p[0], ex.Message); }
            }
            File.WriteAllLines(Index, keep.ToArray());
            Log.W("App settings: put back {0} file(s), {1} failed.", ok, bad);
            return bad == 0 ? L.F("Put back {0} file(s).", ok) : L.F("Put back {0} file(s). ArchTweaker could not put back {1}: details are in the log.", ok, bad);
        }
    }

}
