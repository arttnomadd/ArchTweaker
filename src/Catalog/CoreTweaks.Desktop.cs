// ArchTweaker: core tweak list, part 1 (3.32 split from CoreTweaks.cs): Privacy, Interface, Explorer & Context Menu, System, Gaming.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

#pragma warning disable 219   // each part declares every category name, and uses only its own
namespace ArchTweaker
{
    public static partial class Catalog
    {
        static void AddCoreDesktop()
        {
            string P = Cat.Privacy, I = Cat.Interface, E = Cat.Explorer, S = Cat.System, G = Cat.Gaming, N = Cat.Network, X = DangerZone.CAT;
            string RS = Cat.Responsiveness, TS = Cat.TasksServices, C = Cat.Commands;
            string safe = PRESET_SAFE, game = PRESET_GAMING, priv = PRESET_PRIVACY;

            // ---------------- PRIVACY ----------------
            T("telemetry", P, "Minimize telemetry", "Sets diagnostic data to the lowest level allowed by your edition and disables the Connected User Experiences (DiagTrack) and WAP push services.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1, null)
                .Service("DiagTrack", 4, 2).Service("dmwappushservice", 4, 3)
                .In(safe, priv, game);
            T("adid", P, "Disable advertising ID", "Stops apps from using your advertising ID for personalized ads.")
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1, null)
                .In(safe, priv, game);
            T("activity", P, "Disable activity history / Timeline", "Windows stops collecting and uploading the list of apps and files you used.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0, null)
                .In(safe, priv, game);
            T("tailored", P, "Disable tailored experiences", "No tips, ads or recommendations based on your diagnostic data.")
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1)
                .In(safe, priv, game);
            T("feedback", P, "Never ask for feedback", "Disables Windows feedback pop-ups (SIUF).")
                .Dw(CU, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, null)
                .In(safe, priv, game);
            T("websearch", P, "Disable Bing / web results in Start search", "Start menu searches only your PC — faster and private.")
                .Dw(CU, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", 1, null)
                .NeedsRelogon().In(safe, priv, game);
            T("cortana", P, "Disable Cortana", "Turns Cortana off by policy.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0, null)
                .In(safe, priv, game);
            T("suggestions", P, "Disable ads, suggestions and silent app installs", "Stops Start/Settings/lock-screen suggestions and automatic install of promoted apps (Candy Crush etc).")
                .Dw(CU, CDM, "ContentDeliveryAllowed", 0, 1)
                .Dw(CU, CDM, "SilentInstalledAppsEnabled", 0, 1)
                .Dw(CU, CDM, "SystemPaneSuggestionsEnabled", 0, 1)
                .Dw(CU, CDM, "SoftLandingEnabled", 0, 1)
                .Dw(CU, CDM, "OemPreInstalledAppsEnabled", 0, 1)
                .Dw(CU, CDM, "PreInstalledAppsEnabled", 0, 1)
                .Dw(CU, CDM, "SubscribedContent-338388Enabled", 0, 1)
                .Dw(CU, CDM, "SubscribedContent-338389Enabled", 0, 1)
                .Dw(CU, CDM, "SubscribedContent-353694Enabled", 0, 1)
                .Dw(CU, CDM, "SubscribedContent-353696Enabled", 0, 1)
                .Dw(CU, CDM, "SubscribedContent-310093Enabled", 0, 1)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1, null)
                .In(safe, priv, game);
            T("trackprogs", P, "Don't track app launches", "Disables Start menu app-launch tracking.")
                .Dw(CU, ADV, "Start_TrackProgs", 0, 1).In(priv);
            T("inking", P, "Disable inking & typing personalization", "Windows stops collecting typing and handwriting samples.")
                .Dw(CU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1, 0)
                .Dw(CU, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, 0)
                .Dw(CU, @"Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0, 1)
                .Dw(CU, @"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0, 1)
                .In(safe, priv);
            T("location", P, "Disable location access", "Denies location to all apps system-wide. Leave this off if you use Find My Device or weather by location.")
                .Sz(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny", "Allow")
                .Service("lfsvc", 4, 3).In(priv);
            T("wer", P, "Disable Windows Error Reporting", "Crash reports are no longer sent to Microsoft.")
                .Dw(LM, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1, null)
                .Service("WerSvc", 4, 3).In(safe, priv, game);
            T("ceip", P, "Disable Customer Experience Improvement Program", "Turns off CEIP data collection.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "AITEnable", 0, null)
                .In(safe, priv, game);
            T("copilot", P, "Disable Copilot", "Removes Windows Copilot by policy.")
                .Dw(CU, @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null)
                .W11().In(safe, priv, game);
            T("recall", P, "Disable Recall / AI snapshots", "Stops Windows from saving screen snapshots for Recall and removes the Recall component (Copilot+ PCs). It deletes saved snapshots.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null)
                .Dw(CU, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0, null)   // 3.7: removes the Recall component (24H2 KB5055627+)
                .NeedsReboot().W11().In(safe, priv, game);
            T("edgeprelaunch", P, "Stop Edge pre-launch & background running", "Edge no longer preloads at sign-in or keeps running after you close it.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0, null)
                .In(safe, game);
            // 3.30: the same for Chrome and Brave, through their own policy keys
            T("chromebg", P, "Stop Chrome running in the background", "Chrome closes completely when you close its last window, and stops sending usage statistics. Chrome then shows 'Managed by your organization' in its menu, because these are policies.")
                .Requires(() => ChromiumInstalled(@"Google\Chrome\Application\chrome.exe"), "Google Chrome")
                .Dw(LM, @"SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\Google\Chrome", "MetricsReportingEnabled", 0, null)
                .In(safe, game);
            T("bravebg", P, "Stop Brave running in the background", "Brave closes completely when you close its last window, and stops sending usage statistics. Brave then shows 'Managed by your organization' in its menu, because these are policies.")
                .Requires(() => ChromiumInstalled(@"BraveSoftware\Brave-Browser\Application\brave.exe"), "Brave")
                .Dw(LM, @"SOFTWARE\Policies\BraveSoftware\Brave", "BackgroundModeEnabled", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\BraveSoftware\Brave", "MetricsReportingEnabled", 0, null)
                .In(safe, game);

            // ---------------- INTERFACE ----------------
            T("darkmode", I, "Dark mode (apps + system)", "Switches Windows and apps to the dark theme.")
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0, 1)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0, 1);
            T("transparency", I, "Disable transparency effects", "Slightly reduces GPU/DWM work.")
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, 1);
            T("animations", I, "Faster UI: reduce animations", "Disables minimize/maximize and taskbar animations.")
                .Sz(CU, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", "1")
                .Dw(CU, ADV, "TaskbarAnimations", 0, 1)
                .NeedsRelogon().In(game);
            T("taskleft", I, "Align taskbar to the left", "Classic left-aligned Start button.")
                .Dw(CU, ADV, "TaskbarAl", 0, 1).W11();
            T("taskview", I, "Hide Task View button", "Removes the Task View button from the taskbar.")
                .Dw(CU, ADV, "ShowTaskViewButton", 0, 1).In(safe);
            T("widgets", I, "Disable Widgets / News and Interests", "Removes the widgets board and its background process.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0, null)
                .In(safe, game, priv);
            if (!OSInfo.IsWin11) all[all.Count - 1].Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds", 0, null);   // 3.7: Windows 10 News and Interests only
            T("chaticon", I, "Hide Chat (Teams) icon", "Removes the Chat button from the Windows 11 taskbar.")
                .Dw(CU, ADV, "TaskbarMn", 0, 1).W11().In(safe);
            T("searchbox", I, "Hide taskbar search box", "Search still works by pressing Win and typing.")
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 0, 1);
            T("endtask", I, "Add 'End task' to taskbar right-click", "Kill a frozen app straight from the taskbar.")
                .Dw(CU, ADV + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1, null).W11().In(safe, game);
            T("seconds", I, "Show seconds in taskbar clock", "Useful for timing. Costs a tiny bit of CPU.")
                .Dw(CU, ADV, "ShowSecondsInSystemClock", 1, null);
            T("lockscreen", I, "Skip the lock screen", "Go straight to the sign-in prompt.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", 1, null);
            T("stickykeys", I, "Disable Sticky Keys shortcut (Shift ×5)", "No more Sticky Keys prompt mid-game.")
                .Sz(CU, @"Control Panel\Accessibility\StickyKeys", "Flags", "506", "510")
                .Sz(CU, @"Control Panel\Accessibility\ToggleKeys", "Flags", "58", "62")
                .Sz(CU, @"Control Panel\Accessibility\Keyboard Response", "Flags", "122", "126")
                .In(safe, game);
            T("verbose", I, "Verbose sign-in / shutdown status", "Shows what Windows is doing during boot and shutdown.")
                .Dw(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", 1, null);
            T("startrec", I, "Start & Quick Access: no recent files", "Windows stops listing recently opened files and frequent folders in Start, Jump Lists and Quick Access. To hide the whole Recommended section of Start, use 'Start: hide the Recommended section'.")
                .Dw(CU, ADV, "Start_TrackDocs", 0, 1)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 0, 1)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent", 0, 1)
                .In(priv);

            // ---------------- EXPLORER & CONTEXT MENU ----------------
            T("ext", E, "Show file extensions", "Always see .exe, .jpg and other extensions. This protects against fake 'photo.jpg.exe' files.")
                .Dw(CU, ADV, "HideFileExt", 0, 1).In(safe, game, priv);
            T("hidden", E, "Show hidden files", "Shows hidden files and folders.")
                .Dw(CU, ADV, "Hidden", 1, 2);
            T("thispc", E, "Open Explorer to 'This PC'", "Instead of Home/Quick Access.")
                .Dw(CU, ADV, "LaunchTo", 1, null).In(safe);
            T("classicmenu", E, "Classic right-click menu (no 'Show more options')", "Restores the full Windows 10 style context menu on Windows 11.")
                .Key(CU, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}")
                .Sz(CU, @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", "", null)
                .W11().NeedsRelogon().In(safe, game);
            T("3dobjects", E, "Remove '3D Objects' from This PC", "Hides the useless 3D Objects folder (Windows 10).")
                .DelKey(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{0DB7E03F-FC29-4DC6-9020-FF41B59E513A}")
                .DelKey(LM, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{0DB7E03F-FC29-4DC6-9020-FF41B59E513A}")
                .W10().In(safe);
            T("shortcutsuffix", E, "No '- Shortcut' suffix on new shortcuts", "New shortcuts keep the original name.")
                .Sz(CU, @"Software\Microsoft\Windows\CurrentVersion\Explorer\NamingTemplates", "ShortcutNameTemplate", "%s.lnk", null).In(safe);
            T("takeown", E, "Add 'Take ownership' to right-click menu", "Grants you full control over a file or folder in one click (admin).")
                .Sz(LM, @"SOFTWARE\Classes\*\shell\ArchTakeOwn", "", "Take ownership", null)
                .Sz(LM, @"SOFTWARE\Classes\*\shell\ArchTakeOwn", "HasLUAShield", "", null)
                .Sz(LM, @"SOFTWARE\Classes\*\shell\ArchTakeOwn", "NoWorkingDirectory", "", null)
                .Sz(LM, @"SOFTWARE\Classes\*\shell\ArchTakeOwn\command", "", "cmd.exe /c takeown /f \"%1\" && icacls \"%1\" /grant *S-1-3-4:F /c /l", null)
                .Sz(LM, @"SOFTWARE\Classes\*\shell\ArchTakeOwn\command", "IsolatedCommand", "cmd.exe /c takeown /f \"%1\" && icacls \"%1\" /grant *S-1-3-4:F /c /l", null)
                .Sz(LM, @"SOFTWARE\Classes\Directory\shell\ArchTakeOwn", "", "Take ownership", null)
                .Sz(LM, @"SOFTWARE\Classes\Directory\shell\ArchTakeOwn", "HasLUAShield", "", null)
                .Sz(LM, @"SOFTWARE\Classes\Directory\shell\ArchTakeOwn", "NoWorkingDirectory", "", null)
                .Sz(LM, @"SOFTWARE\Classes\Directory\shell\ArchTakeOwn\command", "", "cmd.exe /c takeown /f \"%1\" /r /d y && icacls \"%1\" /grant *S-1-3-4:F /t /c /l /q", null)
                .Sz(LM, @"SOFTWARE\Classes\Directory\shell\ArchTakeOwn\command", "IsolatedCommand", "cmd.exe /c takeown /f \"%1\" /r /d y && icacls \"%1\" /grant *S-1-3-4:F /t /c /l /q", null)
                .OnRevert(() => { Reg.DeleteTreeRaw(LM, @"SOFTWARE\Classes\*\shell\ArchTakeOwn"); Reg.DeleteTreeRaw(LM, @"SOFTWARE\Classes\Directory\shell\ArchTakeOwn"); });
            T("hashmenu", E, "Add 'SHA-256 hash' to right-click menu", "Check a download's checksum instantly. (Replaces the original's VirusTotal uploader — paste the hash at virustotal.com to look it up without uploading.)")
                .Sz(LM, @"SOFTWARE\Classes\*\shell\ArchHash", "MUIVerb", "SHA-256 hash", null)
                .Sz(LM, @"SOFTWARE\Classes\*\shell\ArchHash", "Icon", "imageres.dll,-5340", null)
                .Sz(LM, @"SOFTWARE\Classes\*\shell\ArchHash\command", "", "cmd.exe /d /k certutil -hashfile \"%1\" SHA256", null)   // 3.2.1: a file name can no longer inject PowerShell
                .OnRevert(() => Reg.DeleteTreeRaw(LM, @"SOFTWARE\Classes\*\shell\ArchHash"));
            T("restartexplorer", E, "Add 'Restart Explorer' to desktop right-click", "Quick fix for a stuck taskbar.")
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchRestartExplorer", "MUIVerb", "Restart Explorer", null)
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchRestartExplorer", "Icon", "explorer.exe", null)
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchRestartExplorer", "Position", "Bottom", null)
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchRestartExplorer\command", "", "cmd.exe /c taskkill /f /im explorer.exe & start explorer.exe", null)
                .OnRevert(() => Reg.DeleteTreeRaw(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchRestartExplorer"));
            T("killnr", E, "Add 'Kill not-responding apps' to desktop right-click", "Closes every frozen program in one click.")
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchKillNR", "MUIVerb", "Kill not-responding apps", null)
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchKillNR", "Icon", "taskmgr.exe,-30651", null)
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchKillNR", "Position", "Bottom", null)
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchKillNR\command", "", "taskkill.exe /f /fi \"status eq NOT RESPONDING\"", null)
                .OnRevert(() => Reg.DeleteTreeRaw(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchKillNR"));
            T("rebootmenu", E, "Add 'Reboot to BIOS/UEFI' to desktop right-click", "Restarts straight into firmware setup — handy for XMP/EXPO and overclock changes.")
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchUefi", "MUIVerb", "Reboot to BIOS/UEFI", null)
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchUefi", "Icon", "shell32.dll,-16739", null)
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchUefi", "Position", "Bottom", null)
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchUefi", "HasLUAShield", "", null)
                .Sz(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchUefi\command", "", "shutdown.exe /r /fw /t 0", null)
                .OnRevert(() => Reg.DeleteTreeRaw(LM, @"SOFTWARE\Classes\DesktopBackground\Shell\ArchUefi"));
            T("noshare", E, "Remove 'Share' and 'Give access to' from menus", "Declutters the right-click menu.")
                .Sz(LM, BLOCKED, "{e2bf9676-5f8f-435c-97eb-11607a5bedf7}", "", null)
                .Sz(LM, BLOCKED, "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}", "", null);
            T("nocast", E, "Remove 'Cast to Device' from menus", "Declutters the right-click menu.")
                .Sz(LM, BLOCKED, "{7AD84985-87B4-4a16-BE58-8B72A5B390F7}", "", null).In(safe);
            T("noprevver", E, "Remove 'Restore previous versions' from menus", "Declutters the right-click menu and Properties.")
                .Sz(LM, BLOCKED, "{596AB062-B4D2-4215-9F74-E9109B0A8153}", "", null);
            T("longpaths", E, "Enable long file paths (>260 chars)", "Lets apps that support it use paths longer than 260 characters.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1, 0).In(safe, game);

            // ---------------- SYSTEM ----------------
            T("hibernate", S, "Disable hibernation", "Deletes hiberfil.sys (frees GBs equal to ~40% of RAM). Also disables Fast Startup.")
                .DetectBy(() => { object o = Reg.Get(LM, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled"); return o is int && (int)o == 0; })
                .Explains("Runs: powercfg /hibernate off   ·   undo: powercfg /hibernate on")
                .OnApply(() => Shell.Run("powercfg.exe", "/hibernate off", true))
                .OnRevert(() => Shell.Run("powercfg.exe", "/hibernate on", true));
            T("faststartup", S, "Disable Fast Startup", "Gives a true cold boot every time — fixes many driver and dual-boot issues.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0, 1).In(safe, game);
            T("dop2p", S, "Disable Delivery Optimization P2P upload", "Your PC stops seeding Windows updates to other PCs on the internet.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0, null).In(safe, game, priv);
            T("nodrivers", S, "Don't install drivers through Windows Update", "Prevents WU from replacing your GPU/chipset drivers with older versions.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1, null)
                .Dw(LM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0, 1).In(game);
            T("noautoreboot", S, "No forced restarts for updates while signed in", "Windows waits for you instead of restarting mid-session.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1, null).In(safe, game);
            T("wunotify", S, "Windows Update: notify before download", "Updates are no longer downloaded and installed automatically. You install them when you choose.")
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions", 2, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", 0, null)
                .R(Risk.Moderate);
            T("lastaccess", S, "Disable NTFS last-access timestamps", "Fewer disk writes on every file read.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", unchecked((int)0x80000001), unchecked((int)0x80000002)).In(game);
            T("8dot3", S, "Disable 8.3 short-name creation", "Speeds up folders with many files. Affects new files only.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation", 1, 2);
            T("fastshutdown", S, "Faster shutdown", "Shortens the wait for hung apps and services at shutdown.")
                .Sz(LM, @"SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000", "5000")
                .Sz(CU, @"Control Panel\Desktop", "HungAppTimeout", "2000", null)
                .Sz(CU, @"Control Panel\Desktop", "WaitToKillAppTimeout", "2000", null)
                .In(safe, game);
            T("sysmain", S, "Disable SysMain (Superfetch)", "Mostly useless on NVMe/SSD; reduces background disk activity.")
                .Service("SysMain", 4, 2).R(Risk.Moderate).In(game);
            T("wsearch", S, "Disable Windows Search indexing", "Stops background indexing. Start search still works, but searching file contents is slower.")
                .Service("WSearch", 4, 2, true).R(Risk.Moderate);
            T("reservedstorage", S, "Disable Reserved Storage", "Frees ~7 GB that Windows keeps for updates.")
                .Explains("Runs: dism /Online /Set-ReservedStorageState /State:Disabled   ·   undo: /State:Enabled")
                .OnApply(() => Shell.Run("dism.exe", "/Online /Set-ReservedStorageState /State:Disabled /NoRestart", true))
                .OnRevert(() => Shell.Run("dism.exe", "/Online /Set-ReservedStorageState /State:Enabled /NoRestart", true));
            T("storagesense", S, "Disable Storage Sense auto-cleanup", "Windows stops deleting Downloads/Recycle Bin content on its own.")
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy", "01", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\StorageSense", "AllowStorageSenseGlobal", 0, null);

            // ---------------- GAMING ----------------
            T("gamemode", G, "Enable Game Mode", "Windows prioritises the game and blocks Windows Update installs and driver installs while you play.")
                .Dw(CU, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, null)
                .Dw(CU, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1, null)
                .In(game);
            T("gamedvr", G, "Disable Game DVR background recording", "Stops Xbox Game Bar from constantly recording in the background (costs FPS and VRAM).")
                .Dw(CU, GCS, "GameDVR_Enabled", 0, 1)
                .Dw(CU, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, null)
                .Dw(LM, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, null)
                .In(game, safe);
            T("gamebar", G, "Disable Xbox Game Bar overlay", "Win+G does nothing; removes overlay hooks. Leave on if you use Game Bar widgets.")
                .Dw(CU, @"Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0, null)
                .Dw(CU, @"Software\Microsoft\GameBar", "ShowStartupPanel", 0, null)
                .Dw(CU, @"Software\Microsoft\GameBar", "GamePanelStartupTipIndex", 3, null)
                .Dw(CU, @"Software\Microsoft\GameBar", "GamepadNexusChordEnabled", 0, null)
                .R(Risk.Moderate);
            T("hags", G, "Enable Hardware-accelerated GPU scheduling (HAGS)", "Lower latency on modern GPUs (NVIDIA 10xx+/AMD 5000+). Necessary for DLSS Frame Generation. Needs a restart.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, 1)
                .NeedsReboot().In(game);
            T("windowedopt", G, "Optimizations for windowed games", "Upgrades DX10/11 games running windowed or borderless to the flip presentation model: lower input latency, and VRR and Auto HDR work outside exclusive fullscreen.")
                .Info("Edits only the SwapEffectUpgradeEnable token in DirectXUserGlobalSettings. Your other DirectX toggles (VRR, Auto HDR) stay as they are.")
                .Dx("SwapEffectUpgradeEnable", "1")
                .W11().In(game);
            T("fso", G, "Disable fullscreen optimizations globally", "Forces true exclusive fullscreen in older games. On Windows 11 FSO usually helps — test before keeping.")
                .Dw(CU, GCS, "GameDVR_FSEBehaviorMode", 2, 0)
                .Dw(CU, GCS, "GameDVR_HonorUserFSEBehaviorMode", 1, 0)
                .Dw(CU, GCS, "GameDVR_FSEBehavior", 2, null)
                .Dw(CU, GCS, "GameDVR_DXGIHonorFSEWindowsCompatible", 1, 0)
                .R(Risk.Moderate);
            T("mmcss", G, "MMCSS: prioritise games", "Raises the scheduler, GPU and I/O priority for games (Multimedia Class Scheduler) and reserves less CPU for background tasks.")
                .Dw(LM, MMSP, "SystemResponsiveness", 10, 20)
                .Dw(LM, MMSP + @"\Tasks\Games", "GPU Priority", 8, 8)
                .Dw(LM, MMSP + @"\Tasks\Games", "Priority", 6, 2)
                .Sz(LM, MMSP + @"\Tasks\Games", "Scheduling Category", "High", "Medium")
                .Sz(LM, MMSP + @"\Tasks\Games", "SFIO Priority", "High", "Normal")
                .In(game);
            T("netthrottle", G, "Disable network throttling", "Removes the MMCSS packet-rate limit (NetworkThrottlingIndex). It only matters while multimedia playback is active. Independent tests (djdallmann/GamingPCSetup) measured higher NDIS DPC latency with it disabled, so it is not in any preset. Test it yourself.")
                .Dw(LM, MMSP, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), 10).R(Risk.Moderate);
            T("prioritysep", G, "Foreground boost (Win32PrioritySeparation 0x26)", "Short, variable quanta with maximum foreground boost — the focused game gets more CPU time.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 0x26, 2).In(game);
            T("powerthrottle", G, "Disable Power Throttling", "Stops Windows from parking background work on efficient/slow cores. Desktop recommended; costs battery on laptops.")
                .Dw(LM, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, null).In(game);
            T("mouseaccel", G, "Disable mouse acceleration", "Turns off 'Enhance pointer precision' for 1:1 aim. Applies immediately.")
                .Sz(CU, @"Control Panel\Mouse", "MouseSpeed", "0", "1")
                .Sz(CU, @"Control Panel\Mouse", "MouseThreshold1", "0", "6")
                .Sz(CU, @"Control Panel\Mouse", "MouseThreshold2", "0", "10")
                .Explains("Also calls SystemParametersInfo(SPI_SETMOUSE) so the change is live without signing out.")
                .OnApply(() => Native.SetMouseAccel(false))
                .OnRevert(() => Native.SetMouseAccel(true))
                .In(game);
            T("mpo", G, "Disable Multiplane Overlay (MPO)", "NVIDIA's own fix for flicker or stutter on some multi-monitor and mixed-refresh-rate setups. Use it only if you have those problems. With NVIDIA R610+ drivers on Windows build 26100.7705 / 26200.7705 or newer, MPO works on most setups. Turning it off can then make things worse.")
                .Dw(LM, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5, null)
                .NeedsReboot().R(Risk.Moderate);
            T("nagle", G, "Disable Nagle's algorithm (TCP no-delay)", "Sends small TCP packets immediately. Helps some older TCP-based games. Most modern games use UDP, so it has no effect on them.")
                .Explains("For every active network interface under HKLM\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\{id}: TcpAckFrequency = 1, TCPNoDelay = 1   ·   undo: both values deleted")
                .DetectBy(() => Nagle.IsDisabled())
                .OnApply(() => Nagle.Set(true))
                .OnRevert(() => Nagle.Set(false))
                .R(Risk.Moderate);
        }
    }
}
