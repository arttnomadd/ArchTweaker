// ArchTweaker 2.3: "never touch" lists, built from the protected-location data in the Reg Organizer ignore
// exports (AppUninstIgnore, IgnoreRegCleaner, CleanupIgnore) and generalized to substrings so they hold on any
// drive letter. Used by the uninstaller's leftover scan and deep clean as an extra safety net
// on top of their own guards. All comparisons are case-insensitive substring matches.
using System;
using System.Linq;

namespace ArchTweaker
{
    public static class SafeLists
    {
        // Folders/files the uninstaller must never offer as "leftovers" (system state, security products, profiles).
        public static readonly string[] LeftoverPaths =
        {
            @"\$recycle.bin", @"\recycler", @"\system volume information", @"\pagefile.sys", @"\hiberfil.sys", @"\swapfile.sys",
            @"\bootmgr", @"\efi\", @"\recovery\", @"\$windows.~bt", @"\$winreagent", @"\config.msi", @"\windows\installer\",
            @"\system32\config\",
            @"\programdata\microsoft\debug", @"\programdata\microsoft\network\downloader", @"\programdata\microsoft\search",
            @"\programdata\microsoft\windows\wer", @"\microsoft\windows defender", @"\microsoft antimalware",
            @"\microsoft\cryptneturlcache", @"\microsoft\network\downloader", @"\microsoft\rac\", @"\microsoft\search\",
            @"\microsoft\wer\", @"\microsoft\windows live\services\cache", @"\microsoft\windows\cookies",
            @"\microsoft\windows\temporary internet files", @"\windows sidebar", @"\windowsapps\",
            @"\kaspersky lab", @"\kasperskylab", @"\doctor web", @"\dr.web security space", @"\drweblwf",
            @"\comodo", @"\agnitum", @"\eset\", @"\local\mozilla\firefox", @"\roaming\mozilla\firefox",
            @"\ntuser.dat", @"\system32\catroot", @"\windowsupdate.log",
        };

        // Registry branches the uninstaller's leftover scan must never delete.
        public static readonly string[] RegistryKeys =
        {
            // system state (AppUninstIgnore)
            // (the BCD/COMPONENTS/HARDWARE/SAM/SECURITY hives are never scanned, so they need no entry here)
            @"\microsoft\cryptography", @"\microsoft\reliability analysis", @"\microsoft\rpc", @"\microsoft\schedulingagent",
            @"\wbem\", @"\microsoft\windows defender", @"\windows nt\currentversion\perflib", @"\windows nt\currentversion\prefetcher",
            @"\windows nt\currentversion\profilelist", @"\schedule\taskcache", @"\softwareprotectionplatform", @"\superfetch",
            @"\systemrestore", @"\windows search", @"\component based servicing", @"\currentversion\diagnostics",
            @"\currentversion\windowsupdate", @"\system\controlset00", @"\control\cmf", @"\criticaldevicedatabase",
            @"\control\deviceclasses", @"\control\diagnostics", @"\control\nsi", @"\appcompatcache", @"\appcompatibility",
            @"\prefetchparameters", @"\session manager\power", @"\control\hivelist", @"\currentcontrolset\enum",
            @"\services\vss", @"\services\kmixer", @"\tcpip\parameters\interfaces", @"\lastknowngoodrecovery",
            @"\mounteddevices", @"\system\rng\", @"\currentversion\appx\", @"\appmodel\", @"\control\class\",
            @"\windows nt\currentversion\winlogon\",
            // per-user shell state that looks orphaned but isn't
            @"\local settings\muicache", @"\currentversion\explorer\traynotify", @"\explorer\userassist", @"\explorer\comdlg32",
            @"\shell extensions\cached", @"\currentversion\applets",
            // IgnoreRegCleaner: keys that look dead but belong to working software
            @"\control\lsa", @"\control\crashcontrol", @"\control\backuprestore\filesnottobackup",
            @"\services\sharedaccess\defaults\firewallpolicy", @"\microsoft\speech\recognizers", @"\microsoft\office\",
            @"\microsoft\money", @"\outlook\journal", @"\windows live contacts\", @"\software\far\", @"\winrar\profiles",
            @"\mozilla\", @"\hewlett-packard", @"\software\brother\", @"\acronis\", @"\intel\network_services\",
            @"\infineon\tpmsoftware\", @"\autodesk\inventor\registryversion", @"\business objects\", @"\qualcomm\eudora\",
            // security products
            @"\comodo", @"\kasperskylab", @"\kaspersky lab", @"\eset\", @"\agnitum", @"\doctor web", @"\dr.web",
            @"\microsoft antimalware", @"\watchtower\",
        };


        // Paths deep clean must never delete from, even if a Winapp2 rule matches (CleanupIgnore + browser extensions).
        public static readonly string[] CleanPaths =
        {
            @"\windows\winsxs\", @"chrome-extension", @"moz-extension", @"\extensions\", @"\extension state\",
            @"\temp\fine.ssr", @"\temp\retail.ssr", @"\temp\abbyy\finereader",
        };

        static bool Any(string text, string[] list)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = Norm(text);   // trailing separator added, so "\\wbem\\" also matches the WBEM key itself
            return list.Any(s => t.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static string Norm(string text) { return text.Replace('/', '\\').TrimEnd(new[] { '\\' }) + "\\"; }
        static readonly System.Text.RegularExpressions.Regex DriveBoot =
            new System.Text.RegularExpressions.Regex(@"^[a-z]:\\boot\\", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // \Boot is protected only at a drive root, never an app's own "Boot" subfolder
        public static bool LeftoverBlocked(string path) { return !string.IsNullOrEmpty(path) && (DriveBoot.IsMatch(Norm(path)) || Any(path, LeftoverPaths)); }
        public static bool RegistryBlocked(string keyPath) { return Any(keyPath, RegistryKeys); }
        public static bool CleanBlocked(string path) { return Any(path, CleanPaths); }
    }
}
