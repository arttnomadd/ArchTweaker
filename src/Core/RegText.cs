// ArchTweaker: .reg file text (3.32): one place for hive names, quoting and value lines.
// Used by the registry snapshot, the recovery kit, the Pro undo file and Change history.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    public static class RegText
    {
        public static string LongHive(Hive h) { return h == Hive.HKLM ? "HKEY_LOCAL_MACHINE" : h == Hive.HKCU ? "HKEY_CURRENT_USER" : h == Hive.HKCR ? "HKEY_CLASSES_ROOT" : "HKEY_USERS"; }
        public static string Quote(string s) { return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
        public static bool Plain(string s) { return s != null && s.All(c => c >= ' '); }   // no CR/LF/NUL: safe inside a "…" .reg string
        public static string Hex(IEnumerable<byte> b) { return string.Join(",", b.Select(x => x.ToString("x2")).ToArray()); }

        // one .reg value line that puts the value back exactly (or deletes it if it didn't exist)
        public static string Line(string name, object v, RegistryValueKind k)
        {
            if (!string.IsNullOrEmpty(name) && !Plain(name)) throw new InvalidOperationException("value name with a line break");
            string n = string.IsNullOrEmpty(name) ? "@" : Quote(name);
            if (v == null) return n + "=-";
            switch (k)
            {
                case RegistryValueKind.DWord: return n + "=dword:" + unchecked((uint)Convert.ToInt32(v)).ToString("x8");
                case RegistryValueKind.QWord: return n + "=hex(b):" + Hex(BitConverter.GetBytes(Convert.ToInt64(v)));
                case RegistryValueKind.String:   // 3.27.1: a value with line breaks would add lines to the file: write it as hex(1)
                    return Plain(v.ToString()) ? n + "=" + Quote(v.ToString()) : n + "=hex(1):" + Hex(Encoding.Unicode.GetBytes(v.ToString() + "\0"));
                case RegistryValueKind.ExpandString: return n + "=hex(2):" + Hex(Encoding.Unicode.GetBytes(v.ToString() + "\0"));
                case RegistryValueKind.MultiString: return n + "=hex(7):" + Hex(Encoding.Unicode.GetBytes(string.Join("\0", (string[])v) + "\0\0"));
                default: return n + "=hex:" + Hex(v as byte[] ?? new byte[0]);
            }
        }
    }
}
