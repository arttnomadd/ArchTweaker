// ArchTweaker 3.23: upgrades for Lite and Pro mode.
//  - Recovery kit for every Pro tweak (not only the Danger zone), with the originals from the undo journal,
//    and a backfill for Pro tweaks applied before 3.23.
//  - PIN lock for Pro mode (for a PC set up for someone else).
//  - Pro: export the selected tweaks as a .reg file and a .cmd file.
//  - Lite: "Optimize safely" picks the recommended safe tweaks for this PC and applies them in one go.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class RescueBackfill
    {
        // Pro tweaks (and Danger zone tweaks) get a recovery kit entry before they are applied.
        public static bool Covers(Tweak t) { return t != null && (t.Dangerous || !Mode.LiteTweak(t)); }

        // Applied Pro tweaks with no kit entry yet: their originals come from the undo journal, so the kit
        // still restores the values from before ArchTweaker changed them.
        static int ran;
        // Once per start; the Tools button forces it.
        public static int Run() { return Run(false); }
        public static int Run(bool force)
        {
            if (!force && System.Threading.Interlocked.Exchange(ref ran, 1) == 1) return 0;   // 3.26: Shown fires again after each window rebuild
            int n = 0; var failed = new List<string>();
            try
            {
                var have = new HashSet<string>(RecoveryKit.Covered());
                var ids = new HashSet<string>(AppliedSet.Read() ?? new List<string>());
                foreach (var t in Catalog.All.Where(x => ids.Contains(x.Id) && Covers(x) && !have.Contains(x.Id)))
                {
                    if (RecoveryKit.Capture(t, true)) n++; else failed.Add(L.T(t.Title));   // 3.27.1: count only what was saved
                }
                if (n > 0) Log.W("Recovery kit: added {0} applied Pro tweak(s).", n);
                if (failed.Count > 0) Log.Say("The recovery kit could not save {0}. Tools & backup → Recovery kit shows the details.", string.Join(", ", failed.ToArray()));
            }
            catch (Exception ex) { Log.W("Recovery kit backfill: {0}", ex.Message); }
            return n;
        }
    }

    // A PIN that must be typed to switch to Pro mode. Stored as a salted PBKDF2 hash in HKLM (admins only).
    public static class ProPin
    {
        const string Key = @"SOFTWARE\ArchTweaker";
        public static string Hash(string pin, byte[] salt)
        {
            using (var k = new Rfc2898DeriveBytes(pin ?? "", salt, 10000)) return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(k.GetBytes(20));
        }
        public static string NewHash(string pin)
        {
            var salt = new byte[16]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(salt);
            return Hash(pin, salt);
        }
        public static bool Check(string stored, string pin)
        {
            try
            {
                var p = (stored ?? "").Split(':'); if (p.Length != 2) return false;
                string again = Hash(pin, Convert.FromBase64String(p[0]));
                // compare without an early exit
                int diff = again.Length ^ stored.Length;
                for (int i = 0; i < Math.Min(again.Length, stored.Length); i++) diff |= again[i] ^ stored[i];
                return diff == 0;
            }
            catch { return false; }
        }
        public static bool Valid(string pin) { return pin != null && pin.Length >= 4 && pin.Length <= 12 && pin.All(char.IsDigit); }

        static string Stored { get { try { return Reg.Get(Hive.HKLM, Key, "ProPin") as string; } catch { return null; } } }
        public static bool IsSet { get { return !string.IsNullOrEmpty(Stored); } }
        public static void Set(string pin)
        {
            using (var b = Reg.Base(Hive.HKLM)) using (var k = b.CreateSubKey(Key))
                if (pin == null) k.DeleteValue("ProPin", false); else k.SetValue("ProPin", NewHash(pin), RegistryValueKind.String);
            Log.W(pin == null ? "ArchTweaker removed the Pro mode PIN." : "Pro mode now needs a PIN.");
        }
        public static bool Verify(string pin) { return Check(Stored, pin); }

        // Asks for the PIN. True when there is no PIN or the right one was typed.
        public static bool Ask(IWin32Window owner, string title)
        {
            if (!IsSet) return true;
            string pin = PinDialog.Show(owner, title, "Type the PIN for Pro mode.");
            if (pin == null) return false;
            if (Verify(pin)) return true;
            Dlg.Error("Wrong PIN", "Pro mode stays off.");
            return false;
        }
    }

    // Pro: the selected tweaks as a .reg file (registry values) and a .cmd file (services, commands).
    public static class ProExport
    {
        static string Q(string s) { return RegText.Quote(s); }
        static string Root(Hive h)
        {
            switch (h) { case Hive.HKLM: return "HKEY_LOCAL_MACHINE"; case Hive.HKCU: return "HKEY_CURRENT_USER"; case Hive.HKCR: return "HKEY_CLASSES_ROOT"; }
            return null;
        }
        static string Value(string name, object v, RegistryValueKind k)
        {
            string n = string.IsNullOrEmpty(name) ? "@" : Q(name);
            if (v == null) return n + "=-";
            return RegText.Line(name, v, k);
        }

        // what = for each tweak: .reg lines and .cmd lines; things neither file can do are listed as comments
        public static void Build(IEnumerable<Tweak> tweaks, StringBuilder reg, StringBuilder cmd)
        {
            reg.Append("Windows Registry Editor Version 5.00\r\n\r\n; ArchTweaker " + Program.Version + " export. Import as administrator. Undo with ArchTweaker or your own backup.\r\n");
            cmd.Append("@echo off\r\nrem ArchTweaker " + Program.Version + " export. Run as administrator.\r\nnet session >nul 2>&1 || (echo Run this file as administrator. & pause & exit /b 1)\r\n");
            foreach (var t in tweaks)
            {
                reg.Append("\r\n; ---- " + t.Title + " (" + t.Id + ")\r\n");
                cmd.Append("\r\nrem ---- " + t.Title + " (" + t.Id + ")\r\n");
                foreach (var o in t.Ops)
                {
                    var v = o as VOp;
                    if (v != null && Root(v.H) != null) { reg.Append("[" + Root(v.H) + "\\" + v.Path + "]\r\n" + Value(v.Name, v.On, v.Kind) + "\r\n"); continue; }
                    var ka = o as KeyAddOp;
                    if (ka != null && Root(ka.H) != null) { reg.Append("[" + Root(ka.H) + "\\" + ka.Path + "]\r\n"); continue; }
                    var s = o as SvcOp;
                    if (s != null) { cmd.Append("sc.exe config \"" + s.Name + "\" start= " + (s.On == 4 ? "disabled" : s.On == 3 ? "demand" : s.On == 2 ? "auto" : s.On == 1 ? "system" : "boot") + "\r\n"); continue; }
                    reg.Append("; not exported: " + o.Describe().Replace("\r", " ").Replace("\n", " ") + "\r\n");
                }
                foreach (var c in t.ApplyCmds)
                {
                    if (c.Length < 2) continue;
                    if (c[0] == "ps") cmd.Append("powershell -NoProfile -ExecutionPolicy Bypass -Command \"" + c[1].Replace("\"", "\\\"") + "\"\r\n");
                    else cmd.Append(c[0] + " " + c[1] + "\r\n");
                }
                if (t.ApplyExtra != null) cmd.Append("rem not exported: a built-in action of ArchTweaker\r\n");
            }
        }

        public static void Run(IWin32Window owner, List<Tweak> tweaks)
        {
            if (tweaks.Count == 0) { Dlg.Error("Nothing to export", "Tick tweaks on any page first, or apply some. ArchTweaker exports the ticked tweaks, or the applied ones when nothing is ticked."); return; }
            using (var d = new SaveFileDialog { Filter = "Registry file|*.reg", FileName = "ArchTweaker-export.reg", Title = L.T("Export the tweaks") })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                var reg = new StringBuilder(); var cmd = new StringBuilder();
                Build(tweaks, reg, cmd);
                try
                {
                    File.WriteAllText(d.FileName, reg.ToString(), Encoding.Unicode);
                    string c = Path.ChangeExtension(d.FileName, ".cmd");
                    File.WriteAllText(c, cmd.ToString(), new UTF8Encoding(false));
                    Log.Say("Exported {0} tweak(s) to {1} and {2}.", tweaks.Count, d.FileName, c);
                }
                catch (Exception ex) { Dlg.Error("Export failed", ex.Message); }
            }
        }
    }

    public static class LiteActions
    {
        // Recommended tweaks for this PC that Lite allows and that make a measurable difference.
        public static List<string> SafePicks()
        {
            var rec = new HashSet<string>(Advisor.RecommendedIds());
            return Catalog.All.Where(t => rec.Contains(t.Id) && Mode.LiteTweak(t) && t.Applicable && t.Impact >= 1).Select(t => t.Id).ToList();
        }
        // 3.39: tweaks measured on this PC with no gain or a loss (fresh, 3+ runs each). Optimize safely offers to undo the applied ones.
        public static List<Tweak> NoGainCandidates()
        {
            return Catalog.All.Where(t => t.Applicable && Mode.Allows(t) && !t.Dangerous && IsNoGain(ProofStore.KindOf(t.Id))).ToList();
        }
        public static bool IsNoGain(ProofKind? k) { return k == ProofKind.NoGain || k == ProofKind.Worse; }
    }
}
