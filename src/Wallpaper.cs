using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WallStretch
{
    public class MonitorImage
    {
        public MonitorInfo Monitor;
        public Bitmap Bitmap;

        public MonitorImage(MonitorInfo monitor, Bitmap bitmap)
        {
            Monitor = monitor;
            Bitmap = bitmap;
        }
    }

    public static class Wallpaper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        [ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
        private class DesktopWallpaper
        {
        }

        /// <summary>Windows per-monitor wallpaper API (IDesktopWallpaper).</summary>
        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
            void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out NativeRect rect);
            void SetBackgroundColor(uint color);
            uint GetBackgroundColor();
            void SetPosition(int position);
            int GetPosition();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint param, string vparam, uint winini);

        private static IDesktopWallpaper _api;

        /// <summary>Creates the COM object up-front, while we are not inside an input-synchronous call.</summary>
        public static void WarmUp()
        {
            try { var unused = Api; }
            catch { }
        }

        private static IDesktopWallpaper Api
        {
            get
            {
                if (_api == null) _api = (IDesktopWallpaper)new DesktopWallpaper();
                return _api;
            }
        }

        public static string OutputDir()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallStretch");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static Bitmap Compose(Size canvas, Image image, float scale, float ox, float oy)
        {
            return Compose(canvas, image, scale, ox, oy, null);
        }

        /// <summary>
        /// Renders the canvas with the image. When clipRects is set, the image is drawn only
        /// inside those rectangles; the rest stays black.
        /// </summary>
        public static Bitmap Compose(Size canvas, Image image, float scale, float ox, float oy, Rectangle[] clipRects)
        {
            var bmp = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format24bppRgb);
            bmp.SetResolution(96f, 96f);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                if (clipRects != null && clipRects.Length > 0)
                {
                    using (var clip = new Region())
                    {
                        clip.MakeEmpty();
                        foreach (var r in clipRects) clip.Union(r);
                        g.SetClip(clip, CombineMode.Replace);
                        g.DrawImage(image, new RectangleF(ox, oy, image.Width * scale, image.Height * scale));
                    }
                }
                else
                {
                    g.DrawImage(image, new RectangleF(ox, oy, image.Width * scale, image.Height * scale));
                }
            }
            return bmp;
        }

        public static void SaveJpeg(Bitmap bmp, string path, long quality)
        {
            ImageCodecInfo codec = null;
            foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
            {
                if (c.MimeType == "image/jpeg") { codec = c; break; }
            }

            var ps = new EncoderParameters(1);
            ps.Param[0] = new EncoderParameter(Encoder.Quality, quality);
            bmp.Save(path, codec, ps);
        }

        /// <summary>Sets a separate image for every monitor (pixel-exact, no stretching).</summary>
        public static List<string> ApplyPerMonitor(List<MonitorImage> images)
        {
            string dir = OutputDir();
            var files = new List<string>();

            IDesktopWallpaper api = Api;

            var map = new Dictionary<Rectangle, string>();
            uint count = api.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                string id = api.GetMonitorDevicePathAt(i);
                NativeRect r;
                api.GetMonitorRECT(id, out r);
                var rect = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                map[rect] = id;
            }

            api.SetPosition(4); // DESKTOP_WALLPAPER_POSITION_FILL

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            for (int i = 0; i < images.Count; i++)
            {
                MonitorImage mi = images[i];
                string file = Path.Combine(dir, "mon" + i + "-" + stamp + ".jpg");
                SaveJpeg(mi.Bitmap, file, 92);

                string id;
                if (map.TryGetValue(mi.Monitor.Bounds, out id))
                {
                    api.SetWallpaper(id, file);
                    files.Add(file);
                }
            }

            CleanupOld(dir, "mon-*.jpg", 12);
            CleanupOld(dir, "wallpaper-*.jpg", 6);
            return files;
        }

        /// <summary>Single spanned wallpaper for the whole virtual desktop (legacy / CLI).</summary>
        public static string Apply(Bitmap bmp)
        {
            string dir = OutputDir();
            string file = Path.Combine(dir, "wallpaper-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jpg");
            SaveJpeg(bmp, file, 92);

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
            {
                key.SetValue("WallPaper", file, RegistryValueKind.String);
                key.SetValue("WallpaperStyle", "22", RegistryValueKind.String);
                key.SetValue("TileWallpaper", "0", RegistryValueKind.String);
            }

            SystemParametersInfo(20, 0, file, 3);
            CleanupOld(dir, "wallpaper-*.jpg", 6);
            return file;
        }

        /// <summary>Renders the whole virtual desktop from an image (cover) and applies it as one span.</summary>
        public static string Apply(string sourceImagePath)
        {
            Rectangle vb;
            DisplayInfo.Load(out vb);
            using (Image img = Image.FromFile(sourceImagePath))
            {
                float s = Math.Max((float)vb.Width / img.Width, (float)vb.Height / img.Height);
                float ox = (vb.Width - img.Width * s) / 2f;
                float oy = (vb.Height - img.Height * s) / 2f;
                using (Bitmap bmp = Compose(new Size(vb.Width, vb.Height), img, s, ox, oy))
                {
                    return Apply(bmp);
                }
            }
        }

        private static void CleanupOld(string dir, string pattern, int keep)
        {
            try
            {
                var files = new DirectoryInfo(dir).GetFiles(pattern);
                Array.Sort(files, delegate(FileInfo a, FileInfo b) { return b.CreationTime.CompareTo(a.CreationTime); });
                for (int i = keep; i < files.Length; i++)
                {
                    try { files[i].Delete(); } catch { }
                }
            }
            catch { }
        }
    }
}
