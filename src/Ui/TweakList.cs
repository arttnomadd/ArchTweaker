// The tweak list: one owner-drawn row per tweak, and the page that lists a category.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{

    // ================= TWEAK PAGE =================
    // One tweak, drawn as a single control (2.6). Before: a Panel with ~11 child controls (own window handle each);
    // pages with 20+ rows and the all-pages search created thousands of handles and flickered on theme changes.
    // Layout is in S() units, so the row ignores Control.Scale (IDpiSelfScaled) and re-lays itself out on resize.
    public class TweakRow : Control, Theme.IDpiSelfScaled
    {
        public readonly Tweak T;
        bool? state;            // null = still being read in the background
        bool shown;             // what the switch shows: pending ?? state
        float knob;             // 3.3: switch position while it slides (0 off … 1 on)
        bool available = true, hover, hoverSwitch;
        readonly List<KeyValuePair<string, Color>> chips = new List<KeyValuePair<string, Color>>();
        Rectangle swR, titleR, descR, meterR, wordR, gainR, breakR, offR; readonly List<Rectangle> chipR = new List<Rectangle>();
        List<string> titleL = new List<string>(), descL = new List<string>(), gainL = new List<string>(), breakL = new List<string>(), offL = new List<string>();
        string[] hl = new string[0];

        // 2.8: search words are highlighted in the title, description and gain text
        public void SetHighlight(string q)
        {
            var w = (q ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length >= 2).ToArray();
            if (string.Join("\u0001", w) == string.Join("\u0001", hl)) return;
            bool was = Expanded;
            hl = w;
            if (Expanded != was) Layout2();
            Invalidate();
        }

        // compact mode: only the selected row (and every row while searching) shows description and gain
        static TweakRow selected;
        // 3.4: Danger zone rows always show what they break, even in compact view
        bool Expanded { get { return !Theme.Compact || selected == this || hl.Length > 0 || T.Dangerous; } }
        void MarkSelected()
        {
            if (selected == this) return;
            var old = selected; selected = this;
            if (Theme.Compact)
            {
                if (old != null && !old.IsDisposed) { old.Layout2(); old.Invalidate(); }
                Layout2(); Invalidate();
            }
        }

        const TextFormatFlags Line = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        // word wrap done here (not by DrawText) so each line's text is known and matches can be located exactly
        static List<string> Wrap(string text, Font f, int width)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            foreach (var para in text.Replace("\r", "").Split('\n'))
            {
                string cur = "";
                foreach (var word in para.Split(' '))
                {
                    string next = cur.Length == 0 ? word : cur + " " + word;
                    if (cur.Length > 0 && TextRenderer.MeasureText(next, f, Size.Empty, Line).Width > width) { lines.Add(cur); cur = word; }
                    else cur = next;
                }
                lines.Add(cur);
            }
            return lines;
        }
        void DrawLines(Graphics g, List<string> lines, Font f, Rectangle r, Color fg, Color bg)
        {
            int lh = f.Height, y = r.Y;
            Color mark = Gfx.Mix(bg, Theme.Accent, 0.38);
            foreach (var l in lines)
            {
                foreach (var w in hl)
                    for (int i = l.IndexOf(w, StringComparison.OrdinalIgnoreCase); i >= 0; i = l.IndexOf(w, i + w.Length, StringComparison.OrdinalIgnoreCase))
                    {
                        int x0 = i == 0 ? 0 : TextRenderer.MeasureText(g, l.Substring(0, i), f, Size.Empty, Line).Width;
                        int wd = TextRenderer.MeasureText(g, l.Substring(i, w.Length), f, Size.Empty, Line).Width;
                        using (var b = new SolidBrush(mark)) g.FillRectangle(b, r.X + x0, y, wd, lh);
                    }
                TextRenderer.DrawText(g, l, f, new Rectangle(r.X, y, r.Width, lh), fg, Line);
                y += lh;
            }
        }
        string titleText;
        // 3.44: why the tweak can't apply here, on its own muted line under the title (it used to be glued to the title)
        string noteText; List<string> noteL = new List<string>(); Rectangle noteR;

        public TweakRow(Tweak t)
        {
            T = t; titleText = L.T(t.Title);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true; Cursor = Cursors.Hand; Margin = new Padding(0, 0, 0, Theme.S(6)); Height = Theme.S(80);
            if (t.Dangerous) { chips.Add(new KeyValuePair<string, Color>(L.T("Danger"), Theme.Risk)); DangerZone.Changed += OnDangerChanged; }
            else if (t.Risk == Risk.Moderate) chips.Add(new KeyValuePair<string, Color>(L.T("Moderate risk"), Theme.Warn));
            else if (t.Risk == Risk.Advanced) chips.Add(new KeyValuePair<string, Color>(L.T("Advanced"), Theme.Risk));
            // 3.33: a row shows only what changes the decision: risk, experimental, a wrong edition, covered, no undo,
            // restart. PRO, documented / undocumented, evidence and "restores defaults" are in the details pane.
            if (t.Experimental) chips.Add(new KeyValuePair<string, Color>(L.T("Experimental"), Theme.Exp));   // 3.6; 3.29: purple
            if ((t.EditionCode == "pro" || t.EditionCode == "ent") && !t.EditionOk)
                chips.Add(new KeyValuePair<string, Color>(L.T(t.EditionCode == "ent" ? "Enterprise / Education" : "Pro / Enterprise"), Theme.Warn));
            if (Catalog.CoveringNow(t, x => StateCache.Get(x) == true) != null) chips.Add(new KeyValuePair<string, Color>(L.T("Covered"), Theme.Muted));
            if (t.UndoNote == "permanent") chips.Add(new KeyValuePair<string, Color>(L.T("No undo"), Theme.Warn));
            if (t.Reboot) chips.Add(new KeyValuePair<string, Color>(L.T("Restart"), Theme.Muted));
            else if (t.Relogon) chips.Add(new KeyValuePair<string, Color>(L.T("Sign-out"), Theme.Muted));
            // 3.34: a green "Proven" chip when an A/B test / Tuning assistant measured a real win on this PC; a stale one asks for a re-test
            var proof = ProofStore.Get(t.Id);
            if (proof != null)
            {
                string pw; var pv = proof.Value;
                // 3.39: no gain and worse show on the row too, not only in the details
                switch (ProofStore.KindOf(pv, pv.Stale(out pw)))
                {
                    case ProofKind.Stale: chips.Insert(0, new KeyValuePair<string, Color>(L.T("Re-test"), Theme.Muted)); break;
                    case ProofKind.Proven: chips.Insert(0, new KeyValuePair<string, Color>(L.T("Proven") + " " + pv.Short(), Theme.Ok)); break;
                    case ProofKind.NoGain: chips.Insert(0, new KeyValuePair<string, Color>(L.T("No gain here"), Theme.Muted)); break;
                    case ProofKind.Worse: chips.Insert(0, new KeyValuePair<string, Color>(L.T("Worse here") + " " + pv.Short(), Theme.Bad)); break;
                }
            }
            AccessibleName = L.T(t.Title); AccessibleDescription = A11yDesc(t); AccessibleRole = AccessibleRole.CheckButton;
        }
        public bool Checked { get { return shown; } }
        // 3.4: Danger zone rows can be read but not switched until the zone is unlocked for this session
        bool Locked { get { return T.Dangerous && !DangerZone.Unlocked; } }
        void OnDangerChanged() { if (IsDisposed) return; if (InvokeRequired) BeginInvoke((Action)Invalidate); else Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) { Waiting(false); if (T.Dangerous) DangerZone.Changed -= OnDangerChanged; } base.Dispose(disposing); }

        // 3.44: rows whose state is still being read repaint a few times a second, so their switch dots pulse
        static readonly HashSet<TweakRow> waiting = new HashSet<TweakRow>();
        static System.Windows.Forms.Timer pulse;
        void Waiting(bool on)
        {
            if (on && Motion.On) waiting.Add(this); else waiting.Remove(this);
            if (waiting.Count > 0 && pulse == null)
            {
                pulse = new System.Windows.Forms.Timer { Interval = 90 };
                pulse.Tick += delegate { foreach (var r in waiting.ToList()) if (r.Visible && !r.IsDisposed) r.Invalidate(r.swR); };
                pulse.Start();
            }
            else if (waiting.Count == 0 && pulse != null) { pulse.Stop(); pulse.Dispose(); pulse = null; }
        }
        // 3.2: "Recommended" badge for the PC advisor's picks that are not applied yet
        bool recommended;
        public void SetRecommended(bool on)
        {
            if (on == recommended) return;
            recommended = on;
            string label = L.T("Recommended");
            if (on) chips.Insert(0, new KeyValuePair<string, Color>(label, Theme.On));
            else chips.RemoveAll(c => c.Key == label);
            AccessibleDescription = (on ? L.T("Recommended for this PC.") + " " : "") + A11yDesc(T);
            Layout2(); Invalidate();
        }
        public bool Known { get { return state.HasValue; } }
        // 3.11: the amber rail and tint are colour only, so a "Pending" chip says it in words too
        bool pendChip;
        void SetPendingChip(bool on)
        {
            if (on == pendChip || IsDisposed) return;
            pendChip = on;
            string label = L.T("Pending");
            if (on) chips.Insert(0, new KeyValuePair<string, Color>(label, Theme.Warn));
            else chips.RemoveAll(c => c.Key == label);
            Layout2(); Invalidate();
        }

        static string A11yDesc(Tweak t)
        {
            string s = L.T(t.Desc) + " " + L.T(ImpactMeter.Word(t.Impact)) + ".";
            if (t.Dangerous) s += " Danger zone. Can break: " + L.T(t.Breaks ?? "") + (DangerZone.Unlocked ? "" : " Locked until the Danger zone is unlocked.");
            else if (t.Risk == Risk.Moderate) s += " Moderate risk."; else if (t.Risk == Risk.Advanced) s += " Advanced.";
            if (!t.Dangerous && !string.IsNullOrEmpty(t.Breaks)) s += " Can break: " + L.T(t.Breaks);
            if (!string.IsNullOrEmpty(t.Off)) s += " Turns off: " + L.T(t.Off);
            if (t.Experimental) s += " Experimental.";
            if (t.Reboot) s += " Needs a restart."; else if (t.Relogon) s += " Needs sign-out.";
            return s;
        }
        public void MarkUnavailable(string reason)
        {
            available = false; titleText = L.T(T.Title); noteText = L.T(reason);
            AccessibleDescription = "Unavailable: " + reason + ". " + A11yDesc(T); Cursor = Cursors.Default; Layout2(); Invalidate();
        }
        // applied == null: state not read yet (switch shows "checking")
        public void Load(bool? applied, bool? pending)
        {
            if (applied.HasValue || !state.HasValue) state = applied;   // a re-read keeps showing the last known state
            shown = pending ?? state ?? false;
            Waiting(available && !state.HasValue);
            SetPendingChip(pending.HasValue);
            Invalidate(); AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
        }
        void Flip()
        {
            if (!available || !state.HasValue) { AppShell.Current.ShowDetails(T); return; }
            if (Locked && !DangerZone.Ensure(FindForm())) { AppShell.Current.ShowDetails(T); return; }
            shown = !shown;
            Motion.Slide(this, () => knob, v => knob = v, shown, Rectangle.Inflate(swR, Theme.S(4), Theme.S(4)));
            MainForm.Instance.SetPending(T, shown);
            SetPendingChip(AppShell.Current.Pending.ContainsKey(T));
            Invalidate(); AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            AppShell.Current.ShowDetails(T);
        }

        // ---- layout (96-dpi units through Theme.S)
        protected override void OnResize(EventArgs e) { Layout2(); base.OnResize(e); }
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified) { }   // self-scaled
        void Layout2()
        {
            if (Width <= 0) return;
            int x = Theme.S(70), right = Theme.S(14) + Theme.S(20), top = Theme.S(12);   // 3.16: room for the expand chevron
            swR = new Rectangle(Theme.S(16), Theme.S(11), Theme.S(44), Theme.S(26));
            chipR.Clear();
            int cx = Width - right;
            var tmp = new Rectangle[chips.Count];
            for (int i = chips.Count - 1; i >= 0; i--)
            {
                var sz = Chip.Measure(chips[i].Key); cx -= sz.Width;
                tmp[i] = new Rectangle(cx, top, sz.Width, sz.Height); cx -= Theme.S(6);
            }
            chipR.AddRange(tmp);
            var wf = Theme.FT;
            var ws = TextRenderer.MeasureText(L.T(ImpactMeter.Word(T.Impact)), wf, Size.Empty, TextFormatFlags.NoPrefix);
            if (!Expanded)
            {
                // compact (2.9): one line — title, then gain meter and chips at the right; details on selection
                cx -= ws.Width + Theme.S(24);
                meterR = new Rectangle(cx, top + Theme.S(2), Theme.S(18), Theme.S(14));
                wordR = new Rectangle(cx + Theme.S(24), top, ws.Width, ws.Height);
                cx -= Theme.S(14);
                int tW = Math.Max(Theme.S(120), cx - x - Theme.S(8));
                titleL = Wrap(titleText, Theme.FB, tW);
                int tw0 = titleL.Count == 0 ? 0 : titleL.Max(l => TextRenderer.MeasureText(l, Theme.FB, Size.Empty, Line).Width);
                titleR = new Rectangle(x, top, Math.Min(tW, tw0 + Theme.S(2)), Math.Max(1, titleL.Count) * Theme.FB.Height);
                PlaceNote(x, tW);
                descL = new List<string>(); gainL = new List<string>(); breakL = new List<string>(); offL = new List<string>(); descR = gainR = breakR = offR = Rectangle.Empty;
                int hc = Math.Max((noteL.Count > 0 ? noteR.Bottom : titleR.Bottom) + Theme.S(12), swR.Bottom + Theme.S(11));
                if (Height != hc) Height = hc;
                return;
            }
            int titleW = Math.Max(Theme.S(120), cx - x - Theme.S(8));
            titleL = Wrap(titleText, Theme.FB, titleW);
            int tw = titleL.Count == 0 ? 0 : titleL.Max(l => TextRenderer.MeasureText(l, Theme.FB, Size.Empty, Line).Width);
            titleR = new Rectangle(x, top, Math.Min(titleW, tw + Theme.S(2)), Math.Max(1, titleL.Count) * Theme.FB.Height);
            int textW = Math.Max(Theme.S(120), Width - x - right - Theme.S(6));
            descL = Wrap(L.T(T.Desc), Theme.FS, textW);
            PlaceNote(x, textW);
            descR = new Rectangle(x, (noteL.Count > 0 ? noteR.Bottom : titleR.Bottom) + Theme.S(4), textW, descL.Count * Theme.FS.Height + Theme.S(2));
            int gy = descR.Bottom + Theme.S(6);
            // 3.6: what it switches off (neutral), then what it can break (a consequence, coloured by risk)
            offL = string.IsNullOrEmpty(T.Off) ? new List<string>() : Wrap(L.T("Turns off:") + " " + L.T(T.Off), Theme.FS, textW - Theme.S(14));
            if (offL.Count > 0) { offR = new Rectangle(x + Theme.S(14), gy, textW - Theme.S(14), offL.Count * Theme.FS.Height + Theme.S(2)); gy = offR.Bottom + Theme.S(4); }
            else offR = Rectangle.Empty;
            breakL = string.IsNullOrEmpty(T.Breaks) ? new List<string>() : Wrap(L.T("Can break:") + " " + L.T(T.Breaks), Theme.FS, textW - Theme.S(14));
            if (breakL.Count > 0) { breakR = new Rectangle(x + Theme.S(14), gy, textW - Theme.S(14), breakL.Count * Theme.FS.Height + Theme.S(2)); gy = breakR.Bottom + Theme.S(6); }
            else breakR = Rectangle.Empty;
            meterR = new Rectangle(x, gy + Theme.S(1), Theme.S(18), Theme.S(14));
            wordR = new Rectangle(x + Theme.S(24), gy - Theme.S(1), ws.Width, ws.Height);
            int gx = wordR.Right + Theme.S(8);
            string gain = T.Impact > 0 ? L.T(T.Gain) : "";
            int gw = Math.Max(Theme.S(80), Width - gx - right);
            gainL = Wrap(gain, Theme.FS, gw);
            gainR = new Rectangle(gx, gy - Theme.S(1), gw, Math.Max(ws.Height, gainL.Count * Theme.FS.Height) + Theme.S(2));
            int h = Math.Max(Math.Max(wordR.Bottom, gainR.Bottom) + Theme.S(12), swR.Bottom + Theme.S(11));
            if (Height != h) Height = h;
        }

        void PlaceNote(int x, int w)
        {
            noteL = string.IsNullOrEmpty(noteText) ? new List<string>() : Wrap(noteText, Theme.FS, w);
            noteR = noteL.Count == 0 ? Rectangle.Empty : new Rectangle(x, titleR.Bottom + Theme.S(2), w, noteL.Count * Theme.FS.Height + Theme.S(2));
        }

        // ---- paint
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            bool pend = MainForm.Instance != null && AppShell.Current.Pending.ContainsKey(T);
            Color bg = pend ? Theme.PendingBg : hover ? Theme.PanelHover : Theme.Raised;
            // 3.36: a raised surface per row. The state lives in the switch, the title weight and the chips; a pending
            // change also gets an amber hairline round the row (no coloured edge stripe).
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Gfx.Surface(g, new RectangleF(0, 0, Width - 1, Height - Theme.S(3)), bg, true, pend ? (Color?)Gfx.Mix(Theme.Warn, bg, 0.35) : null);
            if (!Motion.Running(this)) knob = shown ? 1f : 0f;   // a re-read or Apply snaps it; only a click slides
            Toggle.Draw(g, swR, knob, hoverSwitch && !Locked, available && !Locked, bg, available && !state.HasValue);
            if (Locked && available && Expanded) DangerZone.DrawLock(g, new Rectangle(swR.X - Theme.S(12), swR.Bottom + Theme.S(3), swR.Width + Theme.S(24), Theme.S(14)), bg);
            if (pend != pendChip && IsHandleCreated) BeginInvoke((Action)(() => SetPendingChip(pend)));   // a bulk change the row missed
            if (Focused && ShowFocusCues) Gfx.FocusRing(g, new RectangleF(swR.X - Theme.S(3), swR.Y - Theme.S(1), swR.Width + Theme.S(5), swR.Height + Theme.S(1)), (swR.Height + Theme.S(1)) / 2f);
            // 3.16: a chevron at the top right shows that the row opens (on hover, focus or while open)
            if (hover || Expanded || Focused)
            {
                float cx = Width - Theme.S(20), cy = Theme.S(22), w = Theme.S(5), h = Theme.S(3);
                var o2 = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(hover ? Theme.Text : Theme.Muted, Math.Max(1.5f, 1.5f * Theme.Scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, Expanded ? new[] { new PointF(cx - w, cy + h / 2), new PointF(cx, cy - h / 2 - 1), new PointF(cx + w, cy + h / 2) }
                                              : new[] { new PointF(cx - w, cy - h / 2), new PointF(cx, cy + h / 2 + 1), new PointF(cx + w, cy - h / 2) });
                g.SmoothingMode = o2;
            }
            Color tc = !available ? Theme.Muted : shown ? Theme.Strong : Theme.Text;
            DrawLines(g, titleL, Theme.FB, titleR, tc, bg);
            if (noteL.Count > 0) DrawLines(g, noteL, Theme.FS, noteR, Theme.Muted, bg);
            for (int i = 0; i < chips.Count && i < chipR.Count; i++) Chip.Draw(g, chipR[i], chips[i].Key, chips[i].Value, bg);
            DrawLines(g, descL, Theme.FS, descR, Theme.Muted, bg);
            if (offL.Count > 0)
            {   // 3.6: what it switches off, marked by a neutral rule
                using (var b = new SolidBrush(Gfx.Mix(bg, Theme.Muted, 0.7))) g.FillRectangle(b, offR.X - Theme.S(14), offR.Y + Theme.S(3), Theme.S(2), offR.Height - Theme.S(6));
                DrawLines(g, offL, Theme.FS, offR, Theme.Text, bg);
            }
            if (breakL.Count > 0)
            {   // 3.4: what it can break, marked by a short rule so it reads as a consequence, not more description:
                // red for the Danger zone and advanced tweaks, amber for the rest (3.6)
                Color rc = T.Dangerous || T.Risk == Risk.Advanced ? Theme.Risk : Theme.Warn;
                using (var b = new SolidBrush(rc)) g.FillRectangle(b, breakR.X - Theme.S(14), breakR.Y + Theme.S(3), Theme.S(2), breakR.Height - Theme.S(6));
                DrawLines(g, breakL, Theme.FS, breakR, Gfx.Mix(Theme.Text, rc, 0.5), bg);
            }
            ImpactMeter.Draw(g, meterR, T.Impact);
            TextRenderer.DrawText(g, L.T(ImpactMeter.Word(T.Impact)), Theme.FT, wordR, T.Impact >= 3 ? Theme.Ok : T.Impact >= 1 ? Theme.Text : Theme.Muted, TextFormatFlags.NoPrefix);
            if (T.Impact > 0) DrawLines(g, gainL, Theme.FS, gainR, Theme.Muted, bg);
        }

        // ---- input
        Rectangle HitSwitch { get { var r = swR; r.Inflate(Theme.S(6), Theme.S(6)); return r; } }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hs = available && state.HasValue && HitSwitch.Contains(e.Location);
            if (!hover || hs != hoverSwitch) { hover = true; hoverSwitch = hs; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = hoverSwitch = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnParentChanged(EventArgs e) { hover = hoverSwitch = false; base.OnParentChanged(e); }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) hover = hoverSwitch = false; base.OnVisibleChanged(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus(); MarkSelected();
                if (HitSwitch.Contains(e.Location) || titleR.Contains(e.Location)) Flip(); else MainForm.Instance.OpenDetails(T);
            }
            base.OnMouseClick(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { Flip(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { MainForm.Instance.OpenDetails(T); e.Handled = true; }
            // 3.2: arrow keys walk the list like a list box; Home/End jump to the first/last row shown
            else if ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) && Parent != null) { Parent.SelectNextControl(this, e.KeyCode == Keys.Down, true, false, false); e.Handled = true; }
            else if ((e.KeyCode == Keys.Home || e.KeyCode == Keys.End) && Parent != null)
            {
                var vis = Parent.Controls.OfType<TweakRow>().Where(r => r.Visible).OrderBy(r => Parent.Controls.GetChildIndex(r)).ToList();
                if (vis.Count > 0) (e.KeyCode == Keys.Home ? vis[0] : vis[vis.Count - 1]).Focus();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.PageDown || e.KeyCode == Keys.PageUp)
            {
                var p = Parent;
                for (int i = 0; i < 8 && p != null; i++) p.SelectNextControl(FindFocused(p) ?? this, e.KeyCode == Keys.PageDown, true, false, false);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
        static Control FindFocused(Control p) { foreach (Control c in p.Controls) if (c.Focused) return c; return null; }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || k == Keys.Home || k == Keys.End || k == Keys.PageUp || k == Keys.PageDown || base.IsInputKey(k); }
        protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e) { if (IsInputKey(e.KeyCode)) e.IsInputKey = true; base.OnPreviewKeyDown(e); }
        protected override void OnGotFocus(EventArgs e)
        {
            MarkSelected(); Invalidate(); AppShell.Current.ShowDetails(T);
            var sp = Parent as ScrollableControl; if (sp != null) sp.ScrollControlIntoView(this);
            base.OnGotFocus(e);
        }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override AccessibleObject CreateAccessibilityInstance() { return new RowAcc(this); }
        class RowAcc : ControlAccessibleObject
        {
            readonly TweakRow r; public RowAcc(TweakRow r) : base(r) { this.r = r; }
            public override AccessibleRole Role { get { return AccessibleRole.CheckButton; } }
            public override AccessibleStates State
            {
                get
                {
                    var s = base.State | AccessibleStates.Focusable;
                    if (r.shown) s |= AccessibleStates.Checked;
                    if (!r.available) s |= AccessibleStates.Unavailable;
                    bool? st = r.state; if (!st.HasValue) s |= AccessibleStates.Busy;   // local copy: CS1690 (value-type field of a Control)
                    if (r.Focused) s |= AccessibleStates.Focused;
                    return s;
                }
            }
            public override string DefaultAction { get { return r.shown ? "Turn off" : "Turn on"; } }
            public override string Value
            {
                get
                {
                    bool p; var m = MainForm.Instance;
                    if (m != null && m.Pending.TryGetValue(r.T, out p)) return L.T(p ? "Pending: turns on when you click Apply" : "Pending: turns off when you click Apply");
                    return L.T(r.state == true ? "Applied" : r.state == false ? "Not applied" : "Checking");
                }
            }
            public override void DoDefaultAction() { r.Flip(); }
        }
    }


    public class TweakPage : FlowLayoutPanel, IPage
    {
        readonly string cat; readonly List<TweakRow> rows = new List<TweakRow>(); bool built; string lastQ = ""; DangerBanner banner;
        public TweakPage(string category)
        {
            cat = category; FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true; BackColor = Theme.Bg; Theme.VerticalOnly(this);
            Resize += delegate
            {
                foreach (var r in rows) r.Width = RowWidth;
                if (banner != null) banner.Width = RowWidth;
                foreach (Control c in Controls) if (c is Label && c.AutoSize) c.MaximumSize = new Size(Math.Max(200, RowWidth - 2), 0);
            };
        }
        // 3.11: rows stop at a readable width on wide windows, so the switch stays near its title
        int RowWidth { get { return Math.Max(300, Math.Min(Theme.S(Theme.MaxContent), ClientSize.Width - Padding.Horizontal - 6)); } }
        public void OnShow() { Reload(); }
        public void Reload()
        {
            if (!built)
            {
                built = true;
                SuspendLayout();
                string note = null; Color nc = Theme.Muted;
                if (cat == DangerZone.CAT) { banner = new DangerBanner(); Controls.Add(banner); }
                else if (cat == Cat.PowerPlan) note = "These change the ACTIVE power plan. Also tick 'Create & activate ArchTweaker Max Performance'. ArchTweaker applies it first, so Balanced stays the same. This group ignores power efficiency: maximum clocks, no parking, no device sleep.";
                else if (cat == Cat.Gpu || cat == Cat.Input) note = "ArchTweaker writes per-device tweaks to every matching device on this PC. The details panel lists them. It saves each original value and restores it exactly when you untick.";
                else if (cat == Cat.Commands) note = "These run Windows tools (bcdedit, netsh, fsutil, powercfg). The checkbox shows what ArchTweaker applied.";
                int hidden = Mode.Lite ? Catalog.All.Count(x => x.Category == cat && !Mode.LiteTweak(x)) : 0;   // 3.23
                if (hidden > 0)
                {
                    var more = new LinkLabel { Text = L.F("+{0} more tweaks in Pro mode (higher risk or experimental). Switch to Pro…", hidden), AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Strong, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 0, 0, 10), UseMnemonic = false, MaximumSize = new Size(Math.Max(200, RowWidth - 2), 0) };
                    more.LinkClicked += delegate { MainForm.Instance.SwitchMode(); };
                    Controls.Add(more);
                }
                if (note != null) Controls.Add(new Label { Text = note, ForeColor = nc, AutoSize = true, MaximumSize = new Size(Math.Max(200, RowWidth - 2), 0), Margin = new Padding(0, 0, 0, 10), UseMnemonic = false });
                foreach (var t in Catalog.All.Where(x => x.Category == cat && Mode.Allows(x)))
                {
                    var r = new TweakRow(t) { Width = RowWidth };
                    if (banner != null) banner.Width = r.Width;
                    if (!t.Applicable) r.MarkUnavailable(t.UnavailableReason);
                    rows.Add(r); Controls.Add(r);
                }
                ResumeLayout();
            }
            LoadRows();
            Filter(lastQ);
            // states not cached yet are read in the background; rows show "checking" until then
            if (rows.Any(r => r.T.Applicable && !StateCache.Get(r.T).HasValue))
                StateCache.ReadAsync(rows.Select(r => r.T).Where(t => t.Applicable), this, () => { LoadRows(); Filter(lastQ); MainForm.Instance.PageStatesReady(cat); });
        }
        public string Category { get { return cat; } }
        public void Resort() { if (built) Filter(lastQ); }
        void LoadRows()
        {
            foreach (var r in rows)
            {
                bool p;
                bool? pend = AppShell.Current.Pending.TryGetValue(r.T, out p) ? (bool?)p : null;
                r.Load(r.T.Applicable ? StateCache.Get(r.T) : (bool?)false, pend);
                r.SetRecommended(r.T.Applicable && Recommend.Has(r.T) && StateCache.Get(r.T) == false);
            }
        }
        Label empty;
        public void Filter(string q)
        {
            lastQ = q ?? "";
            SuspendLayout();
            int n = 0;
            // 3.2: rows in the chosen sort order (the note label, if any, stays first)
            var order = TweakSort.Order(rows);
            int baseIdx = Controls.Count > 0 && ((Controls[0] is Label && Controls[0] != empty) || Controls[0] is DangerBanner) ? 1 : 0;
            for (int i = 0; i < order.Count; i++) if (Controls.GetChildIndex(order[i]) != baseIdx + i) Controls.SetChildIndex(order[i], baseIdx + i);
            foreach (var r in rows) { bool show = TweakFilter.Pass(r.T, r.Checked, lastQ); r.Visible = show; r.SetHighlight(lastQ); if (show) n++; }
            if (empty == null) { empty = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 6, 0, 0), UseMnemonic = false }; Controls.Add(empty); }
            empty.Visible = n == 0;
            empty.Text = TweakFilter.Mode == 4 ? "No tweak on this page has a high speed gain. Click All above the list to see them all." : TweakFilter.Mode == 1 || TweakFilter.Mode == 3 ? "Everything here that matches the filter is already applied. Click All above the list to see them." : TweakFilter.Mode == 6 ? "Nothing on this page waits for Apply." : TweakFilter.Mode == 7 ? "Every tweak that ArchTweaker applied on this page is still in place." : (lastQ.Length > 0 ? "No tweaks on this page match \"" + lastQ + "\". Try another word, or clear the search (Esc)." : "No tweaks on this page match the filter. Click All above the list to see them all.");
            ResumeLayout();
        }
    }
}
