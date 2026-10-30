using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras
{
    [TestFixture]
    public class ScopedRenameFixture : CoreTest<ExtraService>
    {
        private Series _series;
        private List<EpisodeFile> _files;
        private List<Episode> _episodes;
        private List<SubtitleFile> _subtitles;
        private List<OtherExtraFile> _others;
        private List<string> _moves;
        private List<int> _updatedSubtitleIds;
        private List<int> _updatedOtherIds;

        [SetUp]
        public void Setup()
        {
            _series = new Series { Id = 1, Path = @"C:\Test\Series".AsOsAgnostic() };
            _files = new List<EpisodeFile>
            {
                new EpisodeFile { Id = 10, SeriesId = 1, SeasonNumber = 0, RelativePath = Path.Combine("Specials", "Series - S00E10.mkv") },
                new EpisodeFile { Id = 20, SeriesId = 1, SeasonNumber = 3, RelativePath = Path.Combine("Season 3", "Series - S03E21.avi") }
            };
            _episodes = new List<Episode>
            {
                new Episode { Id = 100, SeriesId = 1, EpisodeFileId = 10, SeasonNumber = 0, EpisodeNumber = 10 },
                new Episode { Id = 200, SeriesId = 1, EpisodeFileId = 20, SeasonNumber = 3, EpisodeNumber = 21 },
                new Episode { Id = 201, SeriesId = 1, EpisodeFileId = 20, SeasonNumber = 3, EpisodeNumber = 22 }
            };
            _subtitles = new List<SubtitleFile>
            {
                new SubtitleFile { Id = 101, SeriesId = 1, EpisodeFileId = 10, SeasonNumber = 0, RelativePath = "old-selected.en.srt", Extension = ".srt", Language = Language.English, Title = "Commentary", LanguageTags = new List<string> { "sdh" } },
                new SubtitleFile { Id = 201, SeriesId = 1, EpisodeFileId = 20, SeasonNumber = 3, RelativePath = Path.Combine("Season 3", "Series - S03E21.matroska.en.srt"), Extension = ".srt", Language = Language.English, Title = "Legacy title" }
            };
            _others = new List<OtherExtraFile>
            {
                new OtherExtraFile { Id = 102, SeriesId = 1, EpisodeFileId = 10, SeasonNumber = 0, RelativePath = "old-selected.nfo", Extension = ".nfo" },
                new OtherExtraFile { Id = 202, SeriesId = 1, EpisodeFileId = 20, SeasonNumber = 3, RelativePath = "unrelated.nfo", Extension = ".nfo" }
            };
            _moves = new List<string>();
            _updatedSubtitleIds = new List<int>();
            _updatedOtherIds = new List<int>();

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesBySeries(1)).Returns(_files);
            Mocker.GetMock<IEpisodeService>().Setup(s => s.GetEpisodeBySeries(1)).Returns(_episodes);
            Mocker.GetMock<ISubtitleFileService>().Setup(s => s.GetFilesBySeries(1)).Returns(_subtitles);
            Mocker.GetMock<ISubtitleFileService>().Setup(s => s.Upsert(It.IsAny<List<SubtitleFile>>()))
                  .Callback<List<SubtitleFile>>(rows => _updatedSubtitleIds.AddRange(rows.Select(r => r.Id)));
            Mocker.GetMock<IOtherExtraFileService>().Setup(s => s.GetFilesBySeries(1)).Returns(_others);
            Mocker.GetMock<IOtherExtraFileService>().Setup(s => s.Upsert(It.IsAny<List<OtherExtraFile>>()))
                  .Callback<List<OtherExtraFile>>(rows => _updatedOtherIds.AddRange(rows.Select(r => r.Id)));
            Mocker.GetMock<IDiskProvider>().Setup(s => s.MoveFile(It.IsAny<string>(), It.IsAny<string>(), false))
                  .Callback<string, string, bool>((source, destination, overwrite) => _moves.Add(destination));

            // Keep the real rename/formatting code; only disk and persistence are isolated.
            Mocker.SetConstant<IEnumerable<IManageExtraFiles>>(new IManageExtraFiles[]
            {
                Mocker.Resolve<SubtitleService>(),
                Mocker.Resolve<OtherExtraService>()
            });
        }

        private void Rename(params int[] ids)
        {
            Subject.Handle(new SeriesRenamedEvent(_series, _files.Where(f => ids.Contains(f.Id))
                .Select(f => new RenamedEpisodeFile { EpisodeFile = f, PreviousRelativePath = "old.mkv", PreviousPath = Path.Combine(_series.Path, "old.mkv") }).ToList()));
        }

        [Test]
        public void selected_rename_should_leave_other_subtitles_and_extra_registrations_untouched()
        {
            var originalSubtitle = _subtitles[1].RelativePath;
            var originalOther = _others[1].RelativePath;

            Rename(10);

            _moves.Should().BeEquivalentTo(new[]
            {
                Path.Combine(_series.Path, "Specials", "Series - S00E10.Commentary.en.sdh.srt"),
                Path.Combine(_series.Path, "Specials", "Series - S00E10.nfo")
            });
            _updatedSubtitleIds.Should().Equal(101);
            _updatedOtherIds.Should().Equal(102);
            _subtitles[1].RelativePath.Should().Be(originalSubtitle);
            _others[1].RelativePath.Should().Be(originalOther);
            _subtitles.Select(s => s.EpisodeFileId).Should().Equal(10, 20);
            _others.Select(s => s.EpisodeFileId).Should().Equal(10, 20);
            _episodes.Select(e => e.EpisodeFileId).Should().Equal(10, 20, 20);
            _files.Select(f => f.RelativePath).Should().Equal(Path.Combine("Specials", "Series - S00E10.mkv"), Path.Combine("Season 3", "Series - S03E21.avi"));
        }

        [TestCase(null, false, "Series - S00E10.en.srt")]
        [TestCase("Commentary", false, "Series - S00E10.Commentary.en.srt")]
        [TestCase("Commentary", true, "Series - S00E10.Commentary.en.sdh.srt")]
        public void selected_subtitle_should_preserve_title_language_and_hearing_impaired_tags(string title, bool sdh, string expected)
        {
            var subtitle = _subtitles[0];
            subtitle.Title = title;
            subtitle.LanguageTags = sdh ? new List<string> { "sdh" } : new List<string>();

            Rename(10);

            subtitle.RelativePath.Should().Be(Path.Combine("Specials", expected));
            subtitle.Id.Should().Be(101);
            subtitle.EpisodeFileId.Should().Be(10);
            subtitle.Language.Should().Be(Language.English);
            subtitle.Title.Should().Be(title);
            subtitle.LanguageTags.Should().Equal(sdh ? new[] { "sdh" } : new string[0]);
        }

        [Test]
        public void full_successful_rename_set_should_move_extras_for_all_renamed_files()
        {
            Rename(10, 20);

            _updatedSubtitleIds.Should().BeEquivalentTo(new[] { 101, 201 });
            _updatedOtherIds.Should().BeEquivalentTo(new[] { 102, 202 });
            _moves.Should().HaveCount(4);
            _subtitles[1].RelativePath.Should().Be(Path.Combine("Season 3", "Series - S03E21.Legacy title.en.srt"));
            _others[1].RelativePath.Should().Be(Path.Combine("Season 3", "Series - S03E21.nfo"));
            _files[1].Episodes.Value.Select(e => e.Id).Should().Equal(200, 201);
            _episodes.Select(e => e.EpisodeFileId).Should().Equal(10, 20, 20);
        }

        [Test]
        public void empty_successful_rename_set_should_not_move_or_update_extras()
        {
            Rename();

            _moves.Should().BeEmpty();
            _updatedSubtitleIds.Should().BeEmpty();
            _updatedOtherIds.Should().BeEmpty();
        }

        [Test]
        public void missing_renamed_file_id_should_not_fall_back_to_the_entire_series()
        {
            Subject.Handle(new SeriesRenamedEvent(_series, new List<RenamedEpisodeFile>
            {
                new RenamedEpisodeFile { EpisodeFile = new EpisodeFile { Id = 999, SeriesId = 1, RelativePath = "missing.mkv" } }
            }));

            _moves.Should().BeEmpty();
            _updatedSubtitleIds.Should().BeEmpty();
            _updatedOtherIds.Should().BeEmpty();
        }
    }
}
