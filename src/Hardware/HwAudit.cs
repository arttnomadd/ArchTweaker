// ArchTweaker: Hardware and firmware audit (PCIe link, ReBAR, RAM, displays, VBS, BIOS checklist) and its page.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArchTweaker
{
    public class AuditItem
    {
        public string Area = "", Check = "", Value = "", Fix = "";
        public Level Level = Level.Info;   // Fix = should change, Tip = worth a look, Good, Info
    }

    public static class HwAudit
    {
        // ---------- pure helpers (unit-tested) ----------

        // PCIe link speed code (DEVPKEY_PciDevice_*LinkSpeed) → generation and GT/s
        public static string LinkGen(int code)
        {
            switch (code) { case 1: return "Gen 1 (2.5 GT/s)"; case 2: return "Gen 2 (5 GT/s)"; case 3: return "Gen 3 (8 GT/s)"; case 4: return "Gen 4 (16 GT/s)"; case 5: return "Gen 5 (32 GT/s)"; case 6: return "Gen 6 (64 GT/s)"; }
            return code > 0 ? "Gen " + code : "?";
        }

        // powercfg /qh prints the AC and DC index as the last two hex numbers, in any Windows language.
        public static int[] LastTwoHex(string text)
        {
            var m = Regex.Matches(text ?? "", @"0x([0-9a-fA-F]{8})");
            if (m.Count < 2) return null;
            return new[] { Convert.ToInt32(m[m.Count - 2].Groups[1].Value, 16), Convert.ToInt32(m[m.Count - 1].Groups[1].Value, 16) };
        }

        // Largest memory window of the GPU, in MB. Over 256 MB = Resizable BAR is on.
        public static long LargestMb(IEnumerable<ulong[]> ranges)
        {
            ulong max = 0;
            foreach (var r in ranges) if (r != null && r.Length == 2 && r[1] >= r[0]) max = Math.Max(max, r[1] - r[0] + 1);
            return (long)(max >> 20);
        }
        public static bool? RebarFromRanges(IEnumerable<ulong[]> ranges)
        {
            long mb = LargestMb(ranges);
            return mb <= 0 ? (bool?)null : mb > 256;
        }

        // RAM: configured below the module's own rated speed = a BIOS limit; equal = probably no XMP/EXPO.
        public static Level RamLevel(int configured, int rated)
        {
            if (configured <= 0 || rated <= 0) return Level.Info;
            if (configured < rated - 50) return Level.Fix;
            if (configured <= rated + 50) return Level.Tip;
            return Level.Good;
        }

        // ---------- WMI ----------

        static List<ManagementBaseObject> Q(string scope, string q)
        {
            var l = new List<ManagementBaseObject>();
            try { using (var s = new ManagementObjectSearcher(scope, q)) foreach (ManagementBaseObject o in s.Get()) l.Add(o); } catch { }
            return l;
        }
        static string S(ManagementBaseObject o, string p) { try { return o[p] == null ? "" : o[p].ToString(); } catch { return ""; } }

        const string LinkKey = "{3ab22e31-8264-4b4e-9af5-a8d2d8e33e62}";
        // {current speed, current width, max speed, max width}; 0 = unknown
        static int[] Link(string deviceId)
        {
            var r = new int[4];
            try
            {
                using (var mo = new ManagementObject(@"root\CIMV2", "Win32_PnPEntity.DeviceID='" + deviceId.Replace("\\", "\\\\").Replace("'", "\\'") + "'", null))
                {
                    var inP = mo.GetMethodParameters("GetDeviceProperties");
                    inP["devicePropertyKeys"] = new[] { LinkKey + " 9", LinkKey + " 10", LinkKey + " 11", LinkKey + " 12" };
                    var outP = mo.InvokeMethod("GetDeviceProperties", inP, null);
                    var props = outP == null ? null : outP["deviceProperties"] as ManagementBaseObject[];
                    if (props != null)
                        foreach (var p in props)
                        {
                            string k = S(p, "KeyName"); int v; if (!int.TryParse(S(p, "Data"), out v)) continue;
                            if (k.EndsWith(" 9")) r[0] = v; else if (k.EndsWith(" 10")) r[1] = v; else if (k.EndsWith(" 11")) r[2] = v; else if (k.EndsWith(" 12")) r[3] = v;
                        }
                }
            }
            catch { }
            return r;
        }

        static List<ulong[]> MemRanges(string deviceId)
        {
            var l = new List<ulong[]>();
            string id = deviceId.Replace("\\", "\\\\").Replace("'", "\\'");
            foreach (var o in Q(@"root\CIMV2", "ASSOCIATORS OF {Win32_PnPEntity.DeviceID='" + id + "'} WHERE ResultClass = Win32_DeviceMemoryAddress"))
            {
                ulong a, b;
                if (ulong.TryParse(S(o, "StartingAddress"), out a) && ulong.TryParse(S(o, "EndingAddress"), out b)) l.Add(new[] { a, b });
            }
            return l;
        }

        // ---------- displays ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string dev, uint i, ref DISPLAY_DEVICE dd, uint flags);

        static void Displays(List<AuditItem> o)
        {
            for (uint i = 0; i < 16; i++)
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & 1) == 0) continue;   // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
                var cur = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                if (!EnumDisplaySettings(dd.DeviceName, -1, ref cur)) continue;
                int best = cur.dmDisplayFrequency;
                var m = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                for (int k = 0; k < 2000 && EnumDisplaySettings(dd.DeviceName, k, ref m); k++)
                    if (m.dmPelsWidth == cur.dmPelsWidth && m.dmPelsHeight == cur.dmPelsHeight) best = Math.Max(best, m.dmDisplayFrequency);
                var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                string name = EnumDisplayDevices(dd.DeviceName, 0, ref mon, 0) && !string.IsNullOrEmpty(mon.DeviceString) ? mon.DeviceString : dd.DeviceName;
                bool primary = (dd.StateFlags & 4) != 0;
                var it = new AuditItem { Area = "Display", Check = L.F("Refresh rate: {0}", name) + (primary ? " (" + L.T("main") + ")" : ""),
                    Value = L.F("{0} Hz at {1}×{2} (best: {3} Hz)", cur.dmDisplayFrequency, cur.dmPelsWidth, cur.dmPelsHeight, best) };
                if (cur.dmDisplayFrequency + 1 < best) { it.Level = Level.Fix; it.Fix = L.F("Settings → System → Display → Advanced display → choose {0} Hz. Check the cable too: an old HDMI cable can limit the rate.", best); }
                else it.Level = Level.Good;
                o.Add(it);
            }
        }

        // ---------- the audit ----------

        public static List<AuditItem> Run()
        {
            var o = new List<AuditItem>();
            Action<Action> safe = a => { try { a(); } catch (Exception ex) { Log.W("Hardware audit: {0}", ex.Message); } };

            // GPU: PCIe link, Resizable BAR, driver age
            safe(() =>
            {
                foreach (var g in Q(@"root\CIMV2", "SELECT Name,PNPDeviceID,DriverDate,DriverVersion FROM Win32_VideoController"))
                {
                    string name = S(g, "Name"), id = S(g, "PNPDeviceID");
                    if (!id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) continue;
                    var ln = Link(id);
                    if (ln[0] > 0 && ln[1] > 0)
                    {
                        var it = new AuditItem { Area = "Graphics", Check = L.F("PCIe link: {0}", name), Value = L.F("{0} x{1} (card supports {2} x{3})", LinkGen(ln[0]), ln[1], LinkGen(ln[2]), ln[3]) };
                        if (ln[3] > 0 && ln[1] < ln[3]) { it.Level = Level.Fix; it.Fix = L.T("The card runs with fewer PCIe lanes than it has. Use the top x16 slot, reseat the card and check a riser cable. In the BIOS, set the slot to x16 (no bifurcation)."); }
                        else if (ln[2] > 0 && ln[0] < ln[2]) { it.Level = Level.Tip; it.Fix = L.T("The link speed is lower than the card's. GPUs lower it at idle to save power: check again while a game runs. If it stays low, set the slot to Auto or the highest Gen in the BIOS."); }
                        else it.Level = Level.Good;
                        o.Add(it);
                    }
                    var ranges = MemRanges(id);
                    var rebar = RebarFromRanges(ranges);
                    if (rebar.HasValue)
                        o.Add(new AuditItem { Area = "Graphics", Check = L.F("Resizable BAR: {0}", name), Value = rebar.Value ? L.F("on (memory window {0} MB)", LargestMb(ranges)) : L.T("off (256 MB window)"),
                            Level = rebar.Value ? Level.Good : Level.Tip, Fix = rebar.Value ? "" : L.T("In the BIOS turn on Above 4G Decoding and Re-Size BAR Support (CSM must be off). Most RTX 30/40/50 and RX 6000+ cards gain a few % in games that use it.") });
                    string dd = S(g, "DriverDate");
                    if (dd.Length >= 8)
                        try
                        {
                            var d = ManagementDateTimeConverter.ToDateTime(dd);
                            int days = (int)(DateTime.Now - d).TotalDays;
                            o.Add(new AuditItem { Area = "Graphics", Check = L.F("Driver age: {0}", name), Value = S(g, "DriverVersion") + " · " + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " · " + L.F("{0} days old", days),
                                Level = days > 270 ? Level.Tip : Level.Good, Fix = days > 270 ? L.T("New games often get fixes in newer drivers. Update with NVCleanstall (NVIDIA) or the AMD/Intel installer.") : "" });
                        }
                        catch { }
                }
            });

            // NVMe: PCIe link and firmware
            safe(() =>
            {
                foreach (var c in Q(@"root\CIMV2", "SELECT Name,DeviceID FROM Win32_PnPEntity WHERE PNPClass='SCSIAdapter' AND DeviceID LIKE 'PCI%'"))
                {
                    string name = S(c, "Name"); if (name.IndexOf("NVM", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var ln = Link(S(c, "DeviceID"));
                    if (ln[0] <= 0) continue;
                    var it = new AuditItem { Area = "Storage", Check = L.F("PCIe link: {0}", name), Value = L.F("{0} x{1} (drive supports {2} x{3})", LinkGen(ln[0]), ln[1], LinkGen(ln[2]), ln[3]) };
                    if ((ln[3] > 0 && ln[1] < ln[3]) || (ln[2] > 0 && ln[0] < ln[2])) { it.Level = Level.Tip; it.Fix = L.T("The drive runs below its own link. It may sit in a chipset or slower M.2 slot, or share lanes. The motherboard manual lists which M.2 slot is CPU-attached."); }
                    else it.Level = Level.Good;
                    o.Add(it);
                }
                foreach (var d in Q(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName,FirmwareVersion,BusType,MediaType FROM MSFT_PhysicalDisk"))
                {
                    if (S(d, "BusType") != "17") continue;   // NVMe
                    o.Add(new AuditItem { Area = "Storage", Check = L.F("Firmware: {0}", S(d, "FriendlyName")), Value = S(d, "FirmwareVersion"), Level = Level.Info,
                        Fix = L.T("Compare with the newest firmware on the maker's site. Some firmware updates fix slowdowns and drive failures (for example Samsung 980/990 Pro, Crucial P5 Plus).") });
                }
            });

            // RAM
            safe(() =>
            {
                var r = HwProbe.RamSpeed();
                if (r == null) return;
                var lv = RamLevel(r[0], r[1]);
                o.Add(new AuditItem { Area = "Memory", Check = L.T("RAM speed"), Value = L.F("{0} MT/s (module rated {1} MT/s without a profile)", r[0], r[1]), Level = lv,
                    Fix = lv == Level.Fix ? L.T("The RAM runs below its own rated speed. Load the XMP/EXPO profile in the BIOS, or check that all modules are the same kit.")
                        : lv == Level.Tip ? L.T("The RAM runs at the standard speed. If the kit is sold as faster (for example DDR5-6000), turn on XMP/EXPO in the BIOS.") : "" });
            });

            safe(() => Displays(o));

            // Windows graphics settings
            safe(() =>
            {
                object h = Reg.Get(Hive.HKLM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
                bool on = h is int && (int)h == 2;
                o.Add(new AuditItem { Area = "Windows", Check = L.T("Hardware-accelerated GPU scheduling (HAGS)"), Value = h == null ? L.T("default") : on ? L.T("on") : L.T("off"), Level = Level.Info,
                    Fix = L.T("DLSS Frame Generation needs it on. Otherwise compare both settings with the Benchmark page.") });
                object mpo = Reg.Get(Hive.HKLM, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode");
                o.Add(new AuditItem { Area = "Windows", Check = L.T("Multiplane overlay (MPO)"), Value = mpo is int && (int)mpo == 5 ? L.T("off") : L.T("on (Windows default)"), Level = Level.Info,
                    Fix = L.T("Turn MPO off only for flicker or stutter in borderless games with a second monitor.") });
                object gm = Reg.Get(Hive.HKCU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");
                bool gmOff = gm is int && (int)gm == 0;
                o.Add(new AuditItem { Area = "Windows", Check = L.T("Game Mode"), Value = gmOff ? L.T("off") : L.T("on"), Level = gmOff ? Level.Tip : Level.Good,
                    Fix = gmOff ? L.T("Game Mode stops Windows Update installs and driver installs during games. Turn it on in Settings → Gaming → Game Mode.") : "" });
            });

            // VBS / HVCI
            safe(() =>
            {
                foreach (var v in Q(@"root\Microsoft\Windows\DeviceGuard", "SELECT VirtualizationBasedSecurityStatus,SecurityServicesRunning FROM Win32_DeviceGuard"))
                {
                    bool vbs = S(v, "VirtualizationBasedSecurityStatus") == "2", hvci = false;
                    try { var a = v["SecurityServicesRunning"] as uint[]; hvci = a != null && a.Contains(2u); } catch { }
                    o.Add(new AuditItem { Area = "Windows", Check = L.T("Virtualization-based security (VBS) and Memory integrity"), Value = vbs ? (hvci ? L.T("VBS running, Memory integrity on") : L.T("VBS running")) : L.T("off"),
                        Level = vbs ? Level.Tip : Level.Good, Fix = vbs ? L.T("VBS and Memory integrity can cost a few % in CPU-bound games. The Danger zone has tweaks to turn them off. Keep them on if you need them for security or anti-cheat.") : "" });
                    break;
                }
            });

            // core parking (active plan, AC)
            safe(() =>
            {
                var v = LastTwoHex(Shell.Run("powercfg.exe", "/qh scheme_current sub_processor CPMINCORES", true, 15000));
                if (v == null) return;
                o.Add(new AuditItem { Area = "Power", Check = L.T("Core parking (minimum unparked cores, plugged in)"), Value = v[0] + " %", Level = v[0] >= 100 ? Level.Good : Level.Tip,
                    Fix = v[0] >= 100 ? "" : L.T("Windows parks cores when the load is low, and unparking can add a short delay. The Power Plan page has a tweak for 100 % unparked cores.") });
            });

            // timer resolution
            safe(() =>
            {
                double t = TimerRes.CurrentMs();
                if (t > 0) o.Add(new AuditItem { Area = "Power", Check = L.T("Timer resolution now"), Value = t.ToString("0.000", CultureInfo.InvariantCulture) + " ms", Level = t <= 1.01 ? Level.Good : Level.Info,
                    Fix = t <= 1.01 ? "" : L.T("15.6 ms is normal at the desktop. Games and the timer-resolution tweak raise it to 0.5–1 ms.") });
            });

            // BIOS checklist: what software can't read
            o.Add(new AuditItem { Area = "BIOS", Check = L.T("Settings Windows can't read"), Value = L.T("check by hand"), Level = Level.Info,
                Fix = L.T("XMP/EXPO on · Above 4G + Re-Size BAR on · CSM off · spread spectrum off · PCIe slot at x16. Intel: E-cores and HT as you need them. AMD X3D: chipset driver and Game Bar for the CCD scheduler.") });
            return o;
        }
    }

    public class HwAuditPage : Panel, IPage
    {
        readonly ListView lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        readonly Label sum;
        readonly TextBox fix = new TextBox { Dock = DockStyle.Bottom, Height = 70, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical };
        List<AuditItem> items = new List<AuditItem>();

        public HwAuditPage()
        {
            BackColor = Theme.Bg;
            Theme.DarkList(lv);
            lv.Columns.Add("Area", 90); lv.Columns.Add(new ColumnHeader { Text = "Status", Width = 80, Tag = "pill" }); lv.Columns.Add("Check", 330); lv.Columns.Add("Found", 360);
            lv.SelectedIndexChanged += delegate { var a = lv.SelectedItems.Count == 0 ? null : lv.SelectedItems[0].Tag as AuditItem; fix.Text = a == null ? "" : (a.Fix.Length > 0 ? a.Fix : L.T("Nothing to do.")); };
            fix.BackColor = Theme.Panel; fix.ForeColor = Theme.Text; fix.Font = Theme.F; fix.Tag = "notr";
            var bar = Theme.Bar();
            bar.Controls.Add(Theme.Lead("Check again", delegate { Fill(); }));
            bar.Controls.Add(Theme.Btn("Copy as text", delegate { Copy(); }));
            sum = Theme.StatusText();
            bar.Controls.Add(sum);
            Controls.Add(lv); Controls.Add(fix);
            Controls.Add(Theme.Note("ArchTweaker reads what Windows reports about your hardware and its settings. Select a row to see how to fix it. The audit changes nothing."));
            Controls.Add(bar);
            Skin.SetEmpty(lv, "Reading the hardware…");
        }
        public void OnShow() { if (items.Count == 0) Fill(); }
        public void Filter(string q) { }

        void Fill()
        {
            Skin.SetEmpty(lv, "Reading the hardware…"); lv.Items.Clear(); sum.Text = "";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<AuditItem> l;
                try { l = HwAudit.Run(); } catch (Exception ex) { Log.W("Hardware audit: {0}", ex.Message); l = new List<AuditItem>(); }
                if (!IsHandleCreated || IsDisposed) return;
                try { BeginInvoke((Action)(() => Show(l))); } catch (InvalidOperationException) { }
            });
        }
        static string Word(Level l) { return l == Level.Fix ? "Fix" : l == Level.Tip ? "Check" : l == Level.Good ? "Good" : "Info"; }
        void Show(List<AuditItem> l)
        {
            items = l;
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var a in l.OrderBy(x => x.Level == Level.Fix ? 0 : x.Level == Level.Tip ? 1 : x.Level == Level.Good ? 3 : 2))
            {
                var it = new ListViewItem(new[] { L.T(a.Area), L.T(Word(a.Level)), a.Check, a.Value }) { Tag = a };
                if (a.Level == Level.Fix) it.ForeColor = Theme.Bad; else if (a.Level == Level.Tip) it.ForeColor = Theme.Warn;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
            Skin.Loaded(lv, "Windows reported nothing to check.");
            int f = l.Count(x => x.Level == Level.Fix), t = l.Count(x => x.Level == Level.Tip);
            sum.Text = f + t == 0 ? L.T("Nothing to fix.") : L.F("{0} to fix · {1} to check", f, t);
            sum.ForeColor = f > 0 ? Theme.Bad : t > 0 ? Theme.Warn : Theme.Ok;
            if (lv.Items.Count > 0) lv.Items[0].Selected = true;
        }
        void Copy()
        {
            var sb = new StringBuilder("ArchTweaker " + Program.Version + " · " + L.T("Hardware audit") + "\r\n");
            foreach (var a in items) sb.Append("[" + L.T(Word(a.Level)) + "] " + L.T(a.Area) + " · " + a.Check + ": " + a.Value + (a.Fix.Length > 0 ? "\r\n    " + a.Fix : "") + "\r\n");
            try { Clipboard.SetText(sb.ToString()); Log.Say("Hardware audit copied to the clipboard."); } catch (Exception ex) { Log.W("Clipboard: {0}", ex.Message); }
        }
    }
}
