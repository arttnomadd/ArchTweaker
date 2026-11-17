// ArchTweaker: the "Why ArchTweaker" panel (3.34). The five things that set it apart, stated as plain facts, no rival named.
// The live proof count comes from ProofStore so the first line reflects this PC.
namespace ArchTweaker
{
    public static class WhyArchTweaker
    {
        public static void Show()
        {
            int proven = 0;
            try { proven = ProofStore.Count; } catch { }
            string measured = proven > 0
                ? L.F("Measured, not promised. You have proven {0} tweak(s) on this PC with your own numbers. A/B test any other tweak the same way.", proven)
                : L.T("Measured, not promised. A/B test any tweak on this PC. With a game it compares 1 % low FPS. Without one, it uses the timer wake-up delay. ArchTweaker keeps only a measured, significant win.");
            string body = string.Join("\r\n\r\n", new[]
            {
                measured,
                L.T("Exactly undoable. ArchTweaker saves the original value before it changes anything. 'Undo everything' restores exactly what you had. Change history lists every change."),
                L.T("Works offline, no account. ArchTweaker downloads nothing to decide what to do and sends nothing anywhere. You can see the whole tweak list and export it to .reg or .cmd."),
                L.F("Small and self-contained. One {0} program. No browser engine, and no background service runs while it tunes your games.", ExeSize()),
                L.T("Open about risk. Each tweak shows its risk, what it turns off, what it can break and where the evidence comes from, before you apply it."),
            });
            Dlg.Info(L.T("Why ArchTweaker"), body);
        }

        // 3.35: the real size of this exe, so the text can't go out of date (3.34 said 2.7 MB; the exe was 4.1 MB)
        public static string ExeSize()
        {
            try { return Units.Bytes(new System.IO.FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).Length); }
            catch { return "4 MB"; }
        }
    }
}
