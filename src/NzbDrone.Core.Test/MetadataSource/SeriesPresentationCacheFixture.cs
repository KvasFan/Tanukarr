using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class SeriesPresentationCacheFixture : CoreTest<SeriesPresentationCache>
    {
        private SeriesPresentationCache AnotherInstance()
        {
            return new SeriesPresentationCache(Mocker.Resolve<ICacheManager>());
        }

        [Test]
        public void should_coalesce_public_lookups_across_instances_and_return_independent_results()
        {
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var firstCache = Subject;
            var secondCache = AnotherInstance();
            var calls = 0;
            Func<SeriesPresentation> fetch = () =>
            {
                Interlocked.Increment(ref calls);
                started.Set();
                release.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                return new SeriesPresentation { Overview = "Public overview" };
            };
            var first = Task.Run(() => firstCache.Get("jikan:1", fetch));
            started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            var second = Task.Run(() => secondCache.Get("jikan:1", fetch));
            release.Set();
            Task.WaitAll(first, second);
            calls.Should().Be(1);
            first.Result.Should().NotBeSameAs(second.Result);
            first.Result.Overview = "Caller mutation";
            second.Result.Overview.Should().Be("Public overview");
        }

        [Test]
        public void should_cache_missing_metadata_to_avoid_repeating_unsuccessful_requests()
        {
            var calls = 0;
            Func<SeriesPresentation> fetch = () =>
            {
                calls++;
                return null;
            };
            Subject.Get("anilist:1", fetch).Should().BeNull();
            AnotherInstance().Get("anilist:1", fetch).Should().BeNull();
            calls.Should().Be(1);
        }

        [Test]
        public void should_return_complete_oversize_metadata_without_retaining_it()
        {
            var calls = 0;
            var overview = new string('x', SeriesPresentationCache.MaxEntryBytes + 1);
            Func<SeriesPresentation> fetch = () =>
            {
                calls++;
                return new SeriesPresentation { Overview = overview };
            };
            Subject.Get("jikan:1", fetch).Overview.Should().Be(overview);
            AnotherInstance().Get("jikan:1", fetch).Overview.Should().Be(overview);
            calls.Should().Be(2);
        }

        [Test]
        public void should_evict_oldest_entries_before_exceeding_the_count_limit()
        {
            foreach (var id in Enumerable.Range(0, SeriesPresentationCache.MaxEntries + 1))
            {
                Subject.Get("jikan:" + id, () => new SeriesPresentation { Overview = "Entry " + id });
            }

            var calls = 0;
            Subject.Get("jikan:0", () =>
            {
                calls++;
                return new SeriesPresentation { Overview = "Reloaded" };
            }).Overview.Should().Be("Reloaded");
            calls.Should().Be(1);
        }

        [Test]
        public void should_enforce_total_serialized_byte_limit_independently_of_entry_count()
        {
            var overview = new string('x', SeriesPresentationCache.MaxEntryBytes - 100);
            var entries = (SeriesPresentationCache.MaxTotalBytes / SeriesPresentationCache.MaxEntryBytes) + 2;
            foreach (var id in Enumerable.Range(0, entries))
            {
                Subject.Get("tmdb:" + id, () => new SeriesPresentation { Overview = overview });
            }

            var calls = 0;
            Subject.Get("tmdb:0", () =>
            {
                calls++;
                return new SeriesPresentation { Overview = "Reloaded" };
            }).Overview.Should().Be("Reloaded");
            calls.Should().Be(1);
        }

        [Test]
        public void should_keep_provider_cooldowns_shared_and_separate()
        {
            Subject.BackOff(MetadataSourceType.AniList);
            AnotherInstance().IsProviderCoolingDown(MetadataSourceType.AniList).Should().BeTrue();
            AnotherInstance().IsProviderCoolingDown(MetadataSourceType.MyAnimeList).Should().BeFalse();
        }

        [Test]
        public void should_expire_successful_presentation_after_fifteen_minutes_without_extending_expiry_on_reads()
        {
            var now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
            var cache = new SeriesPresentationCache(Mocker.Resolve<ICacheManager>(), () => now);
            var calls = 0;
            Func<SeriesPresentation> fetch = () => new SeriesPresentation { Overview = "Revision " + ++calls };
            cache.Get("anilist:1", fetch).Overview.Should().Be("Revision 1");
            now += TimeSpan.FromMinutes(14);
            cache.Get("anilist:1", fetch).Overview.Should().Be("Revision 1");
            now += TimeSpan.FromMinutes(1);
            cache.Get("anilist:1", fetch).Overview.Should().Be("Revision 2");
            calls.Should().Be(2);
        }

        [Test]
        public void should_expire_missing_result_and_provider_cooldown_after_two_minutes()
        {
            var now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
            var cache = new SeriesPresentationCache(Mocker.Resolve<ICacheManager>(), () => now);
            cache.Get("jikan:1", () => null).Should().BeNull();
            cache.BackOff(MetadataSourceType.MyAnimeList);
            now += TimeSpan.FromSeconds(119);
            cache.Get("jikan:1", () => new SeriesPresentation { Overview = "Should remain cached" }).Should().BeNull();
            cache.IsProviderCoolingDown(MetadataSourceType.MyAnimeList).Should().BeTrue();
            now += TimeSpan.FromSeconds(1);
            cache.Get("jikan:1", () => new SeriesPresentation { Overview = "Recovered" }).Overview.Should().Be("Recovered");
            cache.IsProviderCoolingDown(MetadataSourceType.MyAnimeList).Should().BeFalse();
        }
    }
}
