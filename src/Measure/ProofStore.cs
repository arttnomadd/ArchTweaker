// ArchTweaker: measured proof per tweak (3.34). An A/B test or the Tuning assistant measures a tweak on THIS PC;
// the result is stored here so the tweak can show "Proven +3.2% on this PC" instead of a generic expected gain.
// A proof is tied to the Windows build and GPU driver it was measured on; when either changes it is marked stale.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;

namespace ArchTweaker
{
    public struct Proof
    {
        public string Id, Metric, Unit, Gpu, Source;
        public DateTime When;
        public double GainPct;   // signed: + means the tweak measurably improved this metric, - means it made it worse
        public int Samples, Build;
        public bool Significant;

        // helped = a real, useful win we can stand behind; the "Proven" chip and filter use this
        public bool Helped { get { return Significant && GainPct >= 1.0; } }
        public bool Worse { get { return Significant && GainPct <= -1.0; } }

        // short label for the row chip / details line, e.g. "+3.2% 1% low FPS"
        public string Short()
        {
            string pct = (GainPct >= 0 ? "+" : "") + GainPct.ToString("0.0", CultureInfo.InvariantCulture) + "%";   // 3.35: "+3.2%", as the chip and changelog show it
            return pct + " " + L.T(Metric);
        }
        public string Line()
        {
            string n = Samples > 0 ? L.F(" · {0} runs each", Samples) : "";
            return Short() + " · " + When.ToString("d MMM") + n + (string.IsNullOrEmpty(Source) ? "" : " · " + L.T(Source));
        }
        // true when the Windows build or the GPU driver has changed since the measurement, so it no longer stands
        public bool Stale(out string why)
        {
            why = null;
            int b = OSInfo.Build;
            if (Build > 0 && b > 0 && b != Build) { why = L.F("Windows build changed ({0} → {1})", Build, b); return true; }
            string g = ProofStore.CurrentGpu;
            if (!string.IsNullOrEmpty(Gpu) && !string.IsNullOrEmpty(g) && g != Gpu) { why = L.T("GPU driver changed since the test"); return true; }
            return false;
        }
    }

    public static class ProofStore
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string FileOverride;   // tests
        static string File_ { get { return FileOverride ?? FileDefault; } }
        static string FileDefault { get { return Path.Combine(Paths.Root, "proof.tsv"); } }

        static Dictionary<string, Proof> cache;
        static readonly object gate = new object();

        // the GPU driver version this PC runs now, read once; "" when it can't be read (that just disables the GPU staleness check)
        static string gpu; static bool gpuRead;
        public static string CurrentGpu
        {
            get
            {
                lock (gate)   // 3.35: a second thread waits for the first read instead of seeing "" (that skipped the stale check)
                {
                if (gpuRead) return gpu ?? "";
                try
                {
                    var list = new List<KeyValuePair<string, string>>();
                    using (var s = new ManagementObjectSearcher(@"root\CIMV2", "SELECT DriverVersion, PNPDeviceID, AdapterRAM FROM Win32_VideoController"))
                        foreach (ManagementObject o in s.Get())
                            list.Add(new KeyValuePair<string, string>((o["PNPDeviceID"] ?? "").ToString(), (o["DriverVersion"] ?? "").ToString()));
                    gpu = PickGpu(list);
                }
                catch { gpu = ""; }
                gpuRead = true;
                return gpu ?? (gpu = "");
                }
            }
        }

