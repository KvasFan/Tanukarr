using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class AnimeTrailingGroupSeasonFixture : CoreTest
    {
        [TestCase("Shokugeki no Soma S2 - 01 [HorribleSubs][1080p].mkv", "Shokugeki no Soma S2", 2, 1, "HorribleSubs")]
        [TestCase("Shokugeki no Soma S2 - 10 [HorribleSubs][1080p].mkv", "Shokugeki no Soma S2", 2, 10, "HorribleSubs")]
        [TestCase("Shokugeki no Soma S2 - 13 [HorribleSubs][1080p].mkv", "Shokugeki no Soma S2", 2, 13, "HorribleSubs")]
        [TestCase("Another Anime S3 - 08 [OtherGroup] [720p].mkv", "Another Anime S3", 3, 8, "OtherGroup")]
        public void should_parse_season_alias_and_episode_with_trailing_subgroup(string title, string alias, int season, int episode, string group)
        {
            var result = Parser.Parser.ParseTitle(title);

            result.Should().NotBeNull();
            result.SeriesTitle.Should().Be(alias);
            result.SeasonNumber.Should().Be(season);
            result.AbsoluteEpisodeNumbers.Should().Equal(episode);
            result.ReleaseGroup.Should().Be(group);
            result.FullSeason.Should().BeFalse();
            result.IsMultiSeason.Should().BeFalse();
        }

        [TestCase("Shokugeki no Soma S01-S03 + OVA (Food Wars) [1080p]", new[] { 1, 2, 3 })]
        [TestCase("Series S02-S10 [OtherGroup][1080p]", new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
        [TestCase("Series S02 - S10 [OtherGroup][1080p]", new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
        [TestCase("Series S02-10 [OtherGroup][1080p]", new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
        [TestCase("Series S02 - 10 1080p WEB-DL", new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
        public void should_preserve_pack_ranges(string title, int[] seasons)
        {
            var result = Parser.Parser.ParseTitle(title);

            result.FullSeason.Should().BeTrue();
            result.SeasonNumbers.Should().Equal(seasons);
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
        }

        [Test]
        public void should_preserve_leading_subgroup_season_alias()
        {
            var result = Parser.Parser.ParseTitle("[HorribleSubs] Shokugeki no Soma S3 - 10 [1080p].mkv");

            result.SeriesTitle.Should().Be("Shokugeki no Soma S3");
            result.AbsoluteEpisodeNumbers.Should().Equal(10);
            result.FullSeason.Should().BeFalse();
        }
    }
}
