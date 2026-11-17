// ArchTweaker 1.9: Winapp2.ini-powered deep cleanup.
// Winapp2.ini is CC-BY-SA-4.0, maintained at github.com/MoscaDotTo/Winapp2 (not bundled; downloaded + cached at runtime).
// Only FileKey (file) rules are used here — registry keys are left to the separate, conservative registry scanner.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ArchTweaker
{
    public class WinappFileKey
    {
        public string Folder;              // raw, with env vars + wildcards
        public string[] Masks = { "*" };
        public bool Recurse, RemoveSelf;
    }

    public class WinappExclude      // ExcludeKeyN=FILE|folder|masks  (or PATH|folder|)
    {
        public string Folder;       // raw, with env vars
        public string[] Masks = { "*" };
    }

    public class WinappEntry
    {
        public string Name, Section, Warning;
        public List<WinappFileKey> FileKeys = new List<WinappFileKey>();
        public List<WinappExclude> Excludes = new List<WinappExclude>();
        public List<string> DetectFiles = new List<string>();   // expanded paths (file or dir); any exists => detected
        public List<string> DetectRegs = new List<string>();    // HKxx\... ; any exists => detected
        public int RegKeyCount;            // how many RegKey rules we skipped (shown for info)
        public bool Extended;              // name ended with * (non-standard rule)
        public long Size = -1;             // measured lazily
        public bool Detected;
    }

    public static class Winapp
    {
        public const string Url = "https://raw.githubusercontent.com/MoscaDotTo/Winapp2/master/Winapp2.ini";
        public const string UrlMirror = "https://cdn.jsdelivr.net/gh/MoscaDotTo/Winapp2@master/Winapp2.ini";
        static string Dir { get { string p = Path.Combine(Paths.Root, "winapp2"); Directory.CreateDirectory(p); return p; } }
        public static string CacheFile { get { return Path.Combine(Dir, "Winapp2.ini"); } }

        // LangSecRef numbers → human category (the common ones; anything else falls back).
        static readonly Dictionary<string, string> Lang = new Dictionary<string, string>
        {
            {"3021","Applications"},{"3022","Internet"},{"3023","Multimedia"},{"3024","Utilities"},
            {"3025","Windows"},{"3026","Firefox/Mozilla"},{"3027","Opera"},{"3028","Safari"},
            {"3029","Google Chrome"},{"3030","Thunderbird"},{"3031","Games"},{"3032","Edge"},
            {"3033","Brave"},{"3034","Vivaldi"},
        };

        // ---------- env expansion ----------
        static string Expand(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            p = ReplaceCI(p, "%CommonAppData%", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            p = ReplaceCI(p, "%LocalLowAppData%", Path.Combine(up, @"AppData\LocalLow"));
            p = ReplaceCI(p, "%Documents%", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            p = ReplaceCI(p, "%Public%", Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public");
            p = ReplaceCI(p, "%LocalAppData%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            p = ReplaceCI(p, "%AppData%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            return Environment.ExpandEnvironmentVariables(p);
        }
        static string ReplaceCI(string s, string tok, string val) { return Regex.Replace(s, Regex.Escape(tok), (val ?? "").Replace("$", "$$"), RegexOptions.IgnoreCase); }

        // Resolve a path that may contain * wildcards in any segment → actual existing directories.
        public static IEnumerable<string> ResolveDirs(string pathWithGlobs)
        {
            if (string.IsNullOrEmpty(pathWithGlobs) || pathWithGlobs.IndexOf("..", StringComparison.Ordinal) >= 0) yield break;  // never allow traversal
            string p = Expand(pathWithGlobs).TrimEnd('\\');
            if (p.IndexOf("..", StringComparison.Ordinal) >= 0) yield break;
            var parts = p.Split('\\');
            if (parts.Length == 0) yield break;
            IEnumerable<string> cur = new[] { parts[0].Length == 0 ? "\\" : parts[0] + "\\" };  // drive root
            for (int i = 1; i < parts.Length; i++)
            {
                string seg = parts[i];
                if (seg == "." || seg == "..") yield break;
                var next = new List<string>();
                bool glob = seg.IndexOf('*') >= 0 || seg.IndexOf('?') >= 0;
                foreach (var c in cur)
                {
                    if (glob) { try { next.AddRange(Directory.GetDirectories(c, seg).Where(x => !IsReparse(x))); } catch { } }   // 3.2.1: a junction matched by * is skipped too
                    else { string j = Path.Combine(c, seg); if (Directory.Exists(j) && !IsReparse(j)) next.Add(j); }
                }
                cur = next;
                if (!cur.Any()) yield break;
            }
            foreach (var d in cur)
            {
                string full; try { full = Path.GetFullPath(d); } catch { continue; }
                if (AllowedTarget(full) && !IsReparse(full)) yield return full;   // canonical + allowlisted + not a junction
            }
        }

        static bool IsReparse(string dir)
        {
            try { return (new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0; } catch { return true; }
        }

        // ---------- parse ----------
        public static List<WinappEntry> Parse(string ini)
        {
            var list = new List<WinappEntry>();
            WinappEntry e = null;
            foreach (var raw in ini.Split('\n'))
            {
                string line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0 || line[0] == ';') continue;
                if (line[0] == '[')
                {
                    if (e != null) list.Add(e);
                    string nm = line.Trim('[', ']').Trim();
                    e = new WinappEntry { Extended = nm.EndsWith("*"), Name = nm.TrimEnd(' ', '*') };
                    continue;
                }
                if (e == null) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                string kl = key.ToLowerInvariant();
                if (kl == "langsecref" || kl == "section") { if (e.Section == null) e.Section = Lang.ContainsKey(val) ? Lang[val] : (kl == "section" ? val : "Applications"); }
                else if (kl == "warning") e.Warning = val;
                else if (kl.StartsWith("detectfile")) e.DetectFiles.Add(val);
                else if (kl.StartsWith("detectos") || kl == "specialdetect" || kl == "detectos") { /* OS/special gate — not a registry key; ignore for our detection */ }
                else if (kl.StartsWith("detect")) e.DetectRegs.Add(val);
                else if (kl.StartsWith("excludekey")) { var ex = ParseExclude(val); if (ex != null) e.Excludes.Add(ex); }
                else if (kl.StartsWith("regkey")) e.RegKeyCount++;
                else if (kl.StartsWith("filekey"))
                {
                    var fk = ParseFileKey(val);
                    if (fk != null) e.FileKeys.Add(fk);
                }
            }
            if (e != null) list.Add(e);
            return list.Where(x => x.FileKeys.Count > 0).ToList();   // we only act on file rules
        }

        static WinappFileKey ParseFileKey(string val)
        {
            var parts = val.Split('|');
            if (parts.Length < 1 || parts[0].Trim().Length == 0) return null;
            string folder = parts[0].Trim();
            if (folder.IndexOf("..", StringComparison.Ordinal) >= 0) return null;   // reject traversal rules outright
            var fk = new WinappFileKey { Folder = folder };
            if (parts.Length >= 2 && parts[1].Trim().Length > 0)
                fk.Masks = parts[1].Split(';').Select(m => m.Trim()).Where(m => m.Length > 0 && m.IndexOf("..", StringComparison.Ordinal) < 0).ToArray();
            if (fk.Masks.Length == 0) fk.Masks = new[] { "*" };
            for (int i = 2; i < parts.Length; i++)
            {
                string f = parts[i].Trim().ToUpperInvariant();
                if (f == "RECURSE") fk.Recurse = true;
                else if (f == "REMOVESELF") { fk.Recurse = true; fk.RemoveSelf = true; }
            }
            return fk;
        }

        // ExcludeKeyN=FILE|folder|masks   or   PATH|folder|   (REG excludes are irrelevant — we never touch the registry)
        static WinappExclude ParseExclude(string val)
        {
            var parts = val.Split('|');
            if (parts.Length < 2) return null;
            string type = parts[0].Trim().ToUpperInvariant();
            if (type != "FILE" && type != "PATH") return null;
            string folder = parts[1].Trim().TrimEnd('\\');
            if (folder.Length == 0 || folder.IndexOf("..", StringComparison.Ordinal) >= 0) return null;
            var ex = new WinappExclude { Folder = folder };
            if (parts.Length >= 3 && parts[2].Trim().Length > 0)
                ex.Masks = parts[2].Split(';').Select(m => m.Trim()).Where(m => m.Length > 0).ToArray();
            if (ex.Masks.Length == 0) ex.Masks = new[] { "*" };
            return ex;
        }

        // ---------- detect ----------
        static readonly Regex HiveRx = new Regex(@"^(HKCU|HKLM|HKCR|HKU|HKEY_[A-Z_]+)\\(.*)$", RegexOptions.IgnoreCase);
        static bool RegExists(string spec)
        {
            var m = HiveRx.Match(spec.Trim()); if (!m.Success) return false;
            Hive h; string hs = m.Groups[1].Value.ToUpperInvariant();
            if (hs.StartsWith("HKLM") || hs.Contains("LOCAL_MACHINE")) h = Hive.HKLM;
            else if (hs.StartsWith("HKCU") || hs.Contains("CURRENT_USER")) h = Hive.HKCU;
            else if (hs.StartsWith("HKCR") || hs.Contains("CLASSES")) h = Hive.HKCR;
            else h = Hive.HKU;
            return Reg.KeyExists(h, m.Groups[2].Value.Trim());
        }
        public static bool Detect(WinappEntry e)
        {
            if (e.DetectFiles.Count == 0 && e.DetectRegs.Count == 0) return true;   // unconditional
            if (e.DetectFiles.Any(f => { try { string p = Expand(f); return File.Exists(p) || Directory.Exists(p) || ResolveDirs(f).Any(); } catch { return false; } })) return true;
            if (e.DetectRegs.Any(RegExists)) return true;
            return false;
        }

        // ---------- measure / clean ----------
        // Allowlist model: a target is only cleanable if its canonical path sits UNDER one of a small set of
        // known cache roots, is not one of those roots itself, is not the Windows tree, and is not a user
        // documents folder. This neutralizes a malicious/compromised rule even if detection passed.
        static readonly string[] AllowedBases;      // lower-cased, no trailing slash
        static readonly string[] DenySubtrees;      // Windows + user-document shell folders
        static Winapp()
        {
            var bases = new List<string>();
            Action<Environment.SpecialFolder> addBase = sf => { try { string p = Environment.GetFolderPath(sf).TrimEnd('\\').ToLowerInvariant(); if (p.Length > 3) bases.Add(p); } catch { } };
            addBase(Environment.SpecialFolder.LocalApplicationData);
            addBase(Environment.SpecialFolder.ApplicationData);
            addBase(Environment.SpecialFolder.CommonApplicationData);   // ProgramData
            addBase(Environment.SpecialFolder.ProgramFiles);
            addBase(Environment.SpecialFolder.ProgramFilesX86);
            string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            try { bases.Add(Path.Combine(up, @"AppData\LocalLow").ToLowerInvariant()); } catch { }
            try { string t = Path.GetTempPath().TrimEnd('\\', '/').ToLowerInvariant(); if (t.Length > 3) bases.Add(t); } catch { }
            try { string pub = Environment.GetEnvironmentVariable("PUBLIC"); if (!string.IsNullOrEmpty(pub)) bases.Add(pub.TrimEnd('\\').ToLowerInvariant()); } catch { }
            AllowedBases = bases.Distinct().ToArray();

            var deny = new List<string>();
            Action<Environment.SpecialFolder> addDeny = sf => { try { string p = Environment.GetFolderPath(sf).TrimEnd('\\').ToLowerInvariant(); if (p.Length > 3) deny.Add(p); } catch { } };
            addDeny(Environment.SpecialFolder.Windows);
            addDeny(Environment.SpecialFolder.System);
            addDeny(Environment.SpecialFolder.MyDocuments);
            addDeny(Environment.SpecialFolder.MyPictures);
            addDeny(Environment.SpecialFolder.MyVideos);
            addDeny(Environment.SpecialFolder.MyMusic);
            addDeny(Environment.SpecialFolder.DesktopDirectory);
            addDeny(Environment.SpecialFolder.Favorites);
            try { deny.Add(Path.Combine(up, "Downloads").ToLowerInvariant()); } catch { }
            // 3.2.1: ArchTweaker's own data (undo journal, backups) and Windows components under Program Files
            try { deny.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ArchTweaker").ToLowerInvariant()); } catch { }
            foreach (var pf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                try
                {
                    string root = Environment.GetFolderPath(pf);
                    if (root.Length > 3) foreach (var w in Directory.GetDirectories(root, "Windows*")) deny.Add(w.TrimEnd('\\').ToLowerInvariant());
                    if (root.Length > 3) deny.Add(Path.Combine(root, "Microsoft Update Health Tools").ToLowerInvariant());
                }
                catch { }
            DenySubtrees = deny.Distinct().ToArray();
        }

        static bool Under(string path, string root) { return path == root || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal); }

        static bool AllowedTarget(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            if (!Path.IsPathRooted(dir)) return false;
            string d = dir.TrimEnd('\\').ToLowerInvariant();
            if (d.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            if (Regex.IsMatch(d, @"^[a-z]:\\?$")) return false;                  // drive root
            foreach (var deny in DenySubtrees) if (Under(d, deny)) return false; // Windows + user documents
            if (AllowedBases.Contains(d)) return false;                          // 3.38.2: never a base itself, even when it sits under another base (Temp is under LocalAppData)
            foreach (var b in AllowedBases)
                if (d != b && Under(d, b)) return true;                          // under an allowed base, not the base itself
            return false;
        }

        // Convert a Winapp2 mask (* and ?) to an anchored regex matched against the FILE NAME only,
        // so "*.log" does NOT also match ".log1"/".logs" the way Directory.GetFiles would.
        static Regex MaskRx(string mask)
        {
            if (mask == "*" || mask == "*.*") return null;   // null = match everything
            string rx = "^" + Regex.Escape(mask).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        // Manual, reparse-safe enumeration. Yields files in `dir` (and subdirs if recurse) whose name
        // matches any mask and is not covered by an exclude.
        static IEnumerable<string> MatchFiles(string dir, string[] masks, bool recurse, List<WinappExclude> excludes)
        {
            var rxs = masks.Select(MaskRx).ToList();
            var exc = BuildExcludes(excludes);
            return Walk(dir, rxs, recurse, exc);
        }

        static IEnumerable<string> Walk(string dir, List<Regex> rxs, bool recurse, List<KeyValuePair<string, List<Regex>>> exc)
        {
            string[] files = null;
            try { files = Directory.GetFiles(dir); } catch { }
            if (files != null)
                foreach (var f in files)
                {
                    string name = Path.GetFileName(f);
                    if (!rxs.Any(r => r == null || r.IsMatch(name))) continue;
                    if (SafeLists.CleanBlocked(f)) continue;          // browser extensions, WinSxS, app-required temp
                    if (Excluded(f, name, exc)) continue;
                    yield return f;
                }
            if (!recurse) yield break;
            string[] subs = null;
            try { subs = Directory.GetDirectories(dir); } catch { }
            if (subs == null) yield break;
            foreach (var sd in subs)
            {
                if (IsReparse(sd)) continue;                 // never descend into junctions/symlinks
                if (SafeLists.CleanBlocked(sd + "\\")) continue;
                foreach (var f in Walk(sd, rxs, recurse, exc)) yield return f;
            }
        }

        static List<KeyValuePair<string, List<Regex>>> BuildExcludes(List<WinappExclude> excludes)
        {
            var outl = new List<KeyValuePair<string, List<Regex>>>();
            if (excludes != null)
                foreach (var ex in excludes)
                    foreach (var dir in ResolveDirs(ex.Folder))
                        outl.Add(new KeyValuePair<string, List<Regex>>(dir.TrimEnd('\\').ToLowerInvariant(), ex.Masks.Select(MaskRx).ToList()));
            return outl;
        }
        static bool Excluded(string fullPath, string name, List<KeyValuePair<string, List<Regex>>> exc)
        {
            if (exc.Count == 0) return false;
            string parent = (Path.GetDirectoryName(fullPath) ?? "").TrimEnd('\\').ToLowerInvariant();
            foreach (var e in exc)
                if (parent == e.Key || parent.StartsWith(e.Key + "\\", StringComparison.Ordinal))
                    if (e.Value.Any(r => r == null || r.IsMatch(name))) return true;
            return false;
        }

        public static long Measure(WinappEntry e)
        {
            long sum = 0;
            foreach (var fk in e.FileKeys)
                foreach (var dir in ResolveDirs(fk.Folder))   // already canonical + allowlisted + non-reparse
                    foreach (var f in MatchFiles(dir, fk.Masks, fk.Recurse, e.Excludes)) { try { sum += new FileInfo(f).Length; } catch { } }
            return e.Size = sum;
        }

        // Returns bytes freed; failures (locked/denied files) are counted in `skipped`.
        public static long Clean(WinappEntry e, out int skipped)
        {
            long freed = 0; int sk = 0;
            foreach (var fk in e.FileKeys)
                foreach (var dir in ResolveDirs(fk.Folder))
                {
                    // 3.2.1: the real folder; a delete whose file opens anywhere else (junction swapped in meanwhile) is refused
                    string real = Secure.RealDir(dir);
                    if (real == null || !AllowedTarget(real)) continue;
                    foreach (var f in MatchFiles(dir, fk.Masks, fk.Recurse, e.Excludes).ToList())
                    {
                        try { long len = new FileInfo(f).Length; Secure.DeleteUnder(f, real); freed += len; }
                        catch { sk++; }
                    }
                    if (fk.RemoveSelf && AllowedTarget(dir) && !IsReparse(dir))
                    {
                        try { foreach (var sd in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) try { if (!IsReparse(sd)) Directory.Delete(sd, false); } catch { } } catch { }
                        try { Directory.Delete(dir, false); } catch { }
                    }
                }
            skipped = sk;
            return freed;
        }

        // ---------- download / cache ----------
        const long MaxBytes = 16L * 1024 * 1024;   // sanity cap: the file is ~2MB; refuse a hostile oversized body

        public static string Fetch(bool allowNetwork, out bool fromCache, out string error)
        {
            fromCache = false; error = null;
            if (allowNetwork)
                foreach (var u in new[] { Url, UrlMirror })
                    try
                    {
                        var uri = new Uri(u);
                        if (uri.Scheme != Uri.UriSchemeHttps) { error = "refused non-HTTPS source " + u; continue; }
                        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)768; } catch { }
                        var req = (HttpWebRequest)WebRequest.Create(uri);
                        req.UserAgent = "ArchTweaker/" + Program.Version; req.Timeout = 45000; req.ReadWriteTimeout = 60000;
                        req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                        string t;
                        using (var resp = (HttpWebResponse)req.GetResponse())
                        {
                            if (resp.ContentLength > MaxBytes) { error = "download too large from " + u; continue; }
                            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) t = r.ReadToEnd();
                        }
                        if (t.Length > MaxBytes) { error = "download too large from " + u; continue; }
                        if (!LooksLikeIni(t)) { error = "unexpected content from " + u; continue; }
                        try { WriteCacheAtomic(t); } catch (Exception wx) { Log.W("Winapp2 cache write failed: {0}", wx.Message); }   // download still usable
                        return t;
                    }
                    catch (Exception ex) { error = ex.Message; }
            Secure.DropUntrusted(CacheFile, "the cached cleaning rules");   // 3.2.1: these rules drive elevated deletes
            if (File.Exists(CacheFile)) { fromCache = true; return File.ReadAllText(CacheFile); }
            return null;
        }

        static bool LooksLikeIni(string t)
        {
            if (t.Length < 64) return false;
            foreach (var line in t.Split('\n')) { string l = line.TrimStart(); if (l.Length > 0 && l[0] == '[') return true; }
            return false;
        }
        static void WriteCacheAtomic(string t)
        {
            string tmp = CacheFile + ".tmp";
            File.WriteAllText(tmp, t, new UTF8Encoding(false));
            if (File.Exists(CacheFile)) File.Replace(tmp, CacheFile, null); else File.Move(tmp, CacheFile);
        }

        public static DateTime CachedAt { get { return File.Exists(CacheFile) ? File.GetLastWriteTime(CacheFile) : DateTime.MinValue; } }

        // Full load: fetch, parse, keep only entries detected on this PC, sorted by category then name.
        public static List<WinappEntry> Load(bool allowNetwork, out string status)
        {
            bool cached; string err;
            string ini = Fetch(allowNetwork, out cached, out err);
            if (ini == null) { status = "Could not download Winapp2.ini and no saved copy: " + err; return new List<WinappEntry>(); }
            var all = Parse(ini);
            foreach (var e in all) e.Detected = Detect(e);
            var det = all.Where(x => x.Detected).OrderBy(x => x.Section ?? "Applications").ThenBy(x => x.Name).ToList();
            status = string.Format("{0:N0} rules in Winapp2.ini ({1}), {2:N0} apply to this PC.{3}",
                all.Count, cached ? "saved copy from " + CachedAt.ToString("yyyy-MM-dd") : "downloaded", det.Count,
                cached ? " Offline — using the cached list." : "");
            return det;
        }
    }
}
