using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class MovieCollectionPackParserFixture
    {
        private static MovieSearchCriteria Criteria()
        {
            var movies = new List<Series>();
            for (var index = 0; index < 3; index++)
            {
                movies.Add(new Series
                {
                    Id = index + 1,
                    Title = "Generated Feature " + index,
                    Year = 2019 + index,
                    Runtime = 95 + index,
                    MediaType = MediaType.Movie,
                    AnimeGroupingMode = AnimeGroupingMode.Manual,
                    AnimeGroupId = "manual:feature-collection",
                    AnimeGroupTitle = "Generated Feature Collection",
                    AnimeGroupMemberKind = AnimeGroupMemberKind.Movie,
                    AnimeGroupMovieNumber = index + 1
                });
            }

            return new MovieSearchCriteria { Series = movies[0], CollectionMovies = movies };
        }

        [TestCase("Generated.Feature.Collection.2019-2021.720p.WEB-DL.x264-Group")]
        [TestCase("Generated Feature Collection (2019 - 2021) Bluray-1080p")]
        [TestCase("Generated_Feature_Collection_complete_2019_2021_WEB-DL")]
        public void should_require_the_exact_stored_collection_and_complete_known_year_range(string title)
        {
            var parsed = MovieCollectionPackParser.Match(title, Criteria());
            parsed.Should().NotBeNull();
            parsed.IsMovieCollectionPack.Should().BeTrue();
            parsed.MovieCollectionId.Should().Be("manual:feature-collection");
            parsed.MovieCollectionRuntime.Should().Be(288);
            parsed.EpisodeNumbers.Should().Equal(1);
        }

        [TestCase("Generated Feature Collection 2019 1080p")]
        [TestCase("Generated Feature Collection 2019-2020 1080p")]
        [TestCase("Generated Feature Collection 2018-2021 1080p")]
        [TestCase("Another Feature Collection 2019-2021 1080p")]
        [TestCase("Generated Feature Collection Complete 1080p")]
        [TestCase("Generated Feature Collection 2019-2021 S01 1080p")]
        [TestCase("Generated Feature Collection 2019-2021 S01-S03 1080p")]
        [TestCase("Generated Feature Collection 2019-2021 Season 1-3 Complete 1080p")]
        [TestCase("Generated Feature Collection 2019-2021 S01E01 1080p")]
        [TestCase("Generated Feature Collection 2019-2021 Special 1 1080p")]
        public void should_reject_partial_ambiguous_and_episodic_pack_titles(string title)
        {
            MovieCollectionPackParser.Match(title, Criteria()).Should().BeNull();
        }

        [Test]
        public void should_reject_a_mixed_or_incomplete_candidate_set()
        {
            var criteria = Criteria();
            const string title = "Generated Feature Collection 2019-2021 1080p";
            criteria.CollectionMovies[1].MediaType = MediaType.Series;
            MovieCollectionPackParser.Match(title, criteria).Should().BeNull();
            criteria = Criteria();
            criteria.CollectionMovies[1].AnimeGroupId = "manual:unrelated";
            MovieCollectionPackParser.Match(title, criteria).Should().BeNull();
            criteria = Criteria();
            criteria.CollectionMovies[1].Runtime = 0;
            MovieCollectionPackParser.Match(title, criteria).Should().BeNull();
            criteria.CollectionMovies = new List<Series> { criteria.Series };
            MovieCollectionPackParser.Match(title, criteria).Should().BeNull();
        }

        [Test]
        public void should_reject_missing_context_unbounded_titles_and_runtime_overflow()
        {
            const string title = "Generated Feature Collection 2019-2021 1080p";
            MovieCollectionPackParser.Match(title, null).Should().BeNull();
            MovieCollectionPackParser.Match(title, new MovieSearchCriteria()).Should().BeNull();
            var criteria = Criteria();
            criteria.Series.AnimeGroupTitle = new string('x', 181);
            MovieCollectionPackParser.Match(title, criteria).Should().BeNull();
            criteria = Criteria();
            criteria.CollectionMovies[1].Runtime = int.MaxValue;
            MovieCollectionPackParser.Match(title, criteria).Should().BeNull();
        }
    }
}
