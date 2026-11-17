// ArchTweaker: Program icons for the Apps, Uninstaller and startup lists.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ArchTweaker
{
    // ================= PROGRAM ICONS (3.3) =================
    // Each program's own full-colour icon in the Startup, Uninstaller and Store-app lists, so a long list can be scanned
    // by eye. Icons are read off the UI thread (Get), then put into the list's ImageList on the UI thread (Attach).
    public static class AppIcons
    {
        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        public static int Size { get { return Theme.S(16); } }

        static Bitmap Fit(Image src)
        {
            int n = Size; var b = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                float k = Math.Min((float)n / src.Width, (float)n / src.Height);
                float w = src.Width * k, h = src.Height * k;
                g.DrawImage(src, (n - w) / 2f, (n - h) / 2f, w, h);
            }
            return b;
        }

        // "C:\path\app.exe", "C:\path\app.exe,0", "\"C:\path\app.exe\",-101", or an .ico/.png file
        public static Bitmap FromIconSpec(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            lock (cache) { Bitmap c; if (cache.TryGetValue(spec, out c)) return c; }
            Bitmap result = null;
            try
            {
                string path = Environment.ExpandEnvironmentVariables(spec.Trim()); int index = 0;
                int comma = path.LastIndexOf(',');
                if (comma > 2 && int.TryParse(path.Substring(comma + 1).Trim(), out index)) path = path.Substring(0, comma);
                path = path.Trim().Trim('"');
                if (File.Exists(path))
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".png" || ext == ".bmp" || ext == ".jpg") using (var img = Image.FromFile(path)) result = Fit(img);
                    else if (ext == ".ico") using (var ic = new Icon(path, Size, Size)) using (var bm = ic.ToBitmap()) result = Fit(bm);
                    else
                    {
                        var large = new IntPtr[1]; var small = new IntPtr[1];
                        if (ExtractIconEx(path, index, large, small, 1) > 0)
                        {
                            IntPtr h = Size > 20 && large[0] != IntPtr.Zero ? large[0] : (small[0] != IntPtr.Zero ? small[0] : large[0]);
                            try { using (var ic = Icon.FromHandle(h)) using (var bm = ic.ToBitmap()) result = Fit(bm); }
                            finally { if (large[0] != IntPtr.Zero) DestroyIcon(large[0]); if (small[0] != IntPtr.Zero) DestroyIcon(small[0]); }
                        }
                        if (result == null) using (var ic = Icon.ExtractAssociatedIcon(path)) if (ic != null) using (var bm = ic.ToBitmap()) result = Fit(bm);
                    }
                }
            }
            catch { result = null; }
            lock (cache) cache[spec] = result;
            return result;
        }

        // A Store app's logo from its AppxManifest.xml (<Properties><Logo>), picking the scale-100 / smallest variant
        public static Bitmap FromPackage(string installLocation)
        {
            if (string.IsNullOrEmpty(installLocation) || !Directory.Exists(installLocation)) return null;
            string key = "pkg|" + installLocation;
            lock (cache) { Bitmap c; if (cache.TryGetValue(key, out c)) return c; }
            Bitmap result = null;
            try
            {
                string man = Path.Combine(installLocation, "AppxManifest.xml");
                if (File.Exists(man))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(man), @"<Logo>\s*([^<]+?)\s*</Logo>");
                    if (m.Success)
                    {
                        string rel = m.Groups[1].Value.Replace('/', '\\');
                        string full = Path.Combine(installLocation, rel);
                        string pick = File.Exists(full) ? full : null;
                        if (pick == null)
                        {
                            string dir = Path.GetDirectoryName(full), stem = Path.GetFileNameWithoutExtension(full), ext = Path.GetExtension(full);
                            if (Directory.Exists(dir))
                            {
                                var c = Directory.GetFiles(dir, stem + ".*" + ext);
                                pick = c.FirstOrDefault(f => f.IndexOf("scale-100", StringComparison.OrdinalIgnoreCase) >= 0)
                                    ?? c.FirstOrDefault(f => f.IndexOf("targetsize-24", StringComparison.OrdinalIgnoreCase) >= 0)
                                    ?? c.OrderBy(f => new FileInfo(f).Length).FirstOrDefault();
                            }
                        }
                        if (pick != null) using (var img = Image.FromFile(pick)) result = Fit(img);
                    }
                }
            }
            catch { result = null; }
            lock (cache) cache[key] = result;
            return result;
        }

        // Gives the list an image list (once) and the item its icon; a program without one gets the generic app icon
        public static void Attach(ListView lv, ListViewItem it, string key, Bitmap icon)
        {
            if (lv.SmallImageList == null)
            {
                lv.SmallImageList = new ImageList { ImageSize = new Size(Size, Size), ColorDepth = ColorDepth.Depth32Bit };
                using (var ic = new Icon(SystemIcons.Application, Size, Size)) using (var bm = ic.ToBitmap()) lv.SmallImageList.Images.Add("__app", Fit(bm));
            }
            if (icon != null && !lv.SmallImageList.Images.ContainsKey(key)) lv.SmallImageList.Images.Add(key, icon);
            it.ImageKey = icon != null ? key : "__app";
        }
    }
}
