using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Qualities
{
    public interface IProfileQualityMatcher
    {
        void Apply(RemoteEpisode episode);
        void Apply(LocalEpisode episode);
    }

    public class ProfileQualityMatcher : IProfileQualityMatcher
    {
        private readonly IQualityDefinitionService _definitions;

        public ProfileQualityMatcher(IQualityDefinitionService definitions)
        {
            _definitions = definitions;
        }

        public void Apply(RemoteEpisode episode)
        {
            var profile = episode.Series?.QualityProfile?.Value;
            var quality = episode.ParsedEpisodeInfo?.Quality;
            if (profile == null || quality == null)
            {
                return;
            }

            quality.Quality = Match(profile, quality.Quality, episode.Release.Title, CodecSizeNormalizer.DetectCodec(episode));
        }

        public void Apply(LocalEpisode episode)
        {
            var profile = episode.Series?.QualityProfile?.Value;
            if (profile == null || episode.Quality == null || episode.ExistingFile)
            {
                return;
            }

            var title = Path.GetFileName(episode.Path);
            var remote = new RemoteEpisode
            {
                Release = new ReleaseInfo { Title = title, Codec = episode.MediaInfo?.VideoCodecID },
                ParsedEpisodeInfo = episode.FileEpisodeInfo
            };
            episode.Quality.Quality = Match(profile, episode.Quality.Quality, title, CodecSizeNormalizer.DetectCodec(remote));
        }

        public Quality Match(QualityProfile profile, Quality parsed, string title, string codec)
        {
            if (parsed.Id >= Quality.CustomIdStart)
            {
                return parsed;
            }

            var allowed = profile.Items.Where(item => item.Allowed).SelectMany(item => item.GetQualities()).Select(quality => quality.Id).ToHashSet();
            var definitions = _definitions.All();
            var candidates = definitions.Where(definition => !definition.IsRetired && allowed.Contains(definition.Quality.Id) &&
                (definition.Source ?? definition.Quality.Source) == parsed.Source &&
                (definition.Resolution ?? definition.Quality.Resolution) == parsed.Resolution &&
                (string.IsNullOrEmpty(definition.Codec) || definition.Codec == codec) && MatchesTitle(definition.TitlePattern, title))
                .OrderByDescending(definition => (string.IsNullOrEmpty(definition.Codec) ? 0 : 1) + (string.IsNullOrEmpty(definition.TitlePattern) ? 0 : 1))
                .ThenByDescending(definition => definition.Quality.Id >= Quality.CustomIdStart)
                .ThenByDescending(definition => profile.GetIndex(definition.Quality, true));
            var matched = candidates.FirstOrDefault();
            if (matched != null)
            {
                return matched.Quality;
            }

            var original = definitions.FirstOrDefault(definition => definition.Quality.Id == parsed.Id);
            return original != null && (original.IsRetired || !string.IsNullOrEmpty(original.Codec) || !string.IsNullOrEmpty(original.TitlePattern) ||
                (original.Source ?? parsed.Source) != parsed.Source || (original.Resolution ?? parsed.Resolution) != parsed.Resolution) ? Quality.Unknown : parsed;
        }

        private static bool MatchesTitle(string pattern, string title)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }

            if (title == null || title.Length > 16384)
            {
                return false;
            }

            try
            {
                return Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
