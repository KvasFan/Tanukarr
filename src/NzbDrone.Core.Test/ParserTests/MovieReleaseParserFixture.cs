using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class MovieReleaseParserFixture
    {
        private static Series Movie(string title = "Generated Feature", int year = 2019, string imdb = "tt9700001")
        {
            return new Series { Title = title, Year = year, ImdbId = imdb, MediaType = MediaType.Movie };
        }

        [TestCase("Generated.Feature.2019.1080p.WEB-DL.x265-GROUP.mkv")]
        [TestCase("Generated Feature (2019) [Bluray-1080p].mkv")]
        [TestCase("Generated_Feature_2019_2160p_HEVC.mkv")]
        public void should_match_ordinary_physical_movie_names(string name)
        {
            var parsed = MovieReleaseParser.Match(name, Movie());
            parsed.Should().NotBeNull();
            parsed.IsMovie.Should().BeTrue();
            parsed.MovieYear.Should().Be(2019);
            parsed.SeasonNumber.Should().Be(1);
            parsed.EpisodeNumbers.Should().Equal(1);
        }

        [TestCase("Generated Feature (2020) 1080p.mkv")]
        [TestCase("Another Feature (2019) 1080p.mkv")]
        [TestCase("Generated Feature S01E01 1080p.mkv")]
        [TestCase("Generated Feature Season 1 1080p.mkv")]
        [TestCase("Generated Feature (2019) CD1.mkv")]
        [TestCase("Generated Feature (2019) imdb-tt9700002.mkv")]
        [TestCase("Generated Feature (2019) imdb-tt9700001-tt9700002.mkv")]
        [TestCase("Generated Feature 1080p.mkv")]
        public void should_reject_conflicts_episodic_and_ambiguous_identity(string name)
        {
            MovieReleaseParser.Match(name, Movie()).Should().BeNull();
        }

        [Test]
        public void should_accept_explicit_imdb_identity_without_inventing_a_year()
        {
            MovieReleaseParser.Match("tt9700001.mkv", Movie()).Should().NotBeNull();
            MovieReleaseParser.Match("tt9700002.mkv", Movie()).Should().BeNull();
        }

        [TestCase("Blade Runner 2049", 2017)]
        [TestCase("1917", 2019)]
        [TestCase("2012", 2009)]
        [TestCase("The Collection", 2012)]
        [TestCase("A Complete Unknown", 2024)]
        public void should_preserve_numeric_titles_and_words_that_can_also_name_packs(string title, int year)
        {
            MovieReleaseParser.Match($"{title} ({year}) 1080p.mkv", Movie(title, year)).Should().NotBeNull();
        }

        [Test]
        public void should_match_only_one_stored_film_and_reject_duplicate_provider_ownership()
        {
            var first = Movie();
            var next = Movie("Generated Feature Two", 2021, "tt9700002");
            MovieReleaseParser.MatchUnique("Generated Feature Two (2021) WEB-DL.mkv", new[] { first, next }).Should().BeSameAs(next);
            MovieReleaseParser.MatchUnique("Generated Feature (2019) WEB-DL.mkv", new[] { first, Movie() }).Should().BeNull();
            MovieReleaseParser.MatchUnique("Generated Feature (2019) WEB-DL.mkv", new List<Series>()).Should().BeNull();
        }

        [Test]
        public void should_never_reclassify_a_tv_series_from_a_movie_like_filename()
        {
            var series = Movie();
            series.MediaType = MediaType.Series;
            MovieReleaseParser.Match("Generated Feature (2019) WEB-DL.mkv", series).Should().BeNull();
        }

        [TestCase(MetadataSourceType.AniList, "Generated Feature - S01E01.mkv")]
        [TestCase(MetadataSourceType.MyAnimeList, "Generated.Feature.S01E01.720p.WEB-DL.mkv")]
        [TestCase(MetadataSourceType.AniList, "[Group] Generated Feature - 01 [720p].mkv")]
        public void should_preserve_native_anime_film_release_conventions(MetadataSourceType source, string name)
        {
            var movie = Movie();
            movie.MetadataSource = source;
            MovieReleaseParser.Match(name, movie).Should().NotBeNull();
        }

        [TestCase("Another Feature - S01E01.mkv")]
        [TestCase("Generated Feature - S01E02.mkv")]
        [TestCase("Generated Feature - S02E01.mkv")]
        [TestCase("Generated Feature - S01.mkv")]
        [TestCase("Generated Feature - S01E01-E02.mkv")]
        [TestCase("Generated Feature - S01E01.imdb-tt9700002.mkv")]
        public void should_not_allow_anime_ownership_to_override_wrong_native_feature_identity(string name)
        {
            var movie = Movie();
            movie.MetadataSource = MetadataSourceType.AniList;
            MovieReleaseParser.Match(name, movie).Should().BeNull();
        }

        [TestCase(MetadataSourceType.TmdbMovie)]
        [TestCase(MetadataSourceType.Omdb)]
        public void should_keep_ordinary_movie_catalogues_strict_for_legacy_episode_names(MetadataSourceType source)
        {
            var movie = Movie();
            movie.MetadataSource = source;
            MovieReleaseParser.Match("Generated Feature - S01E01.mkv", movie).Should().BeNull();
        }

        [TestCase(MetadataSourceType.AniList)]
        [TestCase(MetadataSourceType.MyAnimeList)]
        public void should_keep_verified_native_anime_features_when_the_catalogue_release_year_is_unknown(MetadataSourceType source)
        {
            var movie = Movie(year: 0);
            movie.MetadataSource = source;
            var result = MovieReleaseParser.Match("Generated Feature - S01E01.mkv", movie);
            result.Should().NotBeNull();
            result.MovieYear.Should().Be(0);
            result.EpisodeNumbers.Should().Equal(1);
            MovieReleaseParser.Match("Another Feature - S01E01.mkv", movie).Should().BeNull();
        }

        [Test]
        public void should_require_real_native_feature_coverage_for_a_cached_movie_context()
        {
            var movie = Movie();
            movie.Id = 101;
            const string title = "Generated Feature (2019) WEB-DL.mkv";
            var remote = new RemoteEpisode
            {
                Series = movie,
                Release = new TorrentInfo { Title = title },
                ParsedEpisodeInfo = MovieReleaseParser.Match(title, movie),
                Episodes = new List<Episode> { new Episode { Id = 501, SeriesId = 101, SeasonNumber = 1, EpisodeNumber = 1 } }
            };

            MovieReleaseParser.HasMappedFeatureCoverage(remote, 101).Should().BeTrue();
            MovieReleaseParser.HasMappedFeatureCoverage(remote, 999).Should().BeFalse();
            remote.Episodes[0].Id = 0;
            MovieReleaseParser.HasMappedFeatureCoverage(remote, 101).Should().BeFalse();
            remote.Episodes[0].Id = 501;
            remote.ParsedEpisodeInfo.AbsoluteEpisodeNumbers = new[] { 2 };
            MovieReleaseParser.HasMappedFeatureCoverage(remote, 101).Should().BeFalse();
            MovieReleaseParser.HasMappedFeatureCoverage(null, 101).Should().BeFalse();
        }
    }
}
