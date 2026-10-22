using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace ShadowONE.Services
{
    public enum UpdateCheckResult
    {
        UpdateAvailable,
        UpToDate,
        Failed
    }

    public static class UpdateChecker
    {
        private const string CheckUrl = "https://raw.githubusercontent.com/ShadowTheHedgehogHacking/ShadowONE/main/res/version.txt";
        public const string VisitUrl = "https://github.com/ShadowTheHedgehogHacking/ShadowONE/releases/latest";
        private const string UserAgent = "ShadowONE-Agent";
        private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(7);

        public static Version? LocalVersion { get; } = ReadLocalVersion();

        public static bool IsSupported => LocalVersion != null;

        public static bool IsCheckDue()
        {
            return DateTimeOffset.UtcNow - AppSettings.LastUpdateCheck >= CheckInterval;
        }

        public static async Task<(UpdateCheckResult Result, Version? RemoteVersion)> CheckAsync()
        {
            if (LocalVersion == null)
            {
                return (UpdateCheckResult.Failed, null);
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                var response = await client.GetStringAsync(CheckUrl);

                var remoteVersion = ParseVersion(response);
                if (remoteVersion == null)
                {
                    Console.WriteLine("Update check failed: report this strange error.");
                    return (UpdateCheckResult.Failed, null);
                }

                AppSettings.LastUpdateCheck = DateTimeOffset.UtcNow;
                var result = remoteVersion > LocalVersion ? UpdateCheckResult.UpdateAvailable : UpdateCheckResult.UpToDate;
                return (result, remoteVersion);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update check failed: {ex.Message}");
                return (UpdateCheckResult.Failed, null);
            }
        }

        private static Version? ReadLocalVersion()
        {
            var parts = VersionInfo.Version.TrimStart('v', 'V').Split('.');
            if (parts.Length != 3
                || !int.TryParse(parts[0], out var major)
                || !int.TryParse(parts[1], out var minor)
                || !int.TryParse(parts[2], out var patch))
            {
                return null;
            }

            return new Version(major, minor, patch);
        }

        private static Version? ParseVersion(string text)
        {
            int? major = null, minor = null, patch = null;

            foreach (var line in text.Split('\n'))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0 || !int.TryParse(line[(separator + 1)..].Trim(), out var value))
                {
                    continue;
                }

                switch (line[..separator].Trim().ToUpperInvariant())
                {
                    case "MAJOR":
                        major = value;
                        break;
                    case "MINOR":
                        minor = value;
                        break;
                    case "PATCH":
                        patch = value;
                        break;
                }
            }

            if (major == null || minor == null || patch == null)
            {
                return null;
            }

            return new Version(major.Value, minor.Value, patch.Value);
        }
    }
}
