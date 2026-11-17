// ArchTweaker 2.3: Context menu manager. Lists Explorer right-click entries added by programs and turns them
// off with Windows' own reversible switches (journaled, so Undo everything restores them):
//   - shell extensions  → value {CLSID} under HKLM\...\Shell Extensions\Blocked
//   - static commands    → empty "LegacyDisable" value on the command's key
// Changes appear after Explorer restarts. On Windows 11 these entries live under "Show more options".
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ArchTweaker
{
    public class CtxItem
    {
        public bool IsExtension;      // shell extension (CLSID) vs static command (verb)
        public string Root, Name, Clsid, Target;
        public Hive Hive; public string KeyPath;   // for commands: where LegacyDisable goes
        public bool Enabled, Builtin;
        public string Kind { get { return IsExtension ? "Shell extension" : "Command"; } }
        public string AppliesTo { get { return CtxMenu.Describe(Root); } }
    }

    public static class CtxMenu
    {
        public const string Blocked = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        static readonly string[] Roots = { "*", "AllFilesystemObjects", "Directory", @"Directory\Background", "Folder", "Drive", "DesktopBackground" };
        static readonly string[] CoreVerbs = { "open", "opennewwindow", "opennewtab", "opennewprocess", "explore", "find", "runas", "runasuser" };
        static readonly Regex GuidRx = new Regex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$");

        public static string Describe(string root)
        {
            switch (root)
            {
                case "*": return "All files";
                case "AllFilesystemObjects": return "Files and folders";
                case "Directory": return "Folders";
                case @"Directory\Background": return "Folder background";
                case "Folder": return "Folders (incl. libraries)";
                case "Drive": return "Drives";
                case "DesktopBackground": return "Desktop";
                default: return root;
            }
        }

        static string S(Hive h, string p, string n) { object o = Reg.Get(h, p, n); return o == null ? "" : o.ToString(); }

        static bool IsBuiltin(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            string f = Environment.ExpandEnvironmentVariables(file.Trim(new[] { '"' })).ToLowerInvariant();
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
            return win.Length > 0 && f.StartsWith(win + "\\", StringComparison.Ordinal);
        }

        public static List<CtxItem> Load()
        {
            var l = new List<CtxItem>();
            var blocked = new HashSet<string>(Reg.ValueNames(Hive.HKLM, Blocked).Concat(Reg.ValueNames(Hive.HKCU, Blocked)), StringComparer.OrdinalIgnoreCase);

            foreach (var root in Roots)
            {
                // shell extensions
                foreach (var name in Reg.SubKeys(Hive.HKCR, root + @"\shellex\ContextMenuHandlers"))
                {
                    string k = root + @"\shellex\ContextMenuHandlers\" + name;
                    string clsid = S(Hive.HKCR, k, "");
                    if (!GuidRx.IsMatch(clsid) && GuidRx.IsMatch(name)) clsid = name;
                    if (!GuidRx.IsMatch(clsid)) continue;
                    if (l.Any(x => x.IsExtension && x.Root == root && string.Equals(x.Clsid, clsid, StringComparison.OrdinalIgnoreCase))) continue;
                    string friendly = S(Hive.HKCR, @"CLSID\" + clsid, "");
                    string dll = S(Hive.HKCR, @"CLSID\" + clsid + @"\InprocServer32", "");
                    l.Add(new CtxItem
                    {
                        IsExtension = true, Root = root, Clsid = clsid,
                        Name = friendly.Length > 0 && friendly[0] != '@' ? friendly : name,
                        Target = dll, Enabled = !blocked.Contains(clsid), Builtin = IsBuiltin(dll)
                    });
                }
                // static commands (verbs)
                foreach (var verb in Reg.SubKeys(Hive.HKCR, root + @"\shell"))
                {
                    if (CoreVerbs.Contains(verb.ToLowerInvariant())) continue;
                    Hive h; string kp;
                    if (Reg.KeyExists(Hive.HKCU, @"Software\Classes\" + root + @"\shell\" + verb)) { h = Hive.HKCU; kp = @"Software\Classes\" + root + @"\shell\" + verb; }
                    else if (Reg.KeyExists(Hive.HKLM, @"SOFTWARE\Classes\" + root + @"\shell\" + verb)) { h = Hive.HKLM; kp = @"SOFTWARE\Classes\" + root + @"\shell\" + verb; }
                    else continue;
                    // read label/command from the merged HKCR view (an HKCU override may hold only a flag)
                    string hk = root + @"\shell\" + verb;
                    string label = S(Hive.HKCR, hk, "MUIVerb"); if (label.Length == 0) label = S(Hive.HKCR, hk, "");
                    if (label.Length == 0 || label[0] == '@') label = verb;
                    string cmd = S(Hive.HKCR, hk + @"\command", "");
                    bool disabled = Reg.Get(h, kp, "LegacyDisable") != null || Reg.ValueNames(h, kp).Any(n => string.Equals(n, "LegacyDisable", StringComparison.OrdinalIgnoreCase));
                    l.Add(new CtxItem
                    {
                        IsExtension = false, Root = root, Name = label.Replace("&", ""), Target = cmd,
                        Hive = h, KeyPath = kp, Enabled = !disabled, Builtin = IsBuiltin(ExeOf(cmd))
                    });
                }
            }
            return l.OrderBy(x => x.Builtin).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static string ExeOf(string cmd)
        {
            cmd = (cmd ?? "").Trim();
            if (cmd.StartsWith("\"")) { int e = cmd.IndexOf('"', 1); return e > 0 ? cmd.Substring(1, e - 1) : cmd.Substring(1); }
            int sp = cmd.IndexOf(' '); return sp > 0 ? cmd.Substring(0, sp) : cmd;
        }

        // Enable/disable one entry (journaled — Undo everything restores the original state).
        public static void Set(CtxItem it, bool enabled)
        {
            if (it.Enabled == enabled) return;
            if (it.IsExtension)
            {
                if (enabled) { Reg.DeleteValue(Hive.HKLM, Blocked, it.Clsid); Reg.DeleteValue(Hive.HKCU, Blocked, it.Clsid); }
                else Reg.Set(Hive.HKLM, Blocked, it.Clsid, RegistryValueKind.String, it.Name ?? "");
            }
            else
            {
                if (enabled) Reg.DeleteValue(it.Hive, it.KeyPath, "LegacyDisable");
                else Reg.Set(it.Hive, it.KeyPath, "LegacyDisable", RegistryValueKind.String, "");
            }
            it.Enabled = enabled;
            Log.W("{0} context-menu entry '{1}' ({2})", enabled ? "Enabled" : "Disabled", it.Name, it.AppliesTo);
        }
    }
}
