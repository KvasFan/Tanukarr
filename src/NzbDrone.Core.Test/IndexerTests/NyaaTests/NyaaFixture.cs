using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Nyaa;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.IndexerTests.NyaaTests
{
    [TestFixture]
    public class NyaaFixture : CoreTest<Nyaa>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Definition = new IndexerDefinition()
            {
                Name = "Nyaa",
                Settings = new NyaaSettings()
            };
        }

/*        [Test]
        // Legacy Nyaa feed test

        public void should_parse_recent_feed_from_Nyaa()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/Nyaa/Nyaa.xml");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.Execute(It.Is<HttpRequest>(v => v.Method == HttpMethod.Get)))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), recentFeed));

            var releases = Subject.FetchRecent();

            releases.Should().HaveCount(4);
            releases.First().Should().BeOfType<TorrentInfo>();

            var torrentInfo = releases.First() as TorrentInfo;

            torrentInfo.Title.Should().Be("[TSRaws] Futsuu no Joshikousei ga [Locodol] Yattemita. #07 (TBS).ts");
            torrentInfo.DownloadProtocol.Should().Be(DownloadProtocol.Torrent);
            torrentInfo.DownloadUrl.Should().Be("https://www.nyaa.se/?page=download&tid=587750");
            torrentInfo.InfoUrl.Should().Be("https://www.nyaa.se/?page=view&tid=587750");
            torrentInfo.CommentUrl.Should().BeNullOrEmpty();
            torrentInfo.Indexer.Should().Be(Subject.Definition.Name);
            torrentInfo.PublishDate.Should().Be(DateTime.Parse("2014/08/14 18:10:36"));
            torrentInfo.Size.Should().Be(2523293286); //2.35 GiB
            torrentInfo.InfoHash.Should().Be(null);
            torrentInfo.MagnetUrl.Should().Be(null);
            torrentInfo.Peers.Should().Be(2 + 1);
            torrentInfo.Seeders.Should().Be(1);
        }*/

        [Test]
        public async Task should_avoid_supplemental_queries_when_the_bare_feed_already_contains_all_packs()
        {
            ((NyaaSettings)Subject.Definition.Settings).BaseUrl = "https://fixture.invalid";
            var titles = new[] { "Fixture TV S01 1080p", "Fixture TV S02 1080p", "Fixture TV S01-S02 1080p" };
            var items = titles.Select((title, i) => $"<item><title>{title}</title><guid>fixture-{i}</guid><link>https://fixture.invalid/{i}.torrent</link><pubDate>Mon, 12 Oct 2026 12:00:00 +0000</pubDate><size>1 GiB</size><infoHash>0123456789abcdef0123456789abcdef01234567</infoHash><seeders>5</seeders><leechers>0</leechers></item>");
            var xml = "<rss version=\"2.0\"><channel><title>Fixture</title>" + string.Concat(items) + "</channel></rss>";
            Mocker.GetMock<IHttpClient>().Setup(c => c.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest r, CancellationToken _) => new HttpResponse(r, new HttpHeader(), xml));
            var criteria = new CompleteSeriesSearchCriteria
            {
                Series = new Series { Title = "Fixture TV" },
                SceneTitles = new List<string> { "Fixture TV" },
                IncludeSeasonPacks = true,
                Episodes = new List<Episode>
                {
                    new Episode { SeasonNumber = 1, EpisodeNumber = 1 },
                    new Episode { SeasonNumber = 2, EpisodeNumber = 1 }
                }
            };

            var reports = await Subject.Fetch(criteria);

            reports.Should().HaveCount(3);
            Mocker.GetMock<IHttpClient>().Verify(c => c.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Once());
        }

        [Test]
        public async Task should_parse_2021_recent_feed_from_Nyaa()
        {
            var recentFeed = ReadAllText(@"Files/Indexers/Nyaa/Nyaa2021.xml");

            Mocker.GetMock<IHttpClient>()
                .Setup(o => o.ExecuteAsync(It.Is<HttpRequest>(v => v.Method == HttpMethod.Get)))
                .ReturnsAsync((HttpRequest r, CancellationToken _) => new HttpResponse(r, new HttpHeader(), recentFeed));

            var releases = await Subject.FetchRecent();

            releases.Should().HaveCount(3);
            releases.First().Should().BeOfType<TorrentInfo>();

            var torrentInfo = releases.First() as TorrentInfo;

            torrentInfo.Title.Should().Be("[Foxy-Subs] Mahouka Koukou no Yuutousei - 08 [720p] [3194D881].mkv");
            torrentInfo.DownloadProtocol.Should().Be(DownloadProtocol.Torrent);
            torrentInfo.DownloadUrl.Should().Be("https://nyaa.si/download/1424896.torrent");
            torrentInfo.InfoUrl.Should().Be("https://nyaa.si/view/1424896");
            torrentInfo.CommentUrl.Should().BeNullOrEmpty();
            torrentInfo.Indexer.Should().Be(Subject.Definition.Name);
            torrentInfo.PublishDate.Should().Be(DateTime.Parse("Tue, 24 Aug 2021 22:18:46"));
            torrentInfo.Size.Should().Be(639211930); // 609.6 MiB
            torrentInfo.MagnetUrl.Should().Be(null);
            torrentInfo.Seeders.Should().Be(4);
            torrentInfo.Peers.Should().Be(3 + 4);
        }
    }
}
