// NVIDIA driver profile settings (2.7) through NvAPI DRS, the same store NVIDIA Control Panel and Profile Inspector use.
// Only the Base (global) profile is touched. Setting IDs and the struct layout come from NVIDIA's public SDK
// (github.com/NVIDIA/nvapi: NvApiDriverSettings.h, nvapi.h NVDRS_SETTING_V1 under #pragma pack(4), nvapi_interface.h).
// Undo: the previous value is saved to HKCU\Software\ArchTweaker\NvDrs before the first write. "d" means it was the
// driver default, so undo deletes the user value (back to default) instead of writing a number.
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class NvDrs
    {
        // ---- setting IDs (NvApiDriverSettings.h)
        public const uint PREFERRED_PSTATE = 0x1057EB71;      // Power management mode
        public const uint PRERENDERLIMIT = 0x007BA09E;        // Max pre-rendered frames (Low Latency Mode: 1 = On)
        public const uint SHADERCACHE_MAX = 0x00AC8497;       // Shader disk cache maximum size (0xFFFFFFFF = unlimited)
        public const uint FRL_FPS = 0x10835002;               // Max frame rate (0 = off)
        public const uint PSTATE_PREFER_MAX = 1, PSTATE_DEFAULT = 5;

        // ---- function IDs (nvapi_interface.h)
        const uint ID_Initialize = 0x0150E828, ID_CreateSession = 0x0694D52E, ID_DestroySession = 0xDAD9CFF8,
                   ID_LoadSettings = 0x375DBD6B, ID_SaveSettings = 0xFCBC7E14, ID_GetBaseProfile = 0xDA8466A0,
                   ID_GetSetting = 0x73BF8338, ID_SetSetting = 0x577DD202, ID_DeleteProfileSetting = 0xE4A26362;

        const int NVAPI_OK = 0; public const int NVAPI_SETTING_NOT_FOUND = -160;
        internal const int SETTING_SIZE = 12320;                      // sizeof(NVDRS_SETTING_V1), pack(4)
        internal const uint SETTING_VER = SETTING_SIZE | (1u << 16);
        internal const int OFF_ID = 4100, OFF_TYPE = 4104, OFF_LOCATION = 4108, OFF_ISCURPREDEF = 4112, OFF_CURRENT = 8220;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int D0();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DOutH(out IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DH(IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DHOutH(IntPtr s, out IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DGet(IntPtr s, IntPtr p, uint id, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DSet(IntPtr s, IntPtr p, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DDel(IntPtr s, IntPtr p, uint id);

        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr Q64(uint id);
        [DllImport("nvapi.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr Q32(uint id);

        static readonly object sync = new object();
        static bool? available;

        static T Fn<T>(uint id) where T : class
        {
            IntPtr p = IntPtr.Size == 8 ? Q64(id) : Q32(id);
            if (p == IntPtr.Zero) throw new InvalidOperationException("NvAPI function 0x" + id.ToString("X8") + " missing");
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        // True when an NVIDIA driver with NvAPI is installed and initialises.
        public static bool Available
        {
            get
            {
                lock (sync)
                {
                    if (available.HasValue) return available.Value;
                    try { available = Environment.OSVersion.Platform == PlatformID.Win32NT && Fn<D0>(ID_Initialize)() == NVAPI_OK; }
                    catch (Exception ex) { Log.W("NvAPI not available: {0}", ex.Message); available = false; }
                    return available.Value;
                }
            }
        }

        // Opens a DRS session on the base profile, runs work, saves when asked, always destroys the session.
        static void Session(bool save, Action<IntPtr, IntPtr> work)
        {
            lock (sync)
            {
                if (!Available) throw new InvalidOperationException("No NVIDIA driver (NvAPI) found.");
                IntPtr s;
                Check(Fn<DOutH>(ID_CreateSession)(out s), "CreateSession");
                try
                {
                    Check(Fn<DH>(ID_LoadSettings)(s), "LoadSettings");
                    IntPtr prof; Check(Fn<DHOutH>(ID_GetBaseProfile)(s, out prof), "GetBaseProfile");
                    work(s, prof);
                    if (save) Check(Fn<DH>(ID_SaveSettings)(s), "SaveSettings");
                }
                finally { try { Fn<DH>(ID_DestroySession)(s); } catch { } }
            }
        }
        static void Check(int rc, string what) { if (rc != NVAPI_OK) throw new NvApiException("NvAPI " + what + " failed (" + rc + ")", rc); }
        // NVAPI_NOT_SUPPORTED (-104): this driver or GPU doesn't have the setting (for example Smooth Motion before RTX 40).
        public const int NVAPI_NOT_SUPPORTED = -104;
        public static bool Unsupported(Exception ex) { var n = ex as NvApiException; return n != null && (n.Code == NVAPI_NOT_SUPPORTED || n.Code == NVAPI_SETTING_NOT_FOUND); }

        public struct Value { public bool Found; public uint Current; public bool UserSet; }

        // Builds a zeroed NVDRS_SETTING_V1 buffer with its version; caller frees.
        internal static IntPtr NewSetting()
        {
            IntPtr buf = Marshal.AllocHGlobal(SETTING_SIZE);
            for (int i = 0; i < SETTING_SIZE; i += 4) Marshal.WriteInt32(buf, i, 0);
            Marshal.WriteInt32(buf, 0, unchecked((int)SETTING_VER));
            return buf;
        }

        // Reads a DWORD setting of the base profile. Found = false when the driver has no value at all.
        public static Value Get(uint id)
        {
            var v = new Value();
            Session(false, (s, p) =>
            {
                IntPtr buf = NewSetting();
                try
                {
                    int rc = Fn<DGet>(ID_GetSetting)(s, p, id, buf);
                    if (rc == NVAPI_SETTING_NOT_FOUND) return;
                    Check(rc, "GetSetting");
                    v.Found = true;
                    v.Current = unchecked((uint)Marshal.ReadInt32(buf, OFF_CURRENT));
                    // location 0 = this (base) profile; isCurrentPredefined 0 = a user value
                    v.UserSet = Marshal.ReadInt32(buf, OFF_LOCATION) == 0 && Marshal.ReadInt32(buf, OFF_ISCURPREDEF) == 0;
                }
                finally { Marshal.FreeHGlobal(buf); }
            });
            return v;
        }

        public static void Set(uint id, uint value)
        {
            Session(true, (s, p) =>
            {
                IntPtr buf = NewSetting();
                try
                {
                    Marshal.WriteInt32(buf, OFF_ID, unchecked((int)id));
                    Marshal.WriteInt32(buf, OFF_TYPE, 0);          // NVDRS_DWORD_TYPE
                    Marshal.WriteInt32(buf, OFF_CURRENT, unchecked((int)value));
                    Check(Fn<DSet>(ID_SetSetting)(s, p, buf), "SetSetting");
                }
                finally { Marshal.FreeHGlobal(buf); }
            });
        }

        // Removes the user value so the driver default applies again.
        public static void Reset(uint id)
        {
            Session(true, (s, p) =>
            {
                int rc = Fn<DDel>(ID_DeleteProfileSetting)(s, p, id);
                if (rc != NVAPI_OK && rc != NVAPI_SETTING_NOT_FOUND) Check(rc, "DeleteProfileSetting");
            });
        }

        // ---- current refresh rate of the primary display (for the frame-rate cap)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
        public static int RefreshHz()
        {
            try
            {
                var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(null, -1, ref dm) && dm.dmDisplayFrequency > 1) return dm.dmDisplayFrequency;   // ENUM_CURRENT_SETTINGS
            }
            catch { }
            return 0;
        }
    }

    // One DWORD setting of the NVIDIA base profile. ValueFn lets the value depend on the PC (e.g. refresh − 3).
    public class NvDrsOp : Op
    {
        public uint Id; public Func<uint> ValueFn; public string Label, ValueLabel;
        public bool ZeroOk;   // 3.5: 0 is a real value (Off) for most switches; only computed values use 0 as "no value"
        const string PREV = @"Software\ArchTweaker\NvDrs";
        string Key { get { return "0x" + Id.ToString("X8"); } }
        uint Want { get { return ValueFn(); } }
        // "u" in PREV: the driver said it doesn't support this setting, so it was skipped and counts as done.
        bool Skipped { get { object p = Reg.Get(Hive.HKCU, PREV, Key); return p != null && p.ToString() == "u"; } }
        public override bool IsOn()
        {
            if (!NvDrs.Available) return false;
            if (Skipped) return true;
            try { var v = NvDrs.Get(Id); uint w = Want; return (w != 0 || ZeroOk) && v.Found && v.Current == w; }
            catch (Exception ex) { Log.W("  NvAPI read {0}: {1}", Label, ex.Message); return false; }
        }
        public override void Apply()
        {
            uint w = Want;
            if (w == 0 && !ZeroOk) { Log.W("  ({0}: no value for this PC — skipped)", Label); return; }
            try
            {
                if (Reg.Get(Hive.HKCU, PREV, Key) == null || Skipped)
                {
                    var cur = NvDrs.Get(Id);
                    using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(PREV))
                        k.SetValue(Key, cur.Found && cur.UserSet ? cur.Current.ToString() : "d");
                }
                NvDrs.Set(Id, w);
                Log.W("  NVIDIA profile: {0} = {1}", Label, ValueLabel);
            }
            catch (Exception ex)
            {
                if (!NvDrs.Unsupported(ex)) throw;
                using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(PREV)) k.SetValue(Key, "u");
                Log.W("  NVIDIA profile: {0} — this driver or GPU doesn't have this setting, skipped ({1})", Label, ex.Message);
            }
        }
        public override void Revert()
        {
            object prev = Reg.Get(Hive.HKCU, PREV, Key);
            if (prev == null) { NvDrs.Reset(Id); return; }
            if (prev.ToString() == "u")
            {
                using (var b = Reg.Base(Hive.HKCU)) using (var k = b.OpenSubKey(PREV, true)) if (k != null) k.DeleteValue(Key, false);
                return;
            }
            uint old;
            if (prev.ToString() == "d") NvDrs.Reset(Id);
            else if (uint.TryParse(prev.ToString(), out old)) NvDrs.Set(Id, old);
            using (var b = Reg.Base(Hive.HKCU)) using (var k = b.OpenSubKey(PREV, true)) if (k != null) k.DeleteValue(Key, false);
        }
        public override string Source { get { return "NVIDIA driver profile (NvAPI)"; } }
        public override string Describe()
        {
            return "NVIDIA global profile: " + Label + " → " + ValueLabel + "   [setting 0x" + Id.ToString("X8") + "]\r\n      undo: previous value restored (or driver default)";
        }
    }

    public class NvApiException : InvalidOperationException
    {
        public readonly int Code;
        public NvApiException(string msg, int code) : base(msg) { Code = code; }
    }
}
