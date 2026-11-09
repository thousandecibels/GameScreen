using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GameMonitor
{
    internal sealed class Preferences
    {
        public string GamingMonitor { get; set; }
        public string PreviousMain { get; set; }
        public bool Routing { get; set; }
        public List<string> AddedGames { get; set; }

        public Preferences()
        {
            AddedGames = new List<string>();
        }
    }

    internal sealed class PreferenceStore
    {
        private readonly string path;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public PreferenceStore(string path) { this.path = path; }

        public Preferences Load()
        {
            if (!File.Exists(path))
                return new Preferences();
            Preferences saved = json.Deserialize<Preferences>(File.ReadAllText(path));
            if (saved == null || saved.AddedGames == null)
                throw new InvalidDataException("The saved settings could not be read.");
            return saved;
        }

        public void Save(Preferences settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string pending = path + ".tmp";
            File.WriteAllText(pending, json.Serialize(settings));
            if (File.Exists(path))
                File.Replace(pending, path, null);
            else
                File.Move(pending, path);
        }
    }

    internal sealed class GameCatalog
    {
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
        private readonly HashSet<string> folders = new HashSet<string>(PathComparer);
        public int Count { get { return folders.Count; } }

        public static GameCatalog Read()
        {
            GameCatalog catalog = new GameCatalog();
            string steam = ReadRegistry(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath")
                ?? ReadRegistry(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            catalog.ReadSteam(steam);
            catalog.ReadEpic(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Epic\EpicGamesLauncher\Data\Manifests"));
            return catalog;
        }

        internal void ReadSteam(string steam)
        {
            HashSet<string> libraries = new HashSet<string>(PathComparer) { steam };
            string config = ReadText(Path.Combine(steam, @"steamapps\libraryfolders.vdf"));
            foreach (string root in ReadVdf(config, "path"))
                libraries.Add(root);

            foreach (string root in libraries.Where(Path.IsPathRooted))
            {
                string steamApps = Path.Combine(root, "steamapps");
                foreach (string file in ListFiles(steamApps, "appmanifest_*.acf"))
                {
                    string name = ReadVdf(ReadText(file), "installdir").FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(name) && Path.GetFileName(name) == name)
                        AddFolder(Path.Combine(steamApps, "common", name));
                }
            }
        }

        internal void ReadEpic(string directory)
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            foreach (string file in ListFiles(directory, "*.item"))
            {
                try
                {
                    Dictionary<string, object> manifest = json.Deserialize<Dictionary<string, object>>(ReadText(file));
                    object location, launch, incomplete;
                    if (manifest != null && manifest.TryGetValue("InstallLocation", out location)
                        && manifest.TryGetValue("LaunchExecutable", out launch) && !string.IsNullOrWhiteSpace(launch as string)
                        && (!manifest.TryGetValue("bIsIncompleteInstall", out incomplete) || !Equals(incomplete, true)))
                        AddFolder(location as string);
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
        }

        public bool Matches(string executable, IEnumerable<string> added)
        {
            if (string.IsNullOrEmpty(executable))
                return false;
            if (added.Contains(executable, PathComparer))
                return true;
            // The process API returns a native path, which can be longer than Path.GetDirectoryName supports.
            int separator = executable.LastIndexOf('\\');
            while (separator > 0)
            {
                if (folders.Contains(executable.Substring(0, separator)))
                    return true;
                separator = executable.LastIndexOf('\\', separator - 1);
            }
            return false;
        }

        private void AddFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                return;
            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.Length > Path.GetPathRoot(path).TrimEnd('\\', '/').Length && Directory.Exists(fullPath))
                folders.Add(fullPath);
        }

        private static IEnumerable<string> ReadVdf(string content, string key)
        {
            string pattern = "\"" + key + "\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
            foreach (Match match in Regex.Matches(content, pattern, RegexOptions.IgnoreCase))
                yield return match.Groups[1].Value.Replace(@"\\", @"\").Replace("\\\"", "\"");
        }

        private static string ReadText(string path)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
        }

        private static string ReadRegistry(string key, string value)
        {
            try { return Registry.GetValue(key, value, null) as string; }
            catch (SecurityException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static string[] ListFiles(string directory, string pattern)
        {
            try { return Directory.GetFiles(directory, pattern); }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }
    }

    internal static class GameWindow
    {
        private const uint QueryLimitedInformation = 0x1000;
        private const int WindowStyle = -16;
        private const int Caption = 0x00C00000;
        private const uint NoZOrderOrActivation = 0x0004 | 0x0010 | 0x4000;

        public static Dictionary<IntPtr, uint> ReadOpenWindows()
        {
            Dictionary<IntPtr, uint> windows = new Dictionary<IntPtr, uint>();
            EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                if (IsWindowVisible(window) && !IsIconic(window) && GetWindow(window, 4) == IntPtr.Zero)
                {
                    uint processId;
                    GetWindowThreadProcessId(window, out processId);
                    if (processId != 0)
                        windows[window] = processId;
                }
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        public static string Executable(uint processId)
        {
            IntPtr process = OpenProcess(QueryLimitedInformation, false, processId);
            if (process == IntPtr.Zero)
                return null;
            try
            {
                StringBuilder path = new StringBuilder(32768);
                int length = path.Capacity;
                return QueryFullProcessImageName(process, 0, path, ref length) ? path.ToString() : null;
            }
            finally
            {
                CloseHandle(process);
            }
        }

        public static bool Move(IntPtr window, Monitor target, out int error)
        {
            error = 0;
            int cloaked;
            if (DwmGetWindowAttribute(window, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;
            Screen source = Screen.FromHandle(window);
            if (string.Equals(source.DeviceName, target.Device, StringComparison.OrdinalIgnoreCase))
                return false;
            NativeRect native;
            if (!GetWindowRect(window, out native))
                return false;
            Rectangle frame = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
            if (frame.Width < 200 || frame.Height < 120)
                return false;
            bool coversScreen = (GetWindowLong(window, WindowStyle) & Caption) != Caption
                && Math.Abs(frame.Width - source.Bounds.Width) <= 24
                && Math.Abs(frame.Height - source.Bounds.Height) <= 24;
            Rectangle area = Screen.AllScreens.Where(screen => screen.DeviceName == target.Device)
                .Select(screen => screen.WorkingArea).DefaultIfEmpty(target.Bounds).First();
            Rectangle desired = coversScreen ? target.Bounds : Center(frame.Size, area);
            if (SetWindowPos(window, IntPtr.Zero, desired.X, desired.Y, desired.Width, desired.Height, NoZOrderOrActivation))
                return true;
            error = Marshal.GetLastWin32Error();
            return false;
        }

        private static Rectangle Center(Size size, Rectangle area)
        {
            int width = Math.Min(size.Width, area.Width);
            int height = Math.Min(size.Height, area.Height);
            return new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        private delegate bool EnumWindowProc(IntPtr window, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowProc callback, IntPtr data);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int length);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
