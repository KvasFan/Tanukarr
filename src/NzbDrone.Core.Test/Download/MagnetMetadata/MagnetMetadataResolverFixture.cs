using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MonoTorrent;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.MagnetMetadata;

namespace NzbDrone.Core.Test.Download.MagnetMetadata
{
    [TestFixture]
    public class MagnetMetadataResolverFixture
    {
        private Mock<IConfigService> _config;
        private MagnetMetadataResolver _subject;

        [SetUp]
        public void SetUp()
        {
            _config = new Mock<IConfigService>();
            _subject = new MagnetMetadataResolver(_config.Object, LogManager.GetCurrentClassLogger());
        }

        [Test]
        public async Task should_not_create_engine_when_proxy_is_enabled()
        {
            _config.SetupGet(v => v.ProxyEnabled).Returns(true);

            var result = await _subject.ResolveAsync(CreateMagnet("https://tracker.example/announce"), CancellationToken.None);

            result.IsResolved.Should().BeFalse();
            result.Reason.Should().Be(MagnetMetadataUninspectableReason.ProxyConfigured);
        }

        [Test]
        public async Task should_not_fall_back_to_dht_for_udp_only_magnet()
        {
            var result = await _subject.ResolveAsync(CreateMagnet("udp://tracker.example:6969/announce"), CancellationToken.None);

            result.IsResolved.Should().BeFalse();
            result.Reason.Should().Be(MagnetMetadataUninspectableReason.NoAllowedTrackers);
        }

        [Test]
        public void should_propagate_caller_cancellation()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.ThrowsAsync<TaskCanceledException>(() =>
                _subject.ResolveAsync(CreateMagnet("https://tracker.example/announce"), cancellation.Token));
        }

        [Test]
        public void resolution_magnet_should_retain_only_http_and_https_trackers()
        {
            var original = CreateMagnet(
                "udp://tracker.example:6969/announce",
                "HTTPS://secure.example/announce",
                "http://plain.example/announce",
                "ws://unsupported.example/announce");

            var resolution = MagnetMetadataResolver.CreateResolutionMagnet(original);

            resolution.AnnounceUrls.Should().Equal(
                "HTTPS://secure.example/announce",
                "http://plain.example/announce");
            resolution.Webseeds.Should().BeEmpty();
            resolution.InfoHashes.Should().Be(original.InfoHashes);
            resolution.Name.Should().Be(original.Name);
        }

        [Test]
        public void should_apply_restrictive_engine_and_torrent_settings()
        {
            var engineSettings = MagnetMetadataResolver.CreateEngineSettings("resolver-cache");
            var torrentSettings = MagnetMetadataResolver.CreateTorrentSettings();

            engineSettings.AllowLocalPeerDiscovery.Should().BeFalse();
            engineSettings.AllowPortForwarding.Should().BeFalse();
            engineSettings.AutoSaveLoadDhtCache.Should().BeFalse();
            engineSettings.AutoSaveLoadFastResume.Should().BeFalse();
            engineSettings.AutoSaveLoadMagnetLinkMetadata.Should().BeFalse();
            engineSettings.DhtEndPoint.Should().BeNull();
            engineSettings.DiskCacheBytes.Should().Be(0);
            engineSettings.ListenEndPoints.Should().BeEmpty();
            engineSettings.MaximumMetadataSize.Should().Be(MagnetMetadataResolver.MaximumMetadataSize);
            torrentSettings.AllowDht.Should().BeFalse();
            torrentSettings.AllowPeerExchange.Should().BeFalse();
            torrentSettings.CreateContainingDirectory.Should().BeFalse();
            MagnetMetadataResolver.ResolutionTimeout.Should().Be(TimeSpan.FromSeconds(30));
            MagnetMetadataResolver.MaximumConcurrentResolutions.Should().Be(2);
        }

        private static MagnetLink CreateMagnet(params string[] trackers)
        {
            var query = "magnet:?xt=urn:btih:755248817d32b00cc853e633ecdc48e4c21bff15&dn=Test.Release";
            foreach (var tracker in trackers)
            {
                query += "&tr=" + System.Uri.EscapeDataString(tracker);
            }

            return MagnetLink.Parse(query);
        }
    }
}
