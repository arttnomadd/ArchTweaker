// ArchTweaker 2.2: screen, font-rendering, DirectX game-display and image/thumbnail tweaks.
// Every value is set through the journaled Reg.Set, so "Undo everything" restores the exact prior state.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace ArchTweaker
{
    // Windows 11 keeps its per-user DirectX game settings as ONE string:
    //   HKCU\Software\Microsoft\DirectX\UserGpuPreferences  DirectXUserGlobalSettings = "SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;"
    // Setting the whole value would wipe the user's other toggles, so this op edits a single token.
    public class DxTokenOp : Op
    {
        public const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
        public const string Value = "DirectXUserGlobalSettings";
        public string Token, Val;

        // Segments without "=" are kept verbatim (Value = null) so a rewrite never drops something Windows wrote.
        static List<KeyValuePair<string, string>> Parse(string s)
        {
            var l = new List<KeyValuePair<string, string>>();
            foreach (var part in (s ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) { l.Add(new KeyValuePair<string, string>(part, null)); continue; }
                l.Add(new KeyValuePair<string, string>(part.Substring(0, eq).Trim(), part.Substring(eq + 1).Trim()));
            }
            return l;
        }
        static string Join(List<KeyValuePair<string, string>> l) { return string.Concat(l.Select(kv => (kv.Value == null ? kv.Key : kv.Key + "=" + kv.Value) + ";").ToArray()); }
        static string Cur() { object o = Reg.Get(Hive.HKCU, Key, Value); return o == null ? "" : o.ToString(); }

        public override bool IsOn() { return Parse(Cur()).Any(kv => kv.Value != null && string.Equals(kv.Key, Token, StringComparison.OrdinalIgnoreCase) && kv.Value == Val); }
        public override void Apply()
        {
            var l = Parse(Cur()).Where(kv => kv.Value == null || !string.Equals(kv.Key, Token, StringComparison.OrdinalIgnoreCase)).ToList();
            l.Add(new KeyValuePair<string, string>(Token, Val));
            Reg.Set(Hive.HKCU, Key, Value, RegistryValueKind.String, Join(l));
        }
        public override void Revert()
        {
            var l = Parse(Cur()).Where(kv => kv.Value == null || !string.Equals(kv.Key, Token, StringComparison.OrdinalIgnoreCase)).ToList();
            if (l.Count == 0) Reg.DeleteValue(Hive.HKCU, Key, Value);
            else Reg.Set(Hive.HKCU, Key, Value, RegistryValueKind.String, Join(l));
        }
        public override string Describe()
        {
            return "Registry: HKCU\\" + Key + "\r\n      \"" + Value + "\" token " + Token + "=" + Val + " (other tokens kept)\r\n      undo: token " + Token + " removed (Windows default)";
        }
    }

    public static class DxExt
    {
        public static Tweak Dx(this Tweak t, string token, string val) { t.Ops.Add(new DxTokenOp { Token = token, Val = val }); return t; }
    }

    public static partial class Catalog
    {
        static void AddDisplay()
        {
            const Hive LM = Hive.HKLM, CU = Hive.HKCU;
            string I = Cat.Interface, GD = Cat.Gpu, E = Cat.Explorer;
            string safe = PRESET_SAFE, game = PRESET_GAMING, priv = PRESET_PRIVACY;
            const string DESK = @"Control Panel\Desktop";

            // ---------------- SCREEN & FONT RENDERING ----------------
            T("disp_fontsmooth_std", I, "Standard font smoothing instead of ClearType", "Switches text anti-aliasing from ClearType (sub-pixel, coloured edges) to standard grayscale smoothing. Useful on OLED or BGR/odd sub-pixel panels where ClearType shows colour fringing. Smoothing stays on.")
                .Info("Sets FontSmoothingType = 1 (standard). Revert puts it back to 2 (ClearType). Sign out or restart apps to see it everywhere.")
                .Dw(CU, DESK, "FontSmoothingType", 1, 2)
                .NeedsRelogon().R(Risk.Safe);

            T("disp_dpi_fixblurry", I, "Fix blurry apps on scaled displays", "Turns on 'Let Windows try to fix apps so they're not blurry'. When you change display scaling or dock/undock a monitor, older desktop apps are re-rendered at the new DPI instead of being bitmap-stretched.")
                .Dw(CU, DESK, "EnablePerProcessSystemDPI", 1, null)
                .NeedsRelogon().In(safe).R(Risk.Safe);

            T("disp_wallpaper_quality", I, "Full-quality wallpaper (no JPEG recompression)", "Windows recompresses JPEG wallpapers to 85 % quality, which adds visible banding/blocking on a 1440p+ screen. This sets import quality to 100 %. Takes effect the next time you set a wallpaper.")
                .Dw(CU, DESK, "JPEGImportQuality", 100, null)
                .In(safe).R(Risk.Safe);

            // ---------------- DIRECTX GAME DISPLAY (Windows 11) ----------------
            // windowed-games flip model lives in Catalog/CoreTweaks.cs as "windowedopt" (2.5: merged; it used to overwrite the whole DirectX string)
            T("disp_dx_vrr", GD, "Variable refresh rate for all games", "Lets games that don't officially support G-SYNC/FreeSync still use variable refresh in DirectX 11 fullscreen. Needs a VRR-capable monitor with VRR enabled in the GPU driver.")
                .Dx("VRROptimizeEnable", "1")
                .In(game).R(Risk.Safe);

            T("disp_dx_autohdr", GD, "Auto HDR for DirectX 11/12 games", "Windows 11 Auto HDR: converts SDR games to HDR on an HDR display. Small GPU cost. Only works when HDR is on in Display settings.")
                .Dx("AutoHDREnable", "1")
                .W11().R(Risk.Safe);

            // ---------------- IMAGES & THUMBNAILS ----------------
            T("disp_iconsonly", E, "Show icons instead of thumbnails", "Explorer stops generating image/video previews and shows file-type icons. Big photo/video folders open instantly and stop hammering the disk. Turn off if you browse images by preview.")
                .Dw(CU, ADV, "IconsOnly", 1, 0)
                .R(Risk.Safe);

            T("disp_iconcache", E, "Larger icon cache (4096 icons)", "Raises Explorer's in-memory icon cache from the small default so icons stop going blank or redrawing on systems with many programs and shortcuts.")
                .Sz(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "Max Cached Icons", "4096", null)
                .NeedsReboot().In(safe).R(Risk.Safe);

            T("disp_thumbsdb_net", E, "No thumbs.db on network folders", "Stops Explorer from writing hidden thumbs.db files into network shares and NAS folders. These files lock other files and show a list of your images to anyone with access to the share.")
                .Dw(CU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableThumbsDBOnNetworkFolders", 1, null)
                .In(safe, priv).R(Risk.Safe);
        }

        static readonly Dictionary<string, object[]> GainsD = new Dictionary<string, object[]>
        {
            { "disp_fontsmooth_std", new object[] { 0, "No speed effect. Cleaner text on panels where ClearType shows colour fringes." } },
            { "disp_dpi_fixblurry", new object[] { 0, "No speed effect. Sharper old apps after changing scaling or docking." } },
            { "disp_wallpaper_quality", new object[] { 0, "No speed effect. Removes JPEG banding on the desktop background." } },
            { "disp_dx_vrr", new object[] { 1, "Smoother frame pacing (no tearing, less stutter) in games without native VRR support. Needs a VRR monitor." } },
            { "disp_dx_autohdr", new object[] { 0, "Image quality, not speed — a small GPU cost. Only works with HDR on." } },
            { "disp_iconsonly", new object[] { 1, "Large image and video folders open much faster. No thumbnail generation on disk." } },
            { "disp_iconcache", new object[] { 0, "No FPS effect. Fewer blank/redrawn icons in Explorer and the Start menu." } },
            { "disp_thumbsdb_net", new object[] { 0, "No speed effect. No file locks or thumbs.db clutter on network shares." } },
        };
    }
}
