using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation.Aggregators;
using NzbDrone.Core.MediaFiles.EpisodeImport.Specifications;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class EpisodeNumberingResolverFixture : TestBase<EpisodeNumberingResolver>
    {
        private Series _series;
        private List<Episode> _episodes;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 42, Title = "Example Show", UseSceneNumbering = true, SeasonType = SeasonType.Official };
            _episodes = new List<Episode>
            {
                Episode(51, 5, 1, 4, 1, "1600 Candles"),
                Episode(52, 5, 2, 4, 2, "The One That Got Away"),
                Episode(53, 5, 3, 4, 3, "One Little Word"),
                Episode(61, 6, 1, 5, 1, "In Country... Club"),
                Episode(62, 6, 2, 5, 2, "Moon Over Isla Island"),
                Episode(63, 6, 3, 5, 3, "Home Adrone"),
                Episode(71, 7, 1, 6, 1, "100 A.D.")
            };
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(42)).Returns(() => _episodes);
        }

        private static Episode Episode(int id, int season, int number, int sceneSeason, int sceneNumber, string title)
        {
            return new Episode
            {
                Id = id,
                SeriesId = 42,
                SeasonNumber = season,
                EpisodeNumber = number,
                SceneSeasonNumber = sceneSeason,
                SceneEpisodeNumber = sceneNumber,
                Title = title
            };
        }

        private EpisodeNumberingResolution Resolve(string title, string origin = null, NumberingConvention? inferred = null)
        {
            return Subject.Resolve(_series, Parser.Parser.ParseTitle(title), origin == null ? null : new SceneMapping { SceneOrigin = origin }, inferred);
        }

        [TestCase("Example.Show.S05E01.1600.Candles.1080p.WEB.x265-Goki", 51)]
        [TestCase("Example Show - S05E01 - 1600 Candles.mkv", 51)]
        [TestCase("Example.Show.5x01.1600.Candles.1080p.mkv", 51)]
        [TestCase("Example.Show.S05E01.In.Country.Club.720p.HDTV-Group", 61)]
        [TestCase("Example.Show.S05E02.The.One.That.Got.Away.1080p.mkv", 52)]
        public void should_resolve_distinctive_title_in_either_numbering(string title, int id)
        {
            var result = Resolve(title);
            result.Rejection.Should().BeNull();
            result.FromTitle.Should().BeTrue();
            result.Episodes.Select(e => e.Id).Should().Equal(id);
        }

        [Test]
        public void title_should_override_a_wrong_group_assumption()
        {
            Resolve("Example.Show.S05E01.1600.Candles.1080p.mkv", "scene").Episodes[0].Id.Should().Be(51);
        }

        [TestCase(null)]
        [TestCase("unknown")]
        [TestCase("unknown:tvdb")]
        [TestCase("mixed")]
        public void should_block_ambiguous_packs(string origin)
        {
            Resolve("Example.Show.S05.1080p.x265-Unknown", origin).Rejection.Should().Contain("Ambiguous");
        }

        [TestCase("tvdb", 5)]
        [TestCase("scene", 6)]
        public void should_preserve_explicit_release_conventions(string origin, int season)
        {
            var result = Resolve("Example.Show.S05.1080p.x265-Group", origin);
            result.Rejection.Should().BeNull();
            result.Episodes.Should().HaveCount(3).And.OnlyContain(e => e.SeasonNumber == season);
        }

        [Test]
        public void should_not_guess_from_only_one_existing_numeric_candidate()
        {
            Resolve("Example.Show.S07.1080p.x265-Unknown").Rejection.Should().NotBeNull();
        }

        [Test]
        public void should_not_accept_an_incomplete_multi_episode_mapping()
        {
            Resolve("Example.Show.S05E01E04.1080p.mkv", "tvdb").Rejection.Should().NotBeNull();
        }

        [Test]
        public void should_resolve_all_seasons_in_a_trusted_multi_season_pack()
        {
            var result = Resolve("Example.Show.S04-S05.1080p.x265-Group", "scene");
            result.Episodes.Select(e => e.SeasonNumber).Distinct().Should().Equal(5, 6);
            result.Episodes.Should().HaveCount(6);
        }

        [Test]
        public void should_block_unknown_multi_season_numbering()
        {
            Resolve("Example.Show.S05-S06.1080p.x265-Unknown").Rejection.Should().NotBeNull();
        }

        [Test]
        public void should_resolve_each_trusted_season_alias_independently()
        {
            var parsed = Parser.Parser.ParseTitle("Example.Show.S01-S02.1080p.mkv");
            var first = new SceneMapping { SceneOrigin = "tvdb", SeasonNumber = 5, SceneSeasonNumber = 1 };
            Mocker.GetMock<ISceneMappingService>()
                .Setup(m => m.FindSceneMapping(parsed.SeriesTitle, parsed.ReleaseTitle, 2))
                .Returns(new SceneMapping { SceneOrigin = "tvdb", SeasonNumber = 6, SceneSeasonNumber = 2 });

            var resolution = Subject.Resolve(_series, parsed, first);

            resolution.Rejection.Should().BeNull();
            resolution.Episodes.Select(e => e.SeasonNumber).Distinct().Should().Equal(5, 6);
        }

        [Test]
        public void should_preserve_alternate_library_ordering()
        {
            _series.SeasonType = "tmdb:absolute";

            Resolve("Example.Show.S05E01.1600.Candles.1080p.mkv", "scene").Should().BeNull();

            Mocker.GetMock<IEpisodeService>().Verify(s => s.GetEpisodeBySeries(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_leave_identical_candidate_sets_unchanged()
        {
            _episodes.ForEach(e =>
            {
                e.SceneSeasonNumber = e.SeasonNumber;
                e.SceneEpisodeNumber = e.EpisodeNumber;
            });
            Resolve("Example.Show.S05.1080p.x265-Unknown").Should().BeNull();
        }

        [Test]
        public void should_leave_unaffected_season_of_a_mapped_show_unchanged()
        {
            _episodes.Add(Episode(11, 1, 1, 1, 1, "Opening Story"));
            Resolve("Example.Show.S01E01.1080p.mkv").Should().BeNull();
        }

        [Test]
        public void should_handle_episode_only_mapping_differences()
        {
            _episodes = new List<Episode> { Episode(11, 1, 1, 1, 2, "First Story"), Episode(12, 1, 2, 1, 1, "Second Story") };
            Resolve("Example.Show.S01E01.Second.Story.1080p.mkv").Episodes[0].Id.Should().Be(12);
        }

        [TestCase("Example.Show.S00E01.1080p.mkv")]
        [TestCase("Example.Show.2026.09.30.1080p.mkv")]
        [TestCase("[Group] Example Show - 125 [1080p]")]
        public void should_preserve_special_daily_and_absolute_paths(string title)
        {
            Resolve(title).Should().BeNull();
        }

        [Test]
        public void should_not_query_metadata_when_scene_numbering_is_off()
        {
            _series.UseSceneNumbering = false;
            Resolve("Example.Show.S05.1080p.mkv").Should().BeNull();
            Mocker.GetMock<IEpisodeService>().Verify(s => s.GetEpisodeBySeries(It.IsAny<int>()), Times.Never());
        }

        [TestCase("Pilot")]
        [TestCase("Part 1")]
        [TestCase("Episode 1")]
        [TestCase("400")]
        public void should_not_use_generic_or_short_titles_as_evidence(string title)
        {
            _episodes[0].Title = title;
            Resolve("Example.Show.S05E01." + title + ".1080p.mkv").Rejection.Should().NotBeNull();
        }

        [Test]
        public void should_not_match_title_from_series_name()
        {
            Resolve("1600.Candles.S05E01.1080p.mkv").Rejection.Should().NotBeNull();
        }

        [Test]
        public void should_not_match_a_title_prefix_of_a_different_story()
        {
            Resolve("Example.Show.S05E01.1600.Candles.Return.Again.1080p.mkv").Rejection.Should().NotBeNull();
        }

        [Test]
        public void should_block_duplicate_titles()
        {
            _episodes[3].Title = _episodes[0].Title;
            Resolve("Example.Show.S05E01.1600.Candles.1080p.mkv", "scene").Rejection.Should().Contain("not unique");
        }

        [Test]
        public void should_block_title_pointing_to_neither_numeric_interpretation()
        {
            Resolve("Example.Show.S05E01.Home.Adrone.1080p.mkv", "scene").Rejection.Should().Contain("both");
        }

        [Test]
        public void two_distinct_titles_should_establish_a_pack_convention()
        {
            var batch = Subject.InferBatch(_series, new List<string>
            {
                "/downloads/Example.Show.S05E01.1600.Candles.1080p.mkv",
                "/downloads/Example.Show.S05E02.The.One.That.Got.Away.1080p.mkv"
            });
            batch[5].Should().Be(NumberingConvention.Aired);
            Resolve("Example.Show.S05E03.1080p.mkv", inferred: batch[5]).Episodes[0].Id.Should().Be(53);
        }

        [Test]
        public void duplicate_copies_of_one_episode_should_not_establish_a_pack_convention()
        {
            var batch = Subject.InferBatch(_series, new List<string>
            {
                "/downloads/a/Example.Show.S05E01.1600.Candles.1080p.mkv",
                "/downloads/b/Example.Show.S05E01.1600.Candles.720p.mkv"
            });
            batch.Should().BeEmpty();
        }

        [Test]
        public void conflicting_titles_should_block_the_season_including_titleless_files()
        {
            var batch = Subject.InferBatch(_series, new List<string>
            {
                "/downloads/Example.Show.S05E01.1600.Candles.1080p.mkv",
                "/downloads/Example.Show.S05E02.Moon.Over.Isla.Island.1080p.mkv"
            });
            batch[5].Should().Be(NumberingConvention.Ambiguous);
            Resolve("Example.Show.S05E03.1080p.mkv", "tvdb", batch[5]).Rejection.Should().Contain("Conflicting");
        }

        [Test]
        public void file_title_should_not_silently_conflict_with_inferred_convention()
        {
            Resolve("Example.Show.S05E01.1600.Candles.1080p.mkv", inferred: NumberingConvention.Scene).Rejection.Should().Contain("conflicts");
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public void library_scans_should_be_unchanged(bool existing, bool scene)
        {
            Subject.ResolveLocal(new LocalEpisode { ExistingFile = existing, SceneSource = scene, Series = _series },
                Parser.Parser.ParseTitle("Example.Show.S05E01.1080p.mkv")).Should().BeNull();
        }

        [Test]
        public void should_inherit_explicit_pack_group_for_untagged_files()
        {
            var pack = Parser.Parser.ParseTitle("Example.Show.S05.1080p.x265-Known");
            Mocker.GetMock<ISceneMappingService>().Setup(m => m.FindSceneMapping(pack.SeriesTitle, pack.ReleaseTitle, pack.SeasonNumber.Value))
                .Returns(new SceneMapping { SceneOrigin = "tvdb" });
            var local = new LocalEpisode { SceneSource = true, Series = _series, DownloadClientEpisodeInfo = pack };
            Subject.ResolveLocal(local, Parser.Parser.ParseTitle("Example.Show.S05E01.1080p.mkv")).Episodes[0].Id.Should().Be(51);
        }

        [Test]
        public void should_not_inherit_an_unrelated_pack_season()
        {
            var pack = Parser.Parser.ParseTitle("Example.Show.S06.1080p.x265-Known");
            Mocker.GetMock<ISceneMappingService>().Setup(m => m.FindSceneMapping(pack.SeriesTitle, pack.ReleaseTitle, pack.SeasonNumber.Value))
                .Returns(new SceneMapping { SceneOrigin = "tvdb" });
            var local = new LocalEpisode { SceneSource = true, Series = _series, DownloadClientEpisodeInfo = pack };
            Subject.ResolveLocal(local, Parser.Parser.ParseTitle("Example.Show.S05E01.1080p.mkv")).Rejection.Should().NotBeNull();
        }

        [Test]
        public void should_inherit_the_mapping_for_the_files_raw_season_in_a_multi_season_pack()
        {
            var pack = Parser.Parser.ParseTitle("Example.Show.S04-S05.1080p.x265-Known");
            Mocker.GetMock<ISceneMappingService>()
                .Setup(m => m.FindSceneMapping(pack.SeriesTitle, pack.ReleaseTitle, 4))
                .Returns(new SceneMapping { SceneOrigin = "tvdb", SeasonNumber = 5, SceneSeasonNumber = 4 });
            Mocker.GetMock<ISceneMappingService>()
                .Setup(m => m.FindSceneMapping(pack.SeriesTitle, pack.ReleaseTitle, 5))
                .Returns(new SceneMapping { SceneOrigin = "scene", SeasonNumber = 5, SceneSeasonNumber = 5 });
            var local = new LocalEpisode { SceneSource = true, Series = _series, DownloadClientEpisodeInfo = pack };

            var resolution = Subject.ResolveLocal(local, Parser.Parser.ParseTitle("Example.Show.S05E01.1080p.mkv"));

            resolution.Rejection.Should().BeNull();
            resolution.Episodes.Should().ContainSingle().Which.Id.Should().Be(61);
            resolution.Convention.Should().Be(NumberingConvention.Scene);
        }

        [Test]
        public void search_should_reject_numbering_ambiguity_without_a_scene_mapping()
        {
            var result = Mocker.Resolve<EpisodeNumberingSpecification>().IsSatisfiedBy(
                new RemoteEpisode { NumberingRejection = "Ambiguous scene/library numbering" }, new ReleaseDecisionInformation());
            result.Accepted.Should().BeFalse();
            Mocker.Resolve<EpisodeNumberingSpecification>().Type.Should().Be(RejectionType.Permanent);
        }

        [Test]
        public void verified_local_mapping_should_be_used_by_aggregation_and_folder_checks()
        {
            Mocker.SetConstant<IEpisodeNumberingResolver>(Subject);
            var parsed = Parser.Parser.ParseTitle("Example.Show.S05E01.1600.Candles.1080p.mkv");
            var pack = Parser.Parser.ParseTitle("Example.Show.S05.1080p.mkv");
            Mocker.GetMock<IParsingService>().Setup(p => p.GetEpisodes(It.IsAny<ParsedEpisodeInfo>(), _series, true, null))
                .Returns(new List<Episode> { _episodes[3] });
            var local = new LocalEpisode
            {
                Series = _series,
                FileEpisodeInfo = parsed,
                FolderEpisodeInfo = pack,
                SceneSource = true,
                OtherVideoFiles = true,
                Path = "/downloads/" + parsed.ReleaseTitle
            };
            Mocker.Resolve<AggregateEpisodes>().Aggregate(local, null);
            local.Episodes[0].Id.Should().Be(51);
            local.VerifiedNumbering.Should().Be(NumberingConvention.Aired);
            local.NumberingRejection.Should().BeNull();
            Mocker.Resolve<MatchesFolderSpecification>().IsSatisfiedBy(local, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void grabbed_episode_ids_should_still_prevent_importing_a_different_episode()
        {
            // Existing MatchesGrabSpecification remains the final identity boundary.
            var local = new LocalEpisode
            {
                Episodes = new List<Episode> { _episodes[0] },
                Release = new GrabbedReleaseInfo(new List<EpisodeHistory>
                {
                    new EpisodeHistory { EpisodeId = 61, SourceTitle = "Example.Show.S05", Data = new Dictionary<string, string>() }
                })
            };
            Mocker.Resolve<MatchesGrabSpecification>().IsSatisfiedBy(local, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void known_folder_convention_should_not_be_overridden_by_a_conflicting_file()
        {
            Mocker.SetConstant<IEpisodeNumberingResolver>(Subject);
            var file = Parser.Parser.ParseTitle("Example.Show.S05E01.1600.Candles.1080p.mkv");
            var folder = Parser.Parser.ParseTitle("Example.Show.S05.1080p-KnownScene");
            Mocker.GetMock<ISceneMappingService>().Setup(m => m.FindSceneMapping(folder.SeriesTitle, folder.ReleaseTitle, folder.SeasonNumber.Value))
                .Returns(new SceneMapping { SceneOrigin = "scene" });
            Mocker.GetMock<IParsingService>().Setup(p => p.GetEpisodes(It.IsAny<ParsedEpisodeInfo>(), _series, true, null))
                .Returns(new List<Episode> { _episodes[3] });
            var local = new LocalEpisode
            {
                Series = _series,
                FileEpisodeInfo = file,
                FolderEpisodeInfo = folder,
                Episodes = new List<Episode> { _episodes[0] },
                VerifiedNumbering = NumberingConvention.Aired
            };
            Mocker.Resolve<MatchesFolderSpecification>().IsSatisfiedBy(local, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_pack_with_a_missing_mapped_season()
        {
            Resolve("Example.Show.S04-S08.1080p.x265-Group", "scene").Rejection.Should().NotBeNull();
        }

        [Test]
        public void batch_inference_should_not_leak_to_a_different_season()
        {
            var local = new LocalEpisode
            {
                Series = _series,
                SceneSource = true,
                BatchNumbering = new Dictionary<int, NumberingConvention> { [5] = NumberingConvention.Aired }
            };
            Subject.ResolveLocal(local, Parser.Parser.ParseTitle("Example.Show.S06E01.1080p.mkv")).Rejection.Should().NotBeNull();
        }

        [Test]
        public void should_preserve_explicit_season_alias_offsets()
        {
            var parsed = Parser.Parser.ParseTitle("Example.Show.S01E01.1080p.mkv");
            var result = Subject.Resolve(_series, parsed, new SceneMapping
            {
                SceneOrigin = "tvdb",
                SeasonNumber = 5,
                SceneSeasonNumber = 1
            });
            result.Episodes[0].Id.Should().Be(51);
        }

        [Test]
        public void should_preserve_xem_alias_for_first_season()
        {
            var parsed = Parser.Parser.ParseTitle("Example.Show.S01E01.1080p.mkv");
            var result = Subject.Resolve(_series, parsed, new SceneMapping
            {
                Type = "XemService",
                SceneOrigin = "scene",
                SceneSeasonNumber = 5
            });
            result.Episodes[0].Id.Should().Be(61);
        }

        [Test]
        public void equal_pack_coverage_should_not_hide_differing_file_numbering()
        {
            _episodes = new List<Episode> { Episode(11, 1, 1, 1, 2, "First Story"), Episode(12, 1, 2, 1, 1, "Second Story") };
            Resolve("Example.Show.S01.1080p-Unknown").Rejection.Should().NotBeNull();
            Resolve("Example.Show.S01.1080p-Known", "tvdb").Episodes.Should().HaveCount(2);
        }
    }
}
