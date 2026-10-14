using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Jellyfin;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Jellyfin
{
    [TestFixture]
    public class JellyfinWatchPathFixture : CoreTest
    {
        [TestCase("/media/Anime/Film.mkv", "/media/Anime/Film.mkv")]
        [TestCase("D:\\Anime\\Season 03\\Film.mkv", "D:/ANIME/SEASON 03/FILM.MKV")]
        [TestCase("d:/anime/Film.mkv", "D:/ANIME/FILM.MKV")]
        [TestCase("/media/Anime /Film.mkv", "/media/Anime /Film.mkv")]
        [TestCase("/media/Anime/../Film.mkv", null)]
        [TestCase("/media/./Film.mkv", null)]
        [TestCase("relative/Film.mkv", null)]
        [TestCase("/media/Film\n.mkv", null)]
        public void should_handle_platform_paths_without_discarding_meaningful_case_or_whitespace(string input, string expected)
        {
            JellyfinWatchPath.Normalize(input).Should().Be(expected);
        }

        [TestCase("/library/Anime/Season 03/Film.mkv", "/media/Anime/Season 03/Film.mkv")]
        [TestCase("/library-other/Anime/Film.mkv", null)]
        [TestCase("/library/../outside.mkv", null)]
        public void should_translate_only_an_exact_directory_prefix(string input, string expected)
        {
            JellyfinWatchPath.Translate(input, new JellyfinSettings
            {
                JellyfinPathPrefix = "/library", TanukarrPathPrefix = "/media"
            }).Should().Be(expected);
        }

        [Test]
        public void should_preserve_suffix_case_when_translating_windows_server_paths_to_linux()
        {
            JellyfinWatchPath.Translate("d:\\Anime\\Season 03\\MixedCase.mkv", new JellyfinSettings
            {
                JellyfinPathPrefix = "D:\\Anime", TanukarrPathPrefix = "/media/Anime"
            }).Should().Be("/media/Anime/Season 03/MixedCase.mkv");
        }

        [Test]
        public void should_require_a_user_only_when_watched_sync_is_enabled_and_validate_paired_path_prefixes()
        {
            var settings = new JellyfinSettings
            {
                Host = "localhost", ApiKey = "fixture-key", LibraryId = "d834474f444b4d328119fe8c4b9c71fa"
            };
            settings.Validate().IsValid.Should().BeTrue();
            settings.EnableWatchedSync = true;
            settings.Validate().Errors.Should().Contain(error => error.PropertyName == "WatchedUserId");
            settings.WatchedUserId = "af7440e699464d958939fd71fe016d91";
            settings.JellyfinPathPrefix = "/media";
            settings.Validate().Errors.Should().Contain(error => error.PropertyName == "TanukarrPathPrefix");
            settings.TanukarrPathPrefix = "/shows";
            settings.Validate().IsValid.Should().BeTrue();
            settings.WatchedSyncIntervalMinutes = 0;
            settings.Validate().Errors.Should().Contain(error => error.PropertyName == "WatchedSyncIntervalMinutes");
        }
    }
}
