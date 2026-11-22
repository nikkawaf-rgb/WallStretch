using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace WallStretch
{
    public class MonitorInfo
    {
        public IntPtr Handle;
        public string Device;
        public string Name;
        public Rectangle Bounds;
        public bool Primary;
        public bool Included = true;
        /// <summary>Vertical shift of this screen relative to the picture (virtual desktop pixels).</summary>
        public int OffsetY;

        /// <summary>Monitor rect taking the user's vertical shift into account.</summary>
        public Rectangle Frame
        {
            get { return new Rectangle(Bounds.X, Bounds.Y + OffsetY, Bounds.Width, Bounds.Height); }
        }
    }

    public static class DisplayInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        public static void EnablePerMonitorDpi()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch { }
        }

        public static List<MonitorInfo> Load(out Rectangle virtualBounds)
        {
            var list = new List<MonitorInfo>();

            MonitorEnumProc proc = delegate(IntPtr h, IntPtr hdc, ref RECT r, IntPtr data)
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(h, ref mi))
                {
                    var info = new MonitorInfo();
                    info.Handle = h;
                    info.Device = mi.szDevice;
                    info.Bounds = new Rectangle(
                        mi.rcMonitor.Left,
                        mi.rcMonitor.Top,
                        mi.rcMonitor.Right - mi.rcMonitor.Left,
                        mi.rcMonitor.Bottom - mi.rcMonitor.Top);
                    info.Primary = (mi.dwFlags & 1) != 0;
                    info.Name = FriendlyName(mi.szDevice);
                    list.Add(info);
                }
                return true;
            };

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);

            int x = GetSystemMetrics(76);
            int y = GetSystemMetrics(77);
            int w = GetSystemMetrics(78);
            int vh = GetSystemMetrics(79);
            virtualBounds = new Rectangle(x, y, w, vh);

            list.Sort(delegate(MonitorInfo a, MonitorInfo b) { return a.Bounds.X.CompareTo(b.Bounds.X); });
            return list;
        }

        private static string FriendlyName(string device)
        {
            try
            {
                var dd = new DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (EnumDisplayDevices(device, 0, ref dd, 1))
                {
                    string edidName = NameFromEdid(dd.DeviceID);
                    if (!string.IsNullOrEmpty(edidName)) return edidName;
                }

                dd = new DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (EnumDisplayDevices(device, 0, ref dd, 0))
                {
                    string s = (dd.DeviceString ?? string.Empty).Trim();
                    if (s.Length > 0 && s != "Generic PnP Monitor") return s;
                }
            }
            catch { }
            return "Монитор";
        }

        private static string NameFromEdid(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return null;

            string id = deviceId;
            if (id.StartsWith(@"\\?\")) id = id.Substring(4);

            string[] parts = id.Split('#');
            if (parts.Length < 4 || parts[0] != "DISPLAY") return null;

            string keyPath = @"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1] + @"\" + parts[2] + @"\Device Parameters";
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath))
            {
                if (key == null) return null;
                byte[] edid = key.GetValue("EDID") as byte[];
                if (edid == null || edid.Length < 128) return null;

                string mfg = string.Empty;
                try
                {
                    int code = (edid[8] << 8) | edid[9];
                    mfg = string.Concat(
                        (char)('A' + ((code >> 10) & 0x1F) - 1),
                        (char)('A' + ((code >> 5) & 0x1F) - 1),
                        (char)('A' + (code & 0x1F) - 1));
                }
                catch { }

                for (int i = 54; i <= 108; i += 18)
                {
                    if (edid[i] == 0 && edid[i + 1] == 0 && edid[i + 3] == 0xFC)
                    {
                        var chars = new char[13];
                        for (int j = 0; j < 13; j++) chars[j] = (char)edid[i + 5 + j];
                        string name = new string(chars).Trim('\0', '\n', '\r', ' ');
                        if (name.Length == 0) continue;
                        if (mfg.Length == 3 && name.IndexOf(' ') < 0 && name.Length <= 10)
                        {
                            name = mfg + " " + name;
                        }
                        return name;
                    }
                }
            }
            return null;
        }
    }
}
