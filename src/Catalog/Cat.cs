// 3.35.1: the tweak category names in one place. A category is also a sidebar page name and a key in the
// subtitle, glyph and gain dictionaries; a typo in a copy made a tweak with no page (a test caught it only after the
// fact). Use Cat.X in code; L.T("...") calls keep the literal text, so tests/L10nTests can find them.
namespace ArchTweaker
{
    public static class Cat
    {
        public const string Privacy = "Privacy";
        public const string Interface = "Interface";
        public const string Explorer = "Explorer & Context Menu";
        public const string System = "System";
        public const string Responsiveness = "Responsiveness";
        public const string Gaming = "Gaming";
        public const string PowerPlan = "Power Plan";
        public const string Input = "Input & Latency";
        public const string Gpu = "GPU & Devices";
        public const string Network = "Internet & Network";
        public const string Hardening = "Hardening";
        public const string TasksServices = "Tasks & Services";
        public const string Commands = "Command tweaks";
        public const string Danger = "Danger zone";
    }
}
