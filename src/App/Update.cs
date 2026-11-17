// ArchTweaker 2.0: self-update. Checks a small JSON manifest over HTTPS, verifies the SHA-256 of the
// downloaded exe, then stages a tiny .bat that swaps the running exe once this process exits.
// The manifest URL defaults to a GitHub Pages / raw location and can be overridden in the registry.
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ArchTweaker
{
    public class UpdateInfo
    {
        public string Version, Url, Sha256, Notes;
        public bool Newer;            // manifest version is newer than this build
        public bool Verified;         // 3.35: the manifest signature matched the public key built into this exe
    }

    public static class Updater
    {
        // Override with HKLM\Software\ArchTweaker\Update\ManifestUrl (admin-only) if you self-host.
        // NOTE: read from HKLM, never HKCU — a non-admin HKCU writer must not be able to redirect an elevated update.
        public const string DefaultManifest = "https://raw.githubusercontent.com/arttnomadd/ArchTweaker/main/latest.json";
        static readonly string[] AllowedHosts = { "raw.githubusercontent.com", "cdn.jsdelivr.net", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" };   // 3.38.1: GitHub now redirects release downloads to release-assets

        public static string ManifestUrl
        {
            get
            {
                object o = Reg.Get(Hive.HKLM, @"Software\ArchTweaker\Update", "ManifestUrl");
                string s = o == null ? null : o.ToString();
                return string.IsNullOrEmpty(s) ? DefaultManifest : s;
            }
        }

        // Only https, and only to a hosting domain we trust.
        static bool UrlOk(string url)
        {
            try { var u = new Uri(url); return u.Scheme == Uri.UriSchemeHttps && AllowedHosts.Any(h => string.Equals(u.Host, h, StringComparison.OrdinalIgnoreCase)); }
            catch { return false; }
        }
        // 3.35 (ADR-0008): the manifest must carry "sig", an RSA-SHA256 (PKCS#1 v1.5) signature made with the maintainer's
        // private key (tools/sign_manifest.py). The hash in the manifest then proves who made the exe, not only that the
        // download arrived intact. The private key never ships; only this public key is in the exe.
        public const string PublicKeyXml = "<RSAKeyValue><Modulus>q/Rz+D2WYJ5EoMi/1sShG82i5CNqKEHssg33cL2CppB1KK03xVo/eAfY0s8PGh3x/WCSEynoPT2MmjZyYjfarDZnveM38wNFYgKP/g5lfUxMRrOspb0aFTBaRvCWwgzY1kh0sVs2kRiJHIFFffeBC79p8Kg7yuiBSUEBQVC3v3MynEs0jh+7E2IDN1nzSQAU38NdqeLAYZe8id64/aAlpEt83Xjo+fmzJ35fHGV0dInZwooDmvk533e0WIIFHqe/pMALC+Rz/jvQPFqKzZPK3kiyneJ0InI6vX8b/fNU1qloes+jxrAYk6fn43J0aaO09iXD0bZFA44qibkxSqVSG37G05KR4g8oe+6pwpl/Qj70zcixneMC0eUKs7On8JzxAi1eX4fHWprjOPLiflV6PpmkORF5pRT1EgXHHX7PaT1iew3eM4f32GBoRbXoDVmjZLAsa1NW3pvKmVqgGgVkJwHy2A6M2Q9j57Zw4qun0mLWWBwzFxawxElEE9XnFBNx</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        // the exact bytes the signature covers: one field per line, in this order, missing fields as empty lines
        public static string SignedText(string json)
        {
            // notes too: the update prompt shows them; serial: replay guard (Fresh)
            string[] keys = { "version", "serial", "url", "sha256", "notes", "overlay", "overlay_sha256" };
            var sb = new StringBuilder("archtweaker-manifest-v1");
            foreach (var k in keys) sb.Append('\n').Append(Field(json, k) ?? "");
            return sb.ToString();
        }
        public static bool SignatureOk(string json) { return SignatureOk(json, PublicKeyXml); }

        // 3.35: replay guard. "serial" (signed) must not be lower than the highest serial this PC has seen, so an old
        // signed manifest can't roll the overlay or the update offer back. The same serial again is fine (a re-check).
        public static bool SerialNewEnough(string json, long highestSeen)
        {
            long s; string v = Field(json, "serial");
            return v != null && long.TryParse(v, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out s) && s > 0 && s >= highestSeen;
        }
        static bool Fresh(string json)
        {
            long seen = 0;
            try { object o = Reg.Get(Hive.HKLM, @"Software\ArchTweaker\Update", "LastSerial"); if (o != null) long.TryParse(o.ToString(), out seen); } catch { }
            if (!SerialNewEnough(json, seen)) { Log.W("Update: the manifest serial is missing or older than one seen before ({0}). Refused.", seen); return false; }
            try { using (var b = Reg.Base(Hive.HKLM)) using (var k = b.CreateSubKey(@"Software\ArchTweaker\Update")) k.SetValue("LastSerial", Field(json, "serial")); }
            catch (Exception ex) { Log.W("Update: could not save the manifest serial: {0}", ex.Message); }
            return true;
        }
        public static bool SignatureOk(string json, string publicKeyXml)
        {
            string sig = Field(json, "sig");
            if (string.IsNullOrEmpty(sig)) return false;
            try
            {
                byte[] s = Convert.FromBase64String(sig.Trim());
                using (var rsa = new RSACryptoServiceProvider(new CspParameters(24)))   // 24 = PROV_RSA_AES, has SHA-256
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(publicKeyXml);
                    return rsa.VerifyData(Encoding.UTF8.GetBytes(SignedText(json)), "SHA256", s);
                }
            }
            catch (Exception ex) { Log.W("Update: signature check failed ({0}: {1})", ex.GetType().Name, ex.Message); return false; }
        }

        static readonly Regex VersionRx = new Regex(@"^\d+(\.\d+){0,3}\z");

        static string Http(string url)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)768; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "ArchTweaker/" + Program.Version; req.Timeout = 30000; req.MaximumAutomaticRedirections = 5;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if (!UrlOk(resp.ResponseUri.AbsoluteUri)) throw new WebException("Redirected to a host that is not allowed: " + resp.ResponseUri.Host);   // 3.2.1
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        // 3.2.1: a download that ends on any other host (after redirects) is refused
        public sealed class PinnedClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                var r = base.GetWebRequest(address); var h = r as HttpWebRequest;
                if (h != null) { h.MaximumAutomaticRedirections = 5; h.UserAgent = "ArchTweaker/" + Program.Version; }
                return r;
            }
            protected override WebResponse GetWebResponse(WebRequest request)
            {
                var resp = base.GetWebResponse(request);
                if (resp != null && !UrlOk(resp.ResponseUri.AbsoluteUri)) { var host = resp.ResponseUri.Host; resp.Close(); throw new WebException("Redirected to a host that is not allowed: " + host); }
                return resp;
            }
        }
        public static bool HostAllowed(string url) { return UrlOk(url); }

        static string Field(string json, string key)
        {
            // 3.35: case-sensitive, the key must start an object member ({ or , before it), the value has no \ or control
            // character, and a key that appears twice reads as missing, so what is signed is exactly what is used
            var ms = Regex.Matches(json ?? "", "(?:^|[{,])\\s*\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"\\\\\\x00-\\x1f]*)\"");
            if (ms.Count != 1) return null;
            if (Regex.Matches(json, "\"" + Regex.Escape(key) + "\"\\s*:").Count != 1) return null;
            return ms[0].Groups[1].Value;
        }

        // "overlay" + "overlay_sha256" fields of the same manifest (both required, https + allowed host only)
        public static bool OverlayInfo(out string url, out string sha)
        {
            url = sha = null;
            if (!UrlOk(ManifestUrl)) return false;
            string j = Http(ManifestUrl);
            if (!SignatureOk(j)) { Log.W("Overlay: the update manifest is not signed by the ArchTweaker key, refused."); return false; }
            if (!Fresh(j)) return false;
            url = Field(j, "overlay"); sha = Field(j, "overlay_sha256");
            return !string.IsNullOrEmpty(url) && UrlOk(url) && !string.IsNullOrEmpty(sha) && Regex.IsMatch(sha, "^[0-9a-fA-F]{64}\\z");
        }

        public static UpdateInfo Check(out string error)
        {
            error = null;
            try
            {
                if (!UrlOk(ManifestUrl)) { error = "Manifest URL is not an allowed https source."; return null; }
                string j = Http(ManifestUrl);
                var u = new UpdateInfo { Version = Field(j, "version"), Url = Field(j, "url"), Sha256 = Field(j, "sha256"), Notes = Field(j, "notes") };
                if (string.IsNullOrEmpty(u.Version) || string.IsNullOrEmpty(u.Url)) { error = "Manifest is missing version/url."; return null; }
                if (!VersionRx.IsMatch(u.Version)) { error = "Manifest version is malformed."; return null; }
                if (!UrlOk(u.Url)) { error = "Download URL is not an allowed https source."; return null; }
                if (string.IsNullOrEmpty(u.Sha256)) { error = "Manifest has no sha256 — refusing to update without an integrity hash."; return null; }
                if (!Regex.IsMatch(u.Sha256.Trim(), "^[0-9a-fA-F]{64}\\z")) { error = "Manifest sha256 is malformed."; return null; }
                if (!SignatureOk(j)) { error = "The update manifest is not signed by the ArchTweaker key. Update refused."; return null; }   // 3.35
                if (!Fresh(j)) { error = "The update manifest is older than one this PC has seen. Update refused."; return null; }
                u.Verified = true;
                u.Newer = Compare(u.Version, Program.Version) > 0;
                return u;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        // returns >0 if a is newer than b
        public static int Compare(string a, string b)
        {
            var pa = Parts(a); var pb = Parts(b);
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
                if (x != y) return x > y ? 1 : -1;
            }
            return 0;
        }
        static int[] Parts(string v)
        {
            var bits = (v ?? "0").Split('.', '-');
            var outl = new System.Collections.Generic.List<int>();
            foreach (var b in bits) { int n; if (int.TryParse(new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(b, char.IsDigit))), out n)) outl.Add(n); }
            return outl.Count == 0 ? new[] { 0 } : outl.ToArray();
        }

        static string Sha(string file)
        {
            using (var s = File.OpenRead(file)) using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        // Staging lives under ProgramData\ArchTweaker\update with random names — not predictable %TEMP% paths.
        static string StageDir { get { string p = Path.Combine(Paths.Root, "update"); Directory.CreateDirectory(p); return p; } }

        // Download the new exe, verify hash, stage the swap. Returns null on success (process should exit), else an error.
        public static string Download(UpdateInfo u)
        {
            try
            {
                if (!u.Verified) return "The update manifest is not signed by the ArchTweaker key. Update refused.";   // 3.35, fail closed
                if (!UrlOk(u.Url)) return "Download URL is not an allowed https source.";
                if (string.IsNullOrEmpty(u.Sha256)) return "No integrity hash — update refused.";          // fail closed
                if (!VersionRx.IsMatch(u.Version)) return "Malformed version — update refused.";
                if (Compare(u.Version, Program.Version) <= 0) return "Manifest is not newer than the installed version.";  // no downgrade/replay

                string rid = Guid.NewGuid().ToString("N");
                string tmp = Path.Combine(StageDir, "at-" + rid + ".exe");                                  // fixed, random name (no manifest text in path)
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)768; } catch { }
                using (var wc = new PinnedClient()) wc.DownloadFile(u.Url, tmp);

                string got = Sha(tmp);
                if (!string.Equals(got, u.Sha256.Replace("-", "").Trim(), StringComparison.OrdinalIgnoreCase))
                { try { File.Delete(tmp); } catch { } return "Download hash mismatch — update aborted (expected " + u.Sha256 + ", got " + got + ")."; }

                return Stage(tmp, rid, got);
            }
            catch (Exception ex) { return ex.Message; }
        }

        // 3.35: the swap script puts both paths into cmd text; these characters would change what cmd runs
        public static bool BatSafe(string path) { return !string.IsNullOrEmpty(path) && path.IndexOfAny("%^&\"!<>|".ToCharArray()) < 0; }

        // Write a .bat that waits for this exe to exit, keeps a .bak, replaces it, and relaunches.
        static string Stage(string newExe, string rid, string sha)
        {
            string cur = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (!BatSafe(cur) || !BatSafe(newExe) || !BatSafe(StageDir)) { try { File.Delete(newExe); } catch { } return "ArchTweaker's folder name has a character the update helper can't use (% ^ & \" ! < > |). Move ArchTweaker.exe to a plain folder and try again."; }   // 3.35
            string bat = Path.Combine(StageDir, "swap-" + rid + ".bat");
            string name = Path.GetFileName(cur);
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("setlocal");
            sb.AppendLine(":wait");
            sb.AppendLine("timeout /t 1 /nobreak >nul");
            sb.AppendLine("tasklist /fi \"imagename eq " + name + "\" | find /i \"" + name + "\" >nul && goto wait");
            // 3.2.1: check the hash again right before the swap: the staged file can't have been changed in between
            sb.AppendLine("certutil -hashfile \"" + newExe + "\" SHA256 | find /i \"" + sha + "\" >nul || ( del \"" + newExe + "\" >nul 2>&1 & start \"\" \"" + cur + "\" & goto :eof )");
            sb.AppendLine("copy /y \"" + cur + "\" \"" + cur + ".bak\" >nul");                               // keep a rollback copy
            sb.AppendLine("copy /y \"" + newExe + "\" \"" + cur + "\" >nul || ( copy /y \"" + cur + ".bak\" \"" + cur + "\" >nul )");  // restore on failure
            sb.AppendLine("start \"\" \"" + cur + "\"");
            sb.AppendLine("del \"" + newExe + "\" >nul 2>&1");
            sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
            File.WriteAllText(bat, sb.ToString());
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(Shell.ResolveTool("cmd.exe"), "/c start \"\" /min \"" + bat + "\"") { UseShellExecute = false, CreateNoWindow = true };
                if (System.Diagnostics.Process.Start(psi) == null) return "Could not launch the update helper.";
            }
            catch (Exception ex) { return "Could not launch the update helper: " + ex.Message; }
            Log.W("Update staged. ArchTweaker will close and relaunch as {0}.", Program.Version);
            return null;
        }
    }

    // ---------- Catalog overlay (2.7) ----------
    // Lets new or corrected tweaks ship without a new exe. The file is XML, fetched from the update host and
    // checked against the sha256 in the manifest ("overlay" + "overlay_sha256"), stored as ProgramData\ArchTweaker\overlay.xml.
    // Only registry DWORD / string values are allowed (no commands, no services, no deletes of keys), under an allowlist
    // of policy/settings roots. Existing tweaks can be patched (title/desc/gain) or hidden, never given new operations.
    //   <overlay version="1">
    //     <tweak id="ov_example" category="Privacy" title="…" desc="…" impact="1" gain="…" risk="safe|moderate|advanced" restart="0">
    //       <dword hive="HKLM" key="SOFTWARE\Policies\…" name="…" on="1" off="delete|0" />
    //       <string hive="HKCU" key="Software\…" name="…" on="text" off="delete|text" />
    //     </tweak>
    //     <patch id="existing_id" title="…" desc="…" gain="…" hide="1" />
    //   </overlay>
    public static class Overlay
    {
        public static string FilePath { get { return Path.Combine(Paths.Root, "overlay.xml"); } }
        static readonly string[] Roots =
        {
            @"HKLM\SOFTWARE\Policies\", @"HKCU\Software\Policies\", @"HKCU\Software\Microsoft\", @"HKLM\SOFTWARE\Microsoft\",
            @"HKLM\SYSTEM\CurrentControlSet\Control\", @"HKCU\Control Panel\", @"HKCU\System\GameConfigStore"
        };
        // 3.2.1: keys and value names that start programs, even under an allowed root (Run keys, debugger hijacks, shells…)
        static readonly string[] DenyKeyParts =
        {
            @"\Run", @"Image File Execution Options", @"SilentProcessExit", @"\Winlogon", @"AppInit", @"\Explorer\Shell",
            @"\Classes\", @"\Services\", @"\Session Manager", @"\Lsa", @"\SafeBoot", @"\Terminal Server", @"\Print\", @"\Scripts",
            @"\Command Processor", @"\Policies\Explorer\Run", @"\Windows NT\CurrentVersion\Windows", @"\ShellServiceObject", @"\Active Setup",
            @"\App Paths", @"\Drivers32", @"\Shell Extensions", @"\ShellEx", @"\ShellIconOverlayIdentifiers", @"KnownDLLs", @"\Font Drivers",
            @"Notification Packages", @"\Authentication\", @"\Ports", @"\Netsh", @"\AeDebug", @"\Browser Helper Objects", @"\Wow6432Node\Microsoft\Windows\CurrentVersion\Run",
            // 3.35: more keys that start code, load DLLs or switch off protection
            @"User Shell Folders", @"\Shell Folders", @"\SecurityProviders", @"\NetworkProvider", @"\Windows Defender", @"\Environment",
            @"\Policies\System\", @"\Credential Providers",
            @"\Windows\Installer", @"\WindowsUpdate", @"\Terminal Services", @"\Policies\Microsoft\Edge", @"\Policies\Google", @"\Policies\Mozilla",
            @"\Policies\BraveSoftware", @"\Windows\PowerShell", @"\Safer", @"\CredentialsDelegation", @"\LanmanWorkstation", @"\LanmanServer",
            @"\Control\DeviceGuard", @"\Control\CI", @"\Internet Settings", @"\Attachments", @"\Office\", @"\Cryptography", @"\SystemCertificates",
            @"\Windows Script Host", @"\Control\SecurePipeServers", @"\WinRM", @"\AppLocker", @"\SrpV2", @"\WMI\Autologger", @"\Control\Keyboard Layout", @"\BootVerificationProgram",
        };
        static readonly string[] DenyNames = { "ImagePath", "Debugger", "ServiceDll", "Userinit", "Shell", "AppInit_DLLs", "Start", "BootExecute", "AutoRun", "Load", "Run", "GlobalFlag", "VerifierDlls", "MonitorProcess", "Path", "Script", "DelegateExecute",
            "Dll", "DllName", "Command", "CommandLine", "Library", "Program", "Exe", "ProxyServer", "AutoConfigURL" };   // 3.35
        public static bool PathAllowed(string hive, string key)
        {
            if (string.IsNullOrEmpty(key) || key.Contains("..") || key.StartsWith("\\")) return false;
            if (DenyKeyParts.Any(d => ("\\" + key.TrimEnd('\\') + "\\").IndexOf(d, StringComparison.OrdinalIgnoreCase) >= 0)) return false;   // 3.35: trailing \ so "\Policies\System\" matches the key itself
            string full = hive.ToUpperInvariant() + "\\" + key;
            return Roots.Any(r => full.StartsWith(r, StringComparison.OrdinalIgnoreCase) || (full + "\\").Equals(r, StringComparison.OrdinalIgnoreCase));
        }

        public static int Loaded { get; private set; }

        // Applied once while the catalog is built. Any malformed entry is skipped and logged; the built-in catalog always wins.
        public static void ApplyTo(System.Collections.Generic.List<Tweak> all) { ApplyTo(all, FilePath); }
        public static void ApplyTo(System.Collections.Generic.List<Tweak> all, string file)
        {
            Loaded = 0;
            if (!File.Exists(file)) return;
            // 3.2.1: load only the file ArchTweaker downloaded: owned by an administrator and matching the hash recorded then
            if (file == FilePath)
            {
                if (!Secure.TrustedData(file, "the tweak overlay")) return;
                object want = Reg.Get(Hive.HKLM, @"Software\ArchTweaker\Update", "OverlaySha256");
                string got; using (var st = File.OpenRead(file)) using (var h = SHA256.Create()) got = BitConverter.ToString(h.ComputeHash(st)).Replace("-", "").ToLowerInvariant();
                if (want == null || !string.Equals(want.ToString(), got, StringComparison.OrdinalIgnoreCase)) { Log.W("Overlay: ignored, it doesn't match the verified download."); return; }
            }
            try
            {
                var doc = new System.Xml.XmlDocument { XmlResolver = null };
                using (var rd = System.Xml.XmlReader.Create(file, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null }))
                    doc.Load(rd);
                var ids = new System.Collections.Generic.HashSet<string>(all.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
                foreach (System.Xml.XmlElement e in doc.DocumentElement.ChildNodes.OfType<System.Xml.XmlElement>())
                {
                    try
                    {
                        if (e.Name == "patch") Patch(e, all);
                        else if (e.Name == "tweak")
                        {
                            var t = Build(e);
                            if (t == null) continue;
                            if (ids.Contains(t.Id)) { Log.W("Overlay: {0} already exists, skipped (use <patch>)", t.Id); continue; }
                            all.Add(t); ids.Add(t.Id); Loaded++;
                        }
                    }
                    catch (Exception ex) { Log.W("Overlay: entry skipped: {0}", ex.Message); }
                }
                if (Loaded > 0) Log.W("Overlay: {0} extra tweak(s) loaded", Loaded);
            }
            catch (Exception ex) { Log.W("Overlay: ignored ({0})", ex.Message); }
        }

        static string A(System.Xml.XmlElement e, string n) { return e.HasAttribute(n) ? e.GetAttribute(n) : null; }

        static void Patch(System.Xml.XmlElement e, System.Collections.Generic.List<Tweak> all)
        {
            var t = all.FirstOrDefault(x => string.Equals(x.Id, A(e, "id"), StringComparison.OrdinalIgnoreCase));
            if (t == null) return;
            if (A(e, "title") != null) t.Title = A(e, "title");
            if (A(e, "desc") != null) t.Desc = A(e, "desc");
            if (A(e, "gain") != null) t.Gain = A(e, "gain");
            if (A(e, "hide") == "1") { all.Remove(t); Log.W("Overlay: {0} hidden", t.Id); }
        }

        static Tweak Build(System.Xml.XmlElement e)
        {
            string id = A(e, "id"), cat = A(e, "category");
            if (cat == "Security (advanced)") cat = DangerZone.CAT;   // 3.4: renamed category
            if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, "^ov_[a-z0-9_]{2,40}$")) { Log.W("Overlay: id must look like ov_name"); return null; }
            if (!Catalog.Categories.Contains(cat)) { Log.W("Overlay: {0}: unknown category", id); return null; }
            var t = new Tweak { Id = id, Category = cat, Title = A(e, "title") ?? id, Desc = A(e, "desc") ?? "", Gain = A(e, "gain") ?? "" };
            int imp; if (int.TryParse(A(e, "impact"), out imp)) t.Impact = Math.Max(0, Math.Min(3, imp));
            string risk = A(e, "risk"); t.Risk = risk == "advanced" ? Risk.Advanced : risk == "moderate" ? Risk.Moderate : Risk.Safe;
            t.Reboot = A(e, "restart") == "1";
            if (t.Dangerous) { t.Risk = Risk.Advanced; t.Breaks = A(e, "breaks") ?? "Features or protection Windows normally relies on. Read the description first."; }   // 3.4
            foreach (System.Xml.XmlElement op in e.ChildNodes.OfType<System.Xml.XmlElement>())
            {
                string hv = A(op, "hive") ?? "", key = A(op, "key"), name = A(op, "name"), on = A(op, "on"), off = A(op, "off");
                Hive h; if (hv == "HKLM") h = Hive.HKLM; else if (hv == "HKCU") h = Hive.HKCU; else throw new FormatException(id + ": hive must be HKLM or HKCU");
                if (!PathAllowed(hv, key) || string.IsNullOrEmpty(name) || on == null) throw new FormatException(id + ": key not allowed or value missing");
                if (DenyNames.Any(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase))) throw new FormatException(id + ": value name '" + name + "' is not allowed");
                if (op.Name == "dword")
                {
                    int v; if (!int.TryParse(on, out v)) throw new FormatException(id + ": dword 'on' must be a number");
                    object o = null; int ov; if (off != null && off != "delete") { if (!int.TryParse(off, out ov)) throw new FormatException(id + ": bad 'off'"); o = ov; }
                    t.Dw(h, key, name, v, o);
                }
                else if (op.Name == "string") t.Sz(h, key, name, on, off == null || off == "delete" ? null : off);
                else throw new FormatException(id + ": only <dword> and <string> are allowed");
            }
            if (t.Ops.Count == 0) throw new FormatException(id + ": no values");
            t.Explain.Add("From the downloaded tweak overlay (overlay.xml), verified by sha256 from the update manifest.");
            return t;
        }

        // Downloads the overlay named in the update manifest. Returns null on success, else a reason.
        public static string Fetch()
        {
            try
            {
                string url, sha;
                if (!Updater.OverlayInfo(out url, out sha)) return "The update manifest lists no overlay.";
                string tmp = FilePath + ".new";
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
                using (var wc = new Updater.PinnedClient()) wc.DownloadFile(url, tmp);
                string got;
                using (var st = File.OpenRead(tmp)) using (var h = SHA256.Create()) got = BitConverter.ToString(h.ComputeHash(st)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(got, sha, StringComparison.OrdinalIgnoreCase)) { File.Delete(tmp); return "Overlay hash mismatch: download refused."; }
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
                using (var b = Reg.Base(Hive.HKLM)) using (var k = b.CreateSubKey(@"Software\ArchTweaker\Update")) k.SetValue("OverlaySha256", got.ToLowerInvariant());   // checked at every load
                Log.W("Overlay downloaded and verified ({0}). Restart ArchTweaker to load it.", sha.Substring(0, 12));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
