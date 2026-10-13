using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.AniList;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.AniList
{
    [TestFixture]
    public class AniListMetadataCacheFixture : CoreTest<AniListMetadataCache>
    {
        private DateTime _now;

        [SetUp]
        public void Setup()
        {
            _now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
            Mocker.GetMock<IAniListRequestDelay>().SetupGet(delay => delay.UtcNow).Returns(() => _now);
        }

        private AniListMetadataCache AnotherInstance()
        {
            return new AniListMetadataCache(Mocker.Resolve<ICacheManager>(), Mocker.Resolve<IAniListRequestDelay>());
        }

        private static AniListMediaResource Media(int id)
        {
            return new AniListMediaResource
            {
                Id = id,
                Title = new AniListTitleResource { English = "Public metadata" },
                Genres = new List<string> { "Drama" }
            };
        }

        [Test]
        public void should_share_public_metadata_across_instances_without_sharing_mutable_objects()
        {
            var calls = 0;
            var first = Subject.Get(
            "media:1",
            () =>
            {
                calls++;
                return Media(1);
            },
            TimeSpan.FromMinutes(5));
            first.Title.English = "Changed by caller";
            first.Genres.Clear();

            var second = AnotherInstance().Get(
            "media:1",
            () =>
            {
                calls++;
                return Media(1);
            },
            TimeSpan.FromMinutes(5));

            calls.Should().Be(1);
            second.Should().NotBeSameAs(first);
            second.Title.English.Should().Be("Public metadata");
            second.Genres.Should().Equal("Drama");
        }

        [Test]
        public void should_refresh_expired_metadata_and_not_extend_expiry_on_reads()
        {
            var calls = 0;
            Func<AniListMediaResource> fetch = () =>
            {
                calls++;
                return Media(calls);
            };
            Subject.Get("media:1", fetch, TimeSpan.FromMinutes(5)).Id.Should().Be(1);
            _now += TimeSpan.FromMinutes(4);
            AnotherInstance().Get("media:1", fetch, TimeSpan.FromMinutes(5)).Id.Should().Be(1);
            _now += TimeSpan.FromMinutes(1);
            AnotherInstance().Get("media:1", fetch, TimeSpan.FromMinutes(5)).Id.Should().Be(2);
            calls.Should().Be(2);
        }

        [Test]
        public void should_coalesce_concurrent_public_metadata_fetches_across_instances()
        {
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var secondStarted = new ManualResetEventSlim();
            var firstCache = Subject;
            var secondCache = AnotherInstance();
            var calls = 0;
            Func<AniListMediaResource> fetch = () =>
            {
                Interlocked.Increment(ref calls);
                started.Set();
                release.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                return Media(1);
            };

            var first = Task.Run(() => firstCache.Get("media:1", fetch, TimeSpan.FromMinutes(5)));
            started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            var second = Task.Run(() =>
            {
                secondStarted.Set();
                return secondCache.Get("media:1", fetch, TimeSpan.FromMinutes(5));
            });
            secondStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            release.Set();
            Task.WaitAll(first, second);

            calls.Should().Be(1);
            first.Result.Should().NotBeSameAs(second.Result);
            first.Result.Id.Should().Be(1);
            second.Result.Id.Should().Be(1);
        }

        [Test]
        public void should_not_cache_a_failed_fetch_or_hide_its_exception()
        {
            var failure = new InvalidOperationException("Incomplete provider response");
            Action fetch = () => Subject.Get<AniListMediaResource>("media:1", () => throw failure, TimeSpan.FromMinutes(5));

            fetch.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
            AnotherInstance().Get("media:1", () => Media(1), TimeSpan.FromMinutes(5)).Id.Should().Be(1);
        }

        [Test]
        public void should_not_cache_a_missing_or_unavailable_public_resource()
        {
            Subject.Get<AniListMediaResource>("media:1", () => null, TimeSpan.FromMinutes(5)).Should().BeNull();
            AnotherInstance().Get("media:1", () => Media(1), TimeSpan.FromMinutes(5)).Id.Should().Be(1);
        }

        [Test]
        public void should_not_retain_a_resource_marked_incomplete_by_its_provider()
        {
            var calls = 0;
            Func<AniListMediaResource> fetch = () =>
            {
                calls++;
                return Media(1);
            };

            Subject.Get("media:1", fetch, TimeSpan.FromMinutes(5), _ => false).Id.Should().Be(1);
            AnotherInstance().Get("media:1", fetch, TimeSpan.FromMinutes(5), _ => false).Id.Should().Be(1);
            calls.Should().Be(2);
        }

        [Test]
        public void should_evict_the_oldest_entry_at_the_bounded_cache_capacity()
        {
            for (var id = 0; id <= 512; id++)
            {
                Subject.Set("media:" + id, Media(id), TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(id)));
            }

            var calls = 0;
            Subject.Get(
            "media:512",
            () =>
            {
                calls++;
                return Media(512);
            },
            TimeSpan.FromMinutes(5)).Id.Should().Be(512);
            Subject.Get(
            "media:0",
            () =>
            {
                calls++;
                return Media(0);
            },
            TimeSpan.FromMinutes(5)).Id.Should().Be(0);
            calls.Should().Be(1);
        }

        [Test]
        public void should_not_retain_oversized_metadata_entries()
        {
            var calls = 0;
            Func<AniListMediaResource> fetch = () =>
            {
                calls++;
                return new AniListMediaResource { Id = 1, Description = new string('a', 513 * 1024) };
            };
            Subject.Get("media:1", fetch, TimeSpan.FromMinutes(5)).Id.Should().Be(1);
            AnotherInstance().Get("media:1", fetch, TimeSpan.FromMinutes(5)).Id.Should().Be(1);
            calls.Should().Be(2);
        }

        [Test]
        public void should_clone_seeded_metadata_before_the_original_can_be_modified()
        {
            var original = Media(1);
            Subject.Set("media:1", original, TimeSpan.FromMinutes(5));
            original.Title.English = "Changed after seeding";

            AnotherInstance().Get<AniListMediaResource>("media:1", () => null, TimeSpan.FromMinutes(5))
                .Title.English.Should().Be("Public metadata");
        }
    }
}
