// Applied/not-applied state of every tweak, read off the UI thread (2.6).
// Pages draw their rows straight away ("checking…" switch), then fill in real states when the background
// read finishes. Any apply or undo clears the cache, and a generation counter drops results that were
// read before the clear, so a slow read can never paint a stale state.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    public static class StateCache
    {
        static readonly Dictionary<Tweak, bool> map = new Dictionary<Tweak, bool>();
        static readonly object sync = new object();
        static int gen, holds;

        public static int Generation { get { lock (sync) return gen; } }

        public static void Clear()
        {
            lock (sync) { map.Clear(); gen++; }
            DeviceRegOp.Flush();
        }

        // While an apply/undo batch runs, reads are not cached (they could see a half-applied system);
        // the end of the batch clears everything so the next read sees the final state.
        public static void BeginHold() { lock (sync) { holds++; gen++; } }
        public static void EndHold() { lock (sync) { if (holds > 0) holds--; } Clear(); }

        public static bool? Get(Tweak t)
        {
            lock (sync) { bool v; return map.TryGetValue(t, out v) ? (bool?)v : null; }
        }

        static void Put(Tweak t, bool v, int g) { lock (sync) if (g == gen && holds == 0) map[t] = v; }

        // Cached value, or read it now on the calling thread. For background code and single tweaks only:
        // UI code uses Get() + ReadAsync() so a cold cache never freezes the window.
        public static bool Applied(Tweak t)
        {
            var c = Get(t);
            if (c.HasValue) return c.Value;
            lock (t)   // 3.6.4: several startup readers (health, home, advisor, rows) asked for the same tweak at once and each read it again
            {
                c = Get(t);
                if (c.HasValue) return c.Value;
                int g = Generation;
                bool v = Read(t);
                Put(t, v, g);
                return v;
            }
        }

        static bool Read(Tweak t)
        {
            if (!t.Applicable) return false;
            try { return t.IsApplied(); } catch (Exception ex) { Log.W("State check {0}: {1}", t.Id, ex.Message); return false; }
        }

        // ---- one dedicated reader thread with a queue (state checks share caches and locks; parallel reads only contend)
        static readonly Queue<Action> jobs = new Queue<Action>();
        static Thread reader;
        static void Enqueue(Action job)
        {
            lock (jobs)
            {
                jobs.Enqueue(job);
                if (reader == null)
                {
                    reader = new Thread(ReaderLoop) { IsBackground = true, Name = "ArchTweaker state reader", Priority = ThreadPriority.BelowNormal };
                    reader.Start();
                }
                Monitor.Pulse(jobs);
            }
        }
        static void ReaderLoop()
        {
            while (true)
            {
                Action job;
                lock (jobs) { while (jobs.Count == 0) Monitor.Wait(jobs); job = jobs.Dequeue(); }
                try { job(); } catch (Exception ex) { Log.W("State reader: {0}", ex.Message); }
            }
        }

        // Reads the states that aren't cached yet on the reader thread, then calls done on the UI thread.
        // If the cache is cleared meanwhile, the read starts again (max 3 times) so done never reports half the states.
        public static void ReadAsync(IEnumerable<Tweak> tweaks, Control ui, Action done)
        {
            var all = tweaks.Distinct().ToList();
            if (all.All(t => Get(t).HasValue)) { if (done != null) done(); return; }
            var ctx = SynchronizationContext.Current;   // posts even before `ui` has a window handle
            Enqueue(delegate
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    int g = Generation;
                    foreach (var t in all)
                    {
                        if (Generation != g) break;
                        if (Get(t).HasValue) continue;
                        Put(t, Read(t), g);
                    }
                    lock (sync) if (g == gen && holds == 0) break;
                    Thread.Sleep(150);
                }
                if (done == null) return;
                SendOrPostCallback run = delegate { if (!ui.IsDisposed) done(); };
                try
                {
                    if (ctx != null) ctx.Post(run, null);
                    else if (ui.IsHandleCreated && !ui.IsDisposed) ui.BeginInvoke((Action)(() => run(null)));
                }
                catch (InvalidOperationException) { }        // window closed while reading
            });
        }
    }
}
