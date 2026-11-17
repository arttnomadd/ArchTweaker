// 3.4: the Danger zone. Every known-dangerous tweak lives on one page: it is locked until the person unlocks it
// for the session, each tweak says in plain words what it can break, none of them is in a preset, an automatic
// pick or the health score, and applying one needs an explicit "I understand" plus a restore point.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ArchTweaker
{
    public static class DangerZone
    {
        public const string CAT = Cat.Danger;

        // Tweaks defined elsewhere that move here, with what each one can break.
        internal static readonly Dictionary<string, string> Moves = new Dictionary<string, string>
        {
            { "sec_spectre", "CPU side-channel protection. Browsers and other apps are open to Spectre / Meltdown-class attacks again." },
            { "sec_firewall", "All inbound filtering. Anything listening on this PC is reachable from the local network, and from the internet on public Wi-Fi. Some anti-cheats and multiplayer games expect the firewall." },
            { "sec_dep", "Exploit protection for every program. Some anti-cheats refuse to start without DEP." },
            { "sec_whql", "Driver-signing enforcement. A bad or malicious driver can load and crash or take over Windows." },
            { "sec_uac", "Every program runs with full admin rights, with no prompt. The Xbox app and Store sign-in, and many Store apps, stop working." },
            { "smartscreen", "The warning before unknown downloads and apps run. Phishing and malware files open without a check." },
            { "defender", "Real-time virus scanning. Without another antivirus, malware runs unnoticed." },
            { "vbs", "Kernel exploit protection (Memory Integrity). Some anti-cheats on Windows 11 refuse to start without it." },
            { "cmd_hypervisor", "WSL2, Docker, Hyper-V, Windows Sandbox, Memory Integrity, and anti-cheats that need virtualization." },
            { "gd_writecache", "Data safety on power loss. A power cut or crash can corrupt files that were just written, or the whole file system." },
            { "gd_nvme", "Vendor SSD tools and some backup apps may not see the drive. A Windows update can switch it back, or fail to boot with it." },
            { "net_ipv6off", "Networks and services that need IPv6: some ISPs, Xbox networking (Teredo), DirectAccess and parts of Windows that Microsoft only tests with IPv6 on." },
            { "pp_idle", "Cooling and power. Cores never rest, so temperatures and fan noise rise, laptops drain fast, and boost clocks can drop." },
            { "pp_modernstandby", "Sleep, on PCs whose firmware has no S3 sleep: the PC may not sleep at all, or not wake properly." },
            { "il_keyboard_lockout", "The keyboard everywhere, not only in games: the Windows key and Alt+Tab stop working, media keys stop, and Esc types '='." },
        };

        static bool unlocked;
        public static event Action Changed;
        // session only: every start of ArchTweaker begins locked
        public static bool Unlocked
        {
            get { return unlocked; }
            set { if (unlocked == value) return; unlocked = value; Log.W(value ? "Danger zone unlocked for this session." : "Danger zone locked."); var h = Changed; if (h != null) h(); }
        }

        public static bool Is(string id) { var t = Catalog.All.FirstOrDefault(x => x.Id == id); return t != null && t.Dangerous; }

        // Called while the catalog is built: move, mark Advanced and take out of every preset.
        internal static void Sort(List<Tweak> all)
        {
            foreach (var t in all)
            {
                string why;
                if (Moves.TryGetValue(t.Id, out why)) { t.Category = CAT; if (t.Breaks == null) t.Breaks = why; }
                if (t.Category != CAT) continue;
                t.Risk = Risk.Advanced;
                t.Presets = new string[0];
                if (string.IsNullOrEmpty(t.Breaks)) t.Breaks = "Features or protection Windows normally relies on. Read the description first.";
            }
            // the page lists the worst first: security off, then data and boot, then the rest
            var order = new[] { "defender", "sec_firewall", "sec_uac", "smartscreen", "vbs", "sec_spectre", "sec_dep", "sec_whql", "dz_sac", "dz_bitlocker", "dz_cfg", "gd_writecache", "gd_nvme", "cmd_hypervisor" };
            var moved = all.Where(t => t.Category == CAT).OrderBy(t => { int i = Array.IndexOf(order, t.Id); return i < 0 ? order.Length : i; }).ThenBy(t => all.IndexOf(t)).ToList();
            all.RemoveAll(t => t.Category == CAT);
            all.AddRange(moved);
        }

        // Ask to unlock (once per session). True when unlocked.
        public static bool Ensure(IWin32Window owner)
        {
            if (Unlocked) return true;
            using (var d = new DangerGate()) if (d.ShowDialog(owner) == DialogResult.OK) Unlocked = true;
            return Unlocked;
        }

        // small padlock under a locked switch
        public static void DrawLock(Graphics g, Rectangle r, Color under)
        {
            var f = NavItem.IconFont;
            Color c = Gfx.Mix(under, Theme.Risk, 0.75);
            if (f != null) { using (var sf = new Font(f.FontFamily, f.Size * 0.78f, GraphicsUnit.Pixel)) TextRenderer.DrawText(g, "", sf, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix); return; }
            TextRenderer.DrawText(g, L.T("locked"), Theme.FS, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // 3.30: the system-wide Control Flow Guard setting. It lives in 4 bits of the binary MitigationOptions value
    // (PROCESS_CREATION_MITIGATION_POLICY_CONTROL_FLOW_GUARD_*: 0 = default, 1 = always on, 2 = always off).
    // Only those bits change; the value before is kept so undo puts back exactly that.
    public static class CfgPolicy
    {
        const string Key = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", Name = "MitigationOptions";
        const string Mine = @"Software\ArchTweaker", Saved = "CfgWas";
        const int Byte = 5;   // bits 40–43 are the low nibble of byte 5

        public static int Nibble(byte[] v) { return v == null || v.Length <= Byte ? 0 : v[Byte] & 0x0F; }
        public static byte[] WithNibble(byte[] v, int n)
        {
            int len = Math.Max(v == null ? 0 : v.Length, 24);   // Windows writes 24 bytes on 10 and 11
            var r = new byte[len];
            if (v != null) Array.Copy(v, r, v.Length);
            r[Byte] = (byte)((r[Byte] & 0xF0) | (n & 0x0F));
            return r;
        }
        static byte[] Read() { return Reg.Get(Hive.HKLM, Key, Name) as byte[]; }
        public static bool IsOff() { return Nibble(Read()) == 2; }
        public static void TurnOff()
        {
            var v = Read();
            int was = Nibble(v);
            if (was != 2) Reg.SetRaw(Hive.HKCU, Mine, Saved, Microsoft.Win32.RegistryValueKind.DWord, was);
            Reg.Set(Hive.HKLM, Key, Name, Microsoft.Win32.RegistryValueKind.Binary, WithNibble(v, 2));
            Log.W("CFG: system setting off (was {0}).", was);
        }
        public static void Restore()
        {
            object o = Reg.Get(Hive.HKCU, Mine, Saved);
            int was = o is int ? (int)o : 0;
            var v = Read();
            if (v == null && was == 0) return;
            Reg.Set(Hive.HKLM, Key, Name, Microsoft.Win32.RegistryValueKind.Binary, WithNibble(v, was));
            Reg.DeleteValueRaw(Hive.HKCU, Mine, Saved);
            Log.W("CFG: system setting put back to {0}.", was);
        }
    }
}
