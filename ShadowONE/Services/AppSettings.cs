using System;
using System.Diagnostics;
using System.IO;

namespace ShadowONE.Services
{
    public enum FileAssociationMode
    {
        Ask,
        Yes,
        Never
    }
    
    public static class AppSettings
    {
        private const string FileAssociationKey = "file_association";

        private static string? GetSettingsPath()
        {
            var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            return string.IsNullOrEmpty(exeDir) ? null : Path.Combine(exeDir, "settings.toml");
        }

        public static FileAssociationMode FileAssociation
        {
            get
            {
                var value = ReadValue(FileAssociationKey);
                return value?.ToLowerInvariant() switch
                {
                    "yes" => FileAssociationMode.Yes,
                    "never" => FileAssociationMode.Never,
                    _ => FileAssociationMode.Ask
                };
            }
            set => WriteValue(FileAssociationKey, value.ToString().ToLowerInvariant());
        }

        private static string? ReadValue(string key)
        {
            try
            {
                var path = GetSettingsPath();
                if (path == null || !File.Exists(path))
                {
                    return null;
                }

                foreach (var line in File.ReadLines(path))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    {
                        continue;
                    }

                    var separator = trimmed.IndexOf('=');
                    if (separator <= 0 || trimmed[..separator].Trim() != key)
                    {
                        continue;
                    }

                    return trimmed[(separator + 1)..].Trim().Trim('"');
                }
            }
            catch { }

            return null;
        }

        private static void WriteValue(string key, string value)
        {
            try
            {
                var path = GetSettingsPath();
                if (path == null)
                {
                    return;
                }

                var lines = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
                var newLine = $"{key} = \"{value}\"";
                var replaced = false;

                for (var i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].Trim();
                    var separator = trimmed.IndexOf('=');
                    if (!trimmed.StartsWith('#') && separator > 0 && trimmed[..separator].Trim() == key)
                    {
                        lines[i] = newLine;
                        replaced = true;
                    }
                }

                var output = replaced ? lines : [.. lines, newLine];
                File.WriteAllLines(path, output);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
