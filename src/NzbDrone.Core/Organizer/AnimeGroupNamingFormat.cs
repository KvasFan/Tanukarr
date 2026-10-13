using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Organizer
{
    public enum AnimeGroupFolderFormatKind
    {
        Group,
        Season,
        Arc,
        MovieTitle,
        MovieNumber
    }

    public static class AnimeGroupNamingFormat
    {
        public const int MaxFormatLength = 256;

        private static readonly Regex TokenRegex = new Regex(@"\{(?<token>[A-Za-z]+(?:[- ._]+[A-Za-z]+)*)(?::(?<format>[^{}]*))?\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex GroupTokenRegex = new Regex(@"(?<!\{)\{[- ._\[(]*group[- ._]*[A-Za-z]+[^{}]*\}(?!\})", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsValid(string format, AnimeGroupFolderFormatKind kind)
        {
            if (string.IsNullOrWhiteSpace(format) || format.Length > MaxFormatLength || format.Any(char.IsControl) ||
                format.IndexOfAny(new[] { '/', '\\' }) >= 0)
            {
                return false;
            }

            var required = kind switch
            {
                AnimeGroupFolderFormatKind.Group => "grouptitle",
                AnimeGroupFolderFormatKind.Season => "groupseason",
                AnimeGroupFolderFormatKind.Arc => "grouparctitle",
                AnimeGroupFolderFormatKind.MovieTitle => "seriestitle",
                AnimeGroupFolderFormatKind.MovieNumber => "groupmovie",
                _ => string.Empty
            };
            var matches = TokenRegex.Matches(format);
            var hasRequired = false;
            foreach (Match match in matches)
            {
                var token = Normalize(match.Groups["token"].Value);
                var customFormat = match.Groups["format"].Success ? match.Groups["format"].Value : null;
                if (!Allowed(token, kind) || !ValidCustomFormat(token, customFormat))
                {
                    return false;
                }

                hasRequired |= token == required;
            }

            var literal = TokenRegex.Replace(format, string.Empty);
            return hasRequired && literal.IndexOfAny(new[] { '{', '}', ':', '<', '>', '"', '|', '?', '*' }) < 0;
        }

        public static bool HasValidGroupTokens(string format)
        {
            if (format == null)
            {
                return true;
            }

            foreach (Match candidate in GroupTokenRegex.Matches(format))
            {
                var parsed = FileNameBuilder.TitleRegex.Match(candidate.Value);
                var token = Normalize(parsed.Groups["token"].Value);
                if (!parsed.Success || parsed.Length != candidate.Length ||
                    token is not ("grouptitle" or "grouparctitle" or "groupseason" or "groupmovie") ||
                    !ValidCustomFormat(token, parsed.Groups["customFormat"].Success ? parsed.Groups["customFormat"].Value : null))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Allowed(string token, AnimeGroupFolderFormatKind kind)
        {
            return token is "grouptitle" or "grouparctitle" or "seriestitle" or "seriesyear" ||
                   (token == "groupseason" && kind == AnimeGroupFolderFormatKind.Season) ||
                   (token == "groupmovie" && (kind == AnimeGroupFolderFormatKind.MovieTitle || kind == AnimeGroupFolderFormatKind.MovieNumber));
        }

        private static bool ValidCustomFormat(string token, string format)
        {
            if (format == null)
            {
                return true;
            }

            if (token is "groupseason" or "groupmovie")
            {
                return format.Length is >= 1 and <= 6 && format.All(c => c == '0');
            }

            return token != "seriesyear" && format.Length <= 3 &&
                   int.TryParse(format, out var length) && length is >= 1 and <= 180 && format.All(char.IsAsciiDigit);
        }

        private static string Normalize(string token)
        {
            return new string(token.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }
    }
}