        // 3.35: the first Win32_VideoController is not always the same one (iGPU + dGPU, Parsec / OBS / Spacedesk display
        // adapters), which marked proofs stale for no reason. Prefer a real NVIDIA / AMD / Intel PCI GPU in a fixed order
        // (NVIDIA, AMD, then Intel), then the lowest device id; virtual adapters (ROOT\..., SWD\...) only when there is nothing else.
        public static string PickGpu(IList<KeyValuePair<string, string>> adapters)   // key = PNPDeviceID, value = DriverVersion
        {
            string[] vendors = { "VEN_10DE", "VEN_1002", "VEN_8086" };
            var real = adapters.Where(a => !string.IsNullOrEmpty(a.Value)).ToList();
            var pci = real.Where(a => (a.Key ?? "").StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)).ToList();
            var pick = pci.OrderBy(a => { int i = Array.FindIndex(vendors, v => a.Key.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0); return i < 0 ? vendors.Length : i; })
                          .ThenBy(a => a.Key, StringComparer.OrdinalIgnoreCase).Select(a => a.Value).FirstOrDefault();
            if (pick != null) return pick;
            return real.OrderBy(a => a.Key ?? "", StringComparer.OrdinalIgnoreCase).Select(a => a.Value).FirstOrDefault() ?? "";
        }

        // a line with this metric removes the proof of that tweak ("Forget this result"); the file stays append-only
        public const string Forgotten = "!forget";

        static Dictionary<string, Proof> Load()
        {
            lock (gate)
            {
                if (cache != null) return cache;
                var d = new Dictionary<string, Proof>(StringComparer.Ordinal);
                try
                {
                    if (File.Exists(File_))
                        foreach (var line in File.ReadAllLines(File_))
                        {
                            var p = Parse(line);
                            if (!p.HasValue) continue;
                            if (p.Value.Metric == Forgotten) d.Remove(p.Value.Id);   // 3.35
                            else d[p.Value.Id] = p.Value;   // newest line for an id wins (file is append-only, in order)
                        }
                }
                catch (Exception ex) { Log.W("Proof: read failed: {0}", ex.Message); }
                cache = d;
                return d;
            }
        }

        public static Proof? Parse(string line)
        {
            var f = (line ?? "").Split('\t');
            if (f.Length < 10) return null;
            DateTime when; double gain; int n, build;
            if (!DateTime.TryParseExact(f[1], "yyyyMMdd-HHmmss", Inv, DateTimeStyles.None, out when)) return null;
            double.TryParse(f[4], NumberStyles.Float, Inv, out gain);
            int.TryParse(f[5], out n); int.TryParse(f[7], out build);
            return new Proof { Id = f[0], When = when, Metric = f[2], Unit = f[3], GainPct = gain, Samples = n,
                Significant = f[6] == "1", Build = build, Gpu = f[8], Source = f[9] };
        }

        public static Proof? Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Proof p; return Load().TryGetValue(id, out p) ? p : (Proof?)null;
        }
        public static int Count { get { return Load().Count; } }

        // gainPct must be signed so + always means "this metric improved", whatever the metric's own direction
        public static void Record(Tweak t, string metric, string unit, double gainPct, int samples, bool significant, string source)
        {
            if (t == null || string.IsNullOrEmpty(t.Id)) return;
            var p = new Proof { Id = t.Id, When = DateTime.Now, Metric = metric, Unit = unit, GainPct = gainPct,
                Samples = samples, Significant = significant, Build = OSInfo.Build, Gpu = CurrentGpu, Source = source };
            try
            {
                // 3.35: the date in invariant culture, as Parse reads it (a Thai calendar wrote year 2569)
                string row = string.Join("\t", new[] { p.Id, p.When.ToString("yyyyMMdd-HHmmss", Inv), Clean(metric), Clean(unit),
                    gainPct.ToString("0.0", Inv), samples.ToString(Inv), significant ? "1" : "0", p.Build.ToString(Inv), Clean(p.Gpu), Clean(source) });
                lock (gate) { File.AppendAllText(File_, row + Environment.NewLine); if (cache != null) cache[p.Id] = p; }   // 3.35: one writer at a time
                Log.W("Proof: {0} {1} on this PC (build {2}).", t.Title, p.Short(), p.Build);
            }
            catch (Exception ex) { Log.W("Proof: write failed for {0}: {1}", t.Id, ex.Message); }
        }
        // 3.35: "Forget this result" (a bad run, a game left in the background). Writes a tombstone line; older lines stay.
        public static bool Forget(string id)
        {
            if (string.IsNullOrEmpty(id) || Get(id) == null) return false;
            try
            {
                string row = string.Join("\t", new[] { Clean(id), DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv), Forgotten, "", "0", "0", "0", "0", "", "" });
                lock (gate) { File.AppendAllText(File_, row + Environment.NewLine); if (cache != null) cache.Remove(id); }
                Log.W("Proof: the measured result of {0} was forgotten.", id);
                return true;
            }
            catch (Exception ex) { Log.W("Proof: forget failed for {0}: {1}", id, ex.Message); return false; }
        }
        // tests: read another file
        public static void ResetCache() { lock (gate) cache = null; }

        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    }
}
