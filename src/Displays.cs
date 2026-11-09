using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace GameMonitor
{
    internal sealed class Monitor
    {
        public string Key;
        public string Name;
        public string Device;
        public Rectangle Bounds;
        public int SourceIndex;
        public bool Mirrored;

        public bool Primary { get { return Bounds.Location == Point.Empty; } }

        public override string ToString()
        {
            string number = Device.Replace(@"\\.\DISPLAY", "");
            return string.Format("{0}  |  Display {1}{2}", Name, number, Primary ? "  (Windows main)" : "");
        }
    }

    internal sealed class Displays
    {
        private const uint OnlyActivePaths = 2;
        private const uint UseSuppliedConfig = 0x20;
        private const uint ValidateConfig = 0x40;
        private const uint ApplyConfig = 0x80;
        private const int InsufficientBuffer = 122;

        private readonly PathInfo[] paths;
        private readonly ModeInfo[] modes;
        public readonly List<Monitor> Monitors = new List<Monitor>();

        public Monitor Primary { get { return Monitors.FirstOrDefault(monitor => monitor.Primary); } }

        private Displays(PathInfo[] paths, ModeInfo[] modes)
        {
            this.paths = paths;
            this.modes = modes;
            foreach (PathInfo path in paths)
            {
                int index = path.Source.ModeIndex;
                if (index < 0 || index >= modes.Length || modes[index].Type != 1)
                    continue;

                SourceName source = new SourceName();
                source.Header = Header.Create(1, Marshal.SizeOf(typeof(SourceName)), path.Source.Adapter, path.Source.Id);
                Check(GetSourceName(ref source), "Read the display number");

                TargetName target = new TargetName();
                target.Header = Header.Create(2, Marshal.SizeOf(typeof(TargetName)), path.Target.Adapter, path.Target.Id);
                int targetResult = GetTargetName(ref target);
                string key = targetResult == 0 ? target.DevicePath : null;
                string name = targetResult == 0 ? target.FriendlyName : null;
                ModeInfo mode = modes[index];
                Monitors.Add(new Monitor
                {
                    Key = string.IsNullOrWhiteSpace(key) ? source.Device : key,
                    Name = string.IsNullOrWhiteSpace(name) ? "Monitor" : name,
                    Device = source.Device,
                    SourceIndex = index,
                    Bounds = new Rectangle(mode.X, mode.Y, mode.Width, mode.Height)
                });
            }
            foreach (Monitor monitor in Monitors)
                monitor.Mirrored = Monitors.Count(other => other.SourceIndex == monitor.SourceIndex) > 1;
        }

        public Monitor Find(string key)
        {
            return Monitors.FirstOrDefault(monitor => string.Equals(monitor.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public static Displays Read()
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                uint pathCount, modeCount;
                Check(GetDisplayConfigBufferSizes(OnlyActivePaths, out pathCount, out modeCount), "List active displays");
                PathInfo[] paths = new PathInfo[pathCount];
                ModeInfo[] modes = new ModeInfo[modeCount];
                int result = QueryDisplayConfig(OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                if (result == InsufficientBuffer)
                    continue;
                Check(result, "Read the display layout");
                Array.Resize(ref paths, (int)pathCount);
                Array.Resize(ref modes, (int)modeCount);
                return new Displays(paths, modes);
            }
            throw new InvalidOperationException("The display layout is changing. Try again in a moment.");
        }

        public void SetPrimary(Monitor monitor, bool validateOnly)
        {
            if (monitor == null || monitor.Mirrored || Monitors.Any(display => display.Mirrored))
                throw new InvalidOperationException("Use Extend these displays in Windows before switching the main monitor.");
            if (monitor.Primary)
                return;

            ModeInfo[] translated = TranslateModes(modes, monitor.Bounds.Location);
            Check(SetDisplayConfig(paths.Length, paths, translated.Length, translated, UseSuppliedConfig | ValidateConfig),
                "Validate the display switch");
            if (validateOnly)
                return;

            // Apply in the current session. Keep the user's saved Windows topology as a fallback.
            Check(SetDisplayConfig(paths.Length, paths, translated.Length, translated, UseSuppliedConfig | ApplyConfig),
                "Switch the Windows main display");
            Monitor actual = Read().Find(monitor.Key);
            if (actual == null || !actual.Primary)
                throw new InvalidOperationException("Windows did not confirm the requested main display.");
        }

        internal static ModeInfo[] TranslateModes(ModeInfo[] original, Point origin)
        {
            ModeInfo[] translated = (ModeInfo[])original.Clone();
            for (int i = 0; i < translated.Length; i++)
            {
                if (translated[i].Type != 1)
                    continue;
                translated[i].X -= origin.X;
                translated[i].Y -= origin.Y;
            }
            return translated;
        }

        private static void Check(int result, string operation)
        {
            if (result != 0)
                throw new InvalidOperationException(operation + " failed. " + new Win32Exception(result).Message);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AdapterId { public uint Low; public int High; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SourceInfo
        {
            public AdapterId Adapter;
            public uint Id;
            public int ModeIndex;
            public uint Status;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TargetInfo
        {
            public AdapterId Adapter;
            public uint Id, ModeIndex, Technology, Rotation, Scaling, RefreshNumerator, RefreshDenominator;
            public uint Scanline, Available, Status;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PathInfo { public SourceInfo Source; public TargetInfo Target; public uint Flags; }

        [StructLayout(LayoutKind.Explicit, Size = 64)]
        internal struct ModeInfo
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(4)] public uint Id;
            [FieldOffset(8)] private AdapterId adapter;
            // Preserve the native union's entire payload when copying target modes.
            [FieldOffset(16)] public ulong Payload0;
            [FieldOffset(24)] private ulong payload1;
            [FieldOffset(32)] private ulong payload2;
            [FieldOffset(40)] private ulong payload3;
            [FieldOffset(48)] private ulong payload4;
            [FieldOffset(56)] private ulong payload5;
            [FieldOffset(16)] public int Width;
            [FieldOffset(20)] public int Height;
            [FieldOffset(28)] public int X;
            [FieldOffset(32)] public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Header
        {
            public int Type, Size;
            public AdapterId Adapter;
            public uint Id;

            public static Header Create(int type, int size, AdapterId adapter, uint id)
            {
                return new Header { Type = type, Size = size, Adapter = adapter, Id = id };
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SourceName
        {
            public Header Header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TargetName
        {
            public Header Header;
            public uint Flags, Technology;
            public ushort Manufacturer, Product;
            public uint Connector;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths,
            ref uint modeCount, [Out] ModeInfo[] modes, IntPtr topology);
        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(int pathCount, [In] PathInfo[] paths,
            int modeCount, [In] ModeInfo[] modes, uint flags);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        private static extern int GetSourceName(ref SourceName source);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        private static extern int GetTargetName(ref TargetName target);
    }
}
