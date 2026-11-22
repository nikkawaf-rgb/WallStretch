using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WallStretch
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0) { try { AttachConsole(-1); } catch { } }

            DisplayInfo.EnablePerMonitorDpi();

            if (args.Length >= 1 && args[0] == "--list")
            {
                PrintMonitors();
                return;
            }

            if (args.Length >= 2 && args[0] == "--apply")
            {
                string applied = Wallpaper.Apply(args[1]);
                Console.WriteLine("applied: " + applied);
                return;
            }

            if (args.Length >= 3 && args[0] == "--render")
            {
                string file = RenderCover(args[1], args[2]);
                Console.WriteLine("rendered: " + file);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
            }
            catch (Exception ex)
            {
                try
                {
                    string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                    File.WriteAllText(log, ex.ToString());
                }
                catch { }
                throw;
            }
        }

        private static void PrintMonitors()
        {
            Rectangle vb;
            var list = DisplayInfo.Load(out vb);
            Console.WriteLine("virtual: " + vb);
            foreach (var m in list)
            {
                Console.WriteLine(string.Format("  {0} | {1} | {2} | primary={3}", m.Device, m.Name, m.Bounds, m.Primary));
            }
        }

        private static string RenderCover(string path, string outFile)
        {
            Rectangle vb;
            DisplayInfo.Load(out vb);

            using (Image img = Image.FromFile(path))
            {
                float s = Math.Max((float)vb.Width / img.Width, (float)vb.Height / img.Height);
                float ox = (vb.Width - img.Width * s) / 2f;
                float oy = (vb.Height - img.Height * s) / 2f;

                using (Bitmap bmp = Wallpaper.Compose(new Size(vb.Width, vb.Height), img, s, ox, oy))
                {
                    Wallpaper.SaveJpeg(bmp, outFile, 92);
                }
            }
            return outFile;
        }
    }
}
