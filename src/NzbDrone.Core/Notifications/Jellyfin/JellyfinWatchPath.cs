using System;
using System.Linq;

namespace NzbDrone.Core.Notifications.Jellyfin
{
    public static class JellyfinWatchPath
    {
        public static bool IsOptionalAbsolutePath(string path)
        {
            return string.IsNullOrEmpty(path) || Normalize(path) != null;
        }

        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Any(char.IsControl))
            {
                return null;
            }

            var windows = IsWindows(path);
            var normalized = windows ? path.Replace('\\', '/') : path;
            if ((!windows && !normalized.StartsWith("/", StringComparison.Ordinal)) ||
                normalized.Split('/').Any(segment => segment is "." or ".."))
            {
                return null;
            }

            normalized = normalized.TrimEnd('/');
            if (normalized.Length == 0)
            {
                return "/";
            }

            return windows ? normalized.ToUpperInvariant() : normalized;
        }

        public static string Translate(string path, JellyfinSettings settings)
        {
            var normalized = Normalize(path);
            if (normalized == null || string.IsNullOrEmpty(settings.JellyfinPathPrefix))
            {
                return normalized;
            }

            var source = Normalize(settings.JellyfinPathPrefix)?.TrimEnd('/');
            var target = Normalize(settings.TanukarrPathPrefix)?.TrimEnd('/');
            if (source == null || target == null ||
                !normalized.StartsWith(source + "/", StringComparison.Ordinal))
            {
                return null;
            }

            // Compare Windows paths without case sensitivity, but retain the incoming
            // suffix's case when translating onto a case-sensitive Linux filesystem.
            var rawPath = IsWindows(path) ? path.Replace('\\', '/') : path;
            var rawTarget = IsWindows(settings.TanukarrPathPrefix) ?
                settings.TanukarrPathPrefix.Replace('\\', '/') : settings.TanukarrPathPrefix;
            return Normalize(string.Concat(rawTarget.TrimEnd('/').AsSpan(), rawPath.AsSpan(source.Length)));
        }

        private static bool IsWindows(string path)
        {
            return path.StartsWith("\\\\", StringComparison.Ordinal) ||
                (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\');
        }
    }
}
