// ArchTweaker: back / forward page history (3.40): Alt+Left / Alt+Right, mouse back / forward buttons.
using System;
using System.Collections.Generic;

namespace ArchTweaker
{
    // Pure history: the window tells it each page change; Back / Forward return the page to show (or null).
    public class NavHistory
    {
        public const int Max = 30;
        readonly List<string> back = new List<string>(), forward = new List<string>();
        readonly Func<string, bool> keep;   // pages that never enter the history (search results)

        public NavHistory(Func<string, bool> keep) { this.keep = keep ?? (p => true); }

        public bool CanBack { get { return back.Count > 0; } }
        public bool CanForward { get { return forward.Count > 0; } }

        // A normal page change: `from` goes on the back list, the forward list is cleared.
        public void Visit(string from, string to)
        {
            if (string.IsNullOrEmpty(from) || from == to || !keep(from)) return;
            back.Add(from);
            if (back.Count > Max) back.RemoveAt(0);
            forward.Clear();
        }

        // The pages visited before `current`, most recent first, each once (the command palette lists them first).
        public List<string> Recent(string current, int max)
        {
            var l = new List<string>();
            for (int i = back.Count - 1; i >= 0 && l.Count < max; i--)
                if (back[i] != current && !l.Contains(back[i])) l.Add(back[i]);
            return l;
        }

        public string Back(string current)
        {
            if (back.Count == 0) return null;
            string to = back[back.Count - 1]; back.RemoveAt(back.Count - 1);
            if (!string.IsNullOrEmpty(current) && keep(current)) forward.Add(current);
            return to;
        }

        public string Forward(string current)
        {
            if (forward.Count == 0) return null;
            string to = forward[forward.Count - 1]; forward.RemoveAt(forward.Count - 1);
            if (!string.IsNullOrEmpty(current) && keep(current)) back.Add(current);
            return to;
        }
    }
}
