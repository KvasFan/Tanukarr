using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Test.OrganizerTests
{
    [TestFixture]
    public class AnimeGroupNamingFormatFixture
    {
        [TestCase("{Group Title}", AnimeGroupFolderFormatKind.Group)]
        [TestCase("Anime - {Group.Title:60}", AnimeGroupFolderFormatKind.Group)]
        [TestCase("Part {Group Season:000}", AnimeGroupFolderFormatKind.Season)]
        [TestCase("{Series Title} ({Series Year})", AnimeGroupFolderFormatKind.MovieTitle)]
        [TestCase("{Series Title} - {Group Movie:00}", AnimeGroupFolderFormatKind.MovieTitle)]
        [TestCase("Feature {Group Movie:000000}", AnimeGroupFolderFormatKind.MovieNumber)]
        public void should_accept_supported_single_component_expressions(string format, AnimeGroupFolderFormatKind kind)
        {
            AnimeGroupNamingFormat.IsValid(format, kind).Should().BeTrue();
        }

        [TestCase(null, AnimeGroupFolderFormatKind.Group)]
        [TestCase("", AnimeGroupFolderFormatKind.Group)]
        [TestCase("..", AnimeGroupFolderFormatKind.Group)]
        [TestCase("../{Group Title}", AnimeGroupFolderFormatKind.Group)]
        [TestCase("C:\\{Group Title}", AnimeGroupFolderFormatKind.Group)]
        [TestCase("/root/{Group Title}", AnimeGroupFolderFormatKind.Group)]
        [TestCase("{Group Title}\n", AnimeGroupFolderFormatKind.Group)]
        [TestCase("{Group Title}\\nested", AnimeGroupFolderFormatKind.Group)]
        [TestCase("{Group Title}{Unknown}", AnimeGroupFolderFormatKind.Group)]
        [TestCase("{Group Title:0}", AnimeGroupFolderFormatKind.Group)]
        [TestCase("{Group Title:181}", AnimeGroupFolderFormatKind.Group)]
        [TestCase("Season {season:00}", AnimeGroupFolderFormatKind.Season)]
        [TestCase("Season {Group Season:abc}", AnimeGroupFolderFormatKind.Season)]
        [TestCase("Season {Group Season:0000000}", AnimeGroupFolderFormatKind.Season)]
        [TestCase("{Group Movie}", AnimeGroupFolderFormatKind.Season)]
        [TestCase("Film {Group Season}", AnimeGroupFolderFormatKind.MovieNumber)]
        [TestCase("Film", AnimeGroupFolderFormatKind.MovieNumber)]
        [TestCase("{Group Title}", AnimeGroupFolderFormatKind.MovieTitle)]
        [TestCase("{Series Title}{Group Season}", AnimeGroupFolderFormatKind.MovieTitle)]
        public void should_reject_unsafe_unknown_or_wrong_member_number_expressions(string format, AnimeGroupFolderFormatKind kind)
        {
            AnimeGroupNamingFormat.IsValid(format, kind).Should().BeFalse();
        }

        [Test]
        public void should_bound_expression_length_before_rendering()
        {
            AnimeGroupNamingFormat.IsValid(new string('a', 256) + "{Group Title}", AnimeGroupFolderFormatKind.Group).Should().BeFalse();
        }

        [TestCase("{Group Title} - S{season:00}E{episode:00}", true)]
        [TestCase("{Group Movie:00} - S{season:00}E{episode:00}", true)]
        [TestCase("{ - Group Movie:00} - S{season:00}E{episode:00}", true)]
        [TestCase("{[Group Movie:00]} - S{season:00}E{episode:00}", true)]
        [TestCase("{Group Title:20} - S{season:00}E{episode:00}", true)]
        [TestCase("{{Group Movie:abc}} - S{season:00}E{episode:00}", true)]
        [TestCase("{Group Movie:abc} - S{season:00}E{episode:00}", false)]
        [TestCase("{GroupMovie:abc} - S{season:00}E{episode:00}", false)]
        [TestCase("{ - Group Movie:abc} - S{season:00}E{episode:00}", false)]
        [TestCase("{Group Title:0} - S{season:00}E{episode:00}", false)]
        [TestCase("{Group Something} - S{season:00}E{episode:00}", false)]
        public void should_validate_group_filename_tokens_with_the_actual_token_grammar(string format, bool expected)
        {
            AnimeGroupNamingFormat.HasValidGroupTokens(format).Should().Be(expected);
        }
    }
}
