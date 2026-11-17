// ArchTweaker: NVIDIA Profile Inspector (.nip) files.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace ArchTweaker
{
    // ---------------- NVIDIA Profile Inspector (.nip) files ----------------
    public class NipSetting { public uint Id, Value; public string Name; }

    public static class NipFiles
    {
        const string LastKey = @"Software\ArchTweaker";

        // Base Profile DWORD settings only: per-game profiles and string / binary settings are skipped.
        public static List<NipSetting> Parse(string xml)
        {
            var l = new List<NipSetting>();
            var doc = new XmlDocument { XmlResolver = null };
            using (var sr = new StringReader(xml)) using (var xr = XmlReader.Create(sr, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) doc.Load(xr);
            foreach (XmlNode p in doc.SelectNodes("//Profile"))
            {
                var pn = p.SelectSingleNode("ProfileName");
                string name = pn == null ? "" : pn.InnerText.Trim();
                if (name.Length > 0 && !name.Equals("Base Profile", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (XmlNode s in p.SelectNodes("Settings/ProfileSetting"))
                {
                    var type = s.SelectSingleNode("ValueType"); var id = s.SelectSingleNode("SettingID"); var v = s.SelectSingleNode("SettingValue");
                    if (id == null || v == null || (type != null && !type.InnerText.Trim().Equals("Dword", StringComparison.OrdinalIgnoreCase))) continue;
                    ulong i, val;
                    if (!ulong.TryParse(id.InnerText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i) || !ulong.TryParse(v.InnerText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out val)) continue;
                    if (i > uint.MaxValue || val > uint.MaxValue) continue;
                    var nn = s.SelectSingleNode("SettingNameInfo");
                    l.Add(new NipSetting { Id = (uint)i, Value = (uint)val, Name = nn == null ? "" : nn.InnerText.Trim() });
                }
            }
            return l;
        }

        public static string Build(IEnumerable<NipSetting> settings)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n<ArrayOfProfile>\r\n  <Profile>\r\n    <ProfileName>Base Profile</ProfileName>\r\n    <Executeables />\r\n    <Settings>\r\n");
            foreach (var s in settings)
                sb.AppendFormat(CultureInfo.InvariantCulture, "      <ProfileSetting>\r\n        <SettingNameInfo>{0}</SettingNameInfo>\r\n        <SettingID>{1}</SettingID>\r\n        <SettingValue>{2}</SettingValue>\r\n        <ValueType>Dword</ValueType>\r\n      </ProfileSetting>\r\n",
                    System.Security.SecurityElement.Escape(s.Name ?? ""), s.Id, s.Value);
            sb.Append("    </Settings>\r\n  </Profile>\r\n</ArrayOfProfile>\r\n");
            return sb.ToString();
        }

        static string LastBackup()
        {
            try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.OpenSubKey(LastKey)) if (k != null) return k.GetValue("NvLastBackup") as string; } catch { }
            return null;
        }

        // Every value is written to a backup first ("0xID=value", or "0xID=-" when the driver had no value).
        public static string Import(string file)
        {
            var set = Parse(File.ReadAllText(file));
            if (set.Count == 0) return "No Base Profile DWORD settings in " + Path.GetFileName(file) + ".";
            string backup = Path.Combine(Paths.Root, "nvidia-profile-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            var lines = new List<string>();
            foreach (var s in set)
            {
                var v = NvDrs.Get(s.Id);
                lines.Add("0x" + s.Id.ToString("X8") + "=" + (v.Found ? v.Current.ToString(CultureInfo.InvariantCulture) : "-"));
            }
            File.WriteAllLines(backup, lines);
            int ok = 0;
            foreach (var s in set) { try { NvDrs.Set(s.Id, s.Value); ok++; } catch (Exception ex) { Log.W("  0x{0:X8}: {1}", s.Id, ex.Message); } }
            try { using (var b = Reg.Base(Hive.HKCU)) using (var k = b.CreateSubKey(LastKey)) k.SetValue("NvLastBackup", backup); } catch { }
            return string.Format("{0} of {1} settings imported into the NVIDIA global profile. Backup: {2}", ok, set.Count, backup);
        }

        public static string RestoreLast()
        {
            string backup = LastBackup();
            if (backup == null || !File.Exists(backup)) return "ArchTweaker found no NVIDIA profile backup from an import.";
            int n = 0;
            foreach (var line in File.ReadAllLines(backup))
            {
                var p = line.Split('='); uint id;
                if (p.Length != 2 || !uint.TryParse(p[0].Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id)) continue;
                uint v;
                if (p[1] == "-") NvDrs.Reset(id);
                else if (uint.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) NvDrs.Set(id, v);
                n++;
            }
            return n + " NVIDIA settings put back from " + backup;
        }

        // The driver API reads settings one by one, so the export covers the settings ArchTweaker knows plus those of the last import.
        public static int Export(string file)
        {
            var ids = new Dictionary<uint, string>();
            foreach (var op in Catalog.All.SelectMany(t => t.Ops).OfType<NvDrsOp>()) ids[op.Id] = op.Label;
            string backup = LastBackup();
            if (backup != null && File.Exists(backup))
                foreach (var line in File.ReadAllLines(backup)) { uint id; if (uint.TryParse(line.Split('=')[0].Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id) && !ids.ContainsKey(id)) ids[id] = ""; }
            var list = new List<NipSetting>();
            foreach (var kv in ids) { var v = NvDrs.Get(kv.Key); if (v.Found) list.Add(new NipSetting { Id = kv.Key, Value = v.Current, Name = kv.Value }); }
            File.WriteAllText(file, Build(list.OrderBy(x => x.Id)), Encoding.Unicode);
            return list.Count;
        }
    }
}
