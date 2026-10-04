using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Platform;
using System.Runtime.Versioning;

namespace ShadowONE.Services
{
    public static class FileAssociationService
    {
        private const string LinuxMimeContent = @"<?xml version=""1.0""?>
<mime-info xmlns=""http://www.freedesktop.org/standards/shared-mime-info"">
  <mime-type type=""application/x-one"">
    <comment>ONE Archive</comment>
    <glob pattern=""*.one""/>
    <glob pattern=""*.ONE""/>
  </mime-type>
</mime-info>";

        public static bool IsRegistrationNeeded()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    return GetWindowsChanges().Count > 0;
                }

                if (OperatingSystem.IsLinux())
                {
                    return GetLinuxState().NeedsAnything;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to check file association: {ex.Message}");
            }

            return false;
        }

        public static void Register()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    RegisterWindows();
                }
                else if (OperatingSystem.IsLinux())
                {
                    RegisterLinux();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to register file association: {ex.Message}");
            }
        }

        private static string? GetExePath() => Process.GetCurrentProcess().MainModule?.FileName;

        private static byte[] LoadIconPng()
        {
            using var stream = AssetLoader.Open(new Uri("avares://ShadowONE/Assets/logo.png"));
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private record RegistryValue(string SubKey, string Name, string Value);

        [SupportedOSPlatform("windows")]
        private static List<RegistryValue> GetWindowsChanges()
        {
            var exePath = GetExePath();
            if (string.IsNullOrEmpty(exePath))
            {
                return [];
            }

            const string progId = @"Software\Classes\ShadowONE.File";

            List<RegistryValue> desired =
            [
                new(@"Software\Classes\.one", "", "ShadowONE.File"),
                new(progId, "", "ShadowONE Archive"),
                new(progId, "PerceivedType", "text"),
                new(progId + @"\DefaultIcon", "", $"\"{exePath}\",0"),
                new(progId + @"\shell\open\command", "", $"\"{exePath}\" \"%1\""),
            ];

            return desired.Where(v =>
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(v.SubKey);
                return key?.GetValue(v.Name) as string != v.Value;
            }).ToList();
        }

        [SupportedOSPlatform("windows")]
        private static void RegisterWindows()
        {
            foreach (var change in GetWindowsChanges())
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(change.SubKey);
                key.SetValue(change.Name, change.Value);
            }
        }

        private record LinuxState(
            string ExePath, byte[] IconPng,
            string ApplicationsDir, string DesktopFile, string IconFile, string MimeDir, string MimeFile,
            bool NeedsIcon, bool NeedsDesktop, bool NeedsMime)
        {
            public bool NeedsAnything => NeedsIcon || NeedsDesktop || NeedsMime;
        }

        private static LinuxState GetLinuxState()
        {
            var exePath = GetExePath() ?? "";
            var share = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            var applicationsDir = Path.Combine(share, "applications");
            var desktopFile = Path.Combine(applicationsDir, "shadowone.desktop");
            var iconFile = Path.Combine(share, "icons", "hicolor", "256x256", "apps", "shadowone.png");
            var mimeDir = Path.Combine(share, "mime");
            var mimeFile = Path.Combine(mimeDir, "packages", "shadowone.xml");
            var iconPng = LoadIconPng();

            var needsIcon = !File.Exists(iconFile) || !File.ReadAllBytes(iconFile).AsSpan().SequenceEqual(iconPng);
            var needsDesktop = exePath.Length > 0 &&
                               (!File.Exists(desktopFile) || File.ReadAllText(desktopFile) != GetDesktopContent(exePath));

            var needsMime = !File.Exists(mimeFile) || File.ReadAllText(mimeFile) != LinuxMimeContent;

            return new LinuxState(exePath, iconPng, applicationsDir, desktopFile, iconFile, mimeDir, mimeFile,
                needsIcon, needsDesktop, needsMime);
        }

        private static string GetDesktopContent(string exePath)
        {
            return $@"[Desktop Entry]
Name=ShadowONE
Comment=ONE File Editor for Shadow and Sonic Heroes
Exec=""{exePath}"" %f
Icon=shadowone
Type=Application
Categories=Utility;
MimeType=application/x-one;
Terminal=false
StartupNotify=false";
        }

        private static void RegisterLinux()
        {
            var state = GetLinuxState();
            if (!state.NeedsAnything || state.ExePath.Length == 0)
            {
                return;
            }

            if (state.NeedsIcon)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(state.IconFile)!);
                File.WriteAllBytes(state.IconFile, state.IconPng);
            }

            if (state.NeedsDesktop)
            {
                Directory.CreateDirectory(state.ApplicationsDir);
                File.WriteAllText(state.DesktopFile, GetDesktopContent(state.ExePath));
                StartAndForget("update-desktop-database", state.ApplicationsDir);
            }

            if (state.NeedsMime)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(state.MimeFile)!);
                File.WriteAllText(state.MimeFile, LinuxMimeContent);
                StartAndForget("update-mime-database", state.MimeDir);
            }
        }

        private static void StartAndForget(string fileName, string argument)
        {
            try
            {
                Process.Start(fileName, argument)?.Dispose();
            }
            catch {} // silent fail
        }
    }
}
