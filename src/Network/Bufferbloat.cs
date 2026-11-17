// ArchTweaker: Bufferbloat test (3.30): ping at rest, while downloading and while uploading, then a grade A+ to F.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;

namespace ArchTweaker
{
    public static class Bufferbloat
    {
        public const string Target = "1.1.1.1";
        const string Down = "https://speed.cloudflare.com/__down?bytes=100000000", Up = "https://speed.cloudflare.com/__up";

        public class Phase
        {
            public string Name; public List<double> Rtt = new List<double>(); public int Lost; public double Mbps;
            public double Median { get { return Pct(50); } }
            public double Pct(double p)
            {
                if (Rtt.Count == 0) return 0;
                var s = Rtt.OrderBy(x => x).ToList();
                double i = (s.Count - 1) * p / 100.0; int lo = (int)Math.Floor(i), hi = (int)Math.Ceiling(i);
                return s[lo] + (s[hi] - s[lo]) * (i - lo);
            }
            // min · 25 % · median · 75 % · 95 % · max: the box plot as text
            public string Line()
            {
                if (Rtt.Count == 0) return L.F("{0}: no reply", L.T(Name));
                string t = L.F("{0}: min {1:0} · 25 % {2:0} · median {3:0} · 75 % {4:0} · 95 % {5:0} · max {6:0} ms", L.T(Name), Rtt.Min(), Pct(25), Median, Pct(75), Pct(95), Rtt.Max());
                if (Lost > 0) t += L.F(" · {0} lost", Lost);
                if (Mbps > 0) t += L.F(" · {0:0} Mbit/s", Mbps);
                return t;
            }
        }
        public class Result
        {
            public Phase Idle, Download, Upload;
            public double DownMore { get { return Math.Max(0, Download.Median - Idle.Median); } }
            public double UpMore { get { return Math.Max(0, Upload.Median - Idle.Median); } }
            public string Grade { get { return Bufferbloat.Grade(Math.Max(DownMore, UpMore)); } }
        }

        // Same bands as the common bufferbloat tests: extra latency under load, in ms.
        public static string Grade(double extraMs)
        {
            if (extraMs < 5) return "A+";
            if (extraMs < 30) return "A";
            if (extraMs < 60) return "B";
            if (extraMs < 200) return "C";
            if (extraMs < 400) return "D";
            return "F";
        }
        public static string Advice(string grade)
        {
            switch (grade)
            {
                case "A+": case "A": return "Latency stays low when the line is busy. Nothing to change.";
                case "B": return "Latency rises a little when the line is busy. Games feel it only during big downloads.";
                default: return "Latency rises a lot when the line is busy: ping spikes in games whenever someone downloads or uploads. Turn on SQM, Smart Queue or Cake in the router, or limit its speed to about 90 % of your line. No Windows tweak can fix this.";
            }
        }

        public static Result Run(int phaseSeconds)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 16);
            var r = new Result();
            r.Idle = PingFor("At rest", phaseSeconds, null);
            r.Download = PingFor("While downloading", phaseSeconds, DownloadLoad);
            r.Upload = PingFor("While uploading", phaseSeconds, UploadLoad);
            return r;
        }

        // pings every 100 ms while `load` runs on 4 connections; load returns the bytes moved
        static Phase PingFor(string name, int secs, Func<Func<bool>, long> load)
        {
            var ph = new Phase { Name = name };
            var stopAt = DateTime.UtcNow.AddSeconds(secs);
            Func<bool> go = () => DateTime.UtcNow < stopAt;
            long bytes = 0; var threads = new List<Thread>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (load != null)
                for (int i = 0; i < 4; i++)
                {
                    var t = new Thread(() => { long b = 0; try { b = load(go); } catch { } Interlocked.Add(ref bytes, b); }) { IsBackground = true };
                    threads.Add(t); t.Start();
                }
            if (load != null) Thread.Sleep(1500);   // let the connections ramp up before counting
            using (var p = new Ping())
                while (go())
                {
                    try
                    {
                        var rep = p.Send(Target, 2000);
                        if (rep.Status == IPStatus.Success) ph.Rtt.Add(Math.Max(1, rep.RoundtripTime)); else ph.Lost++;
                    }
                    catch { ph.Lost++; }
                    Thread.Sleep(100);
                }
            foreach (var t in threads) t.Join(5000);
            double s = sw.Elapsed.TotalSeconds;
            if (load != null && s > 0) ph.Mbps = Interlocked.Read(ref bytes) * 8 / s / 1e6;
            return ph;
        }

        static long DownloadLoad(Func<bool> go)
        {
            long n = 0; var buf = new byte[65536];
            while (go())
            {
                var req = (HttpWebRequest)WebRequest.Create(Down);
                req.Timeout = 10000; req.ReadWriteTimeout = 10000; req.UserAgent = "ArchTweaker";
                try
                {
                    using (var resp = req.GetResponse())
                    using (var st = resp.GetResponseStream())
                    {
                        int k;
                        while (go() && (k = st.Read(buf, 0, buf.Length)) > 0) n += k;
                        if (!go()) { try { req.Abort(); } catch { } }
                    }
                }
                catch (WebException) { Thread.Sleep(500); }
                catch (IOException) { Thread.Sleep(500); }
            }
            return n;
        }

        static long UploadLoad(Func<bool> go)
        {
            long n = 0; var buf = new byte[65536]; new Random().NextBytes(buf);
            while (go())
            {
                var req = (HttpWebRequest)WebRequest.Create(Up);
                req.Method = "POST"; req.Timeout = 10000; req.ReadWriteTimeout = 10000; req.UserAgent = "ArchTweaker";
                req.AllowWriteStreamBuffering = false; req.ContentLength = 25L * 1024 * 1024; req.ContentType = "application/octet-stream";
                try
                {
                    using (var st = req.GetRequestStream())
                        for (long sent = 0; sent < req.ContentLength && go(); sent += buf.Length)
                        {
                            int k = (int)Math.Min(buf.Length, req.ContentLength - sent);
                            st.Write(buf, 0, k); n += k;
                        }
                    if (!go()) { try { req.Abort(); } catch { } break; }
                    using (req.GetResponse()) { }
                }
                catch (WebException) { if (!go()) break; Thread.Sleep(500); }
                catch (IOException) { if (!go()) break; Thread.Sleep(500); }
            }
            return n;
        }
    }
}
