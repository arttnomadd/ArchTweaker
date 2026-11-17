// ArchTweaker - original Windows 10/11 tweaking utility
// Engine: registry access with automatic undo journal, services, command runner, state store.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace ArchTweaker
{
    public enum Hive { HKLM, HKCU, HKCR, HKU }

    public static class Paths
    {
        public static string Root
        {
            get
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ArchTweaker");
                Directory.CreateDirectory(p);
                Secure.Harden(p);   // 3.2.1: only SYSTEM and Administrators may write here (once per process)
                return p;
            }
        }
        public static string Backups
        {
            get { string p = Path.Combine(Root, "backup"); Directory.CreateDirectory(p); return p; }
        }
        public static string Journal { get { return Path.Combine(Root, "undo.journal"); } }
        public static string LogFile { get { return Path.Combine(Root, "archtweaker.log"); } }
    }

    public static class Log
    {
        public static event Action<string> Line;
        public static void W(string fmt, params object[] args)
        {
            string s = args.Length > 0 ? string.Format(fmt, args) : fmt;
            s = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
            try { File.AppendAllText(Paths.LogFile, s + Environment.NewLine); } catch { }
            var h = Line; if (h != null) h(s);
        }
        // 2.8: a message meant for the user. Logged like W(), and also shown in the status line under the page.
        // Internal details (aliases learned, probes, API fallbacks) use W() only, so they never reach the status line.
        public static event Action<string> Notice;
        public static void Say(string fmt, params object[] args)
        {
            string s = args.Length > 0 ? string.Format(fmt, args) : fmt;
            W("{0}", s);
            var h = Notice; if (h != null) h(s);
        }
    }

}
