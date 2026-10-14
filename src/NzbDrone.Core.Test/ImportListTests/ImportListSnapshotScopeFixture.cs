using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ImportListSnapshotScopeFixture : CoreTest
    {
        [Test]
        public void should_flow_one_scope_to_parallel_import_tasks_and_coalesce_an_inflight_read()
        {
            using var scope = ImportListSnapshotScope.Begin();
            using var release = new ManualResetEventSlim();
            using var started = new ManualResetEventSlim();
            var key = ImportListSnapshotScope.Fingerprint("account", "generated-token", "all-statuses-v1");
            var calls = 0;
            var entered = 0;
            var tasks = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(() =>
            {
                ImportListSnapshotScope.Current.Should().BeSameAs(scope);
                Interlocked.Increment(ref entered);
                return scope.GetOrLoad(key, () =>
                {
                    Interlocked.Increment(ref calls);
                    started.Set();
                    release.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
                    return new[] { 1, 2, 3 };
                });
            },
            TaskCreationOptions.LongRunning)).ToArray();

            try
            {
                started.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                SpinWait.SpinUntil(() => Volatile.Read(ref entered) == 8, TimeSpan.FromSeconds(5)).Should().BeTrue();
                scope.TryGet<int[]>(key, out _).Should().BeFalse();
                scope.GetCompletedValues<int[]>().Should().BeEmpty();
            }
            finally
            {
                release.Set();
            }

            Task.WaitAll(tasks, TimeSpan.FromSeconds(10)).Should().BeTrue();
            calls.Should().Be(1);
            tasks.Should().OnlyContain(task => ReferenceEquals(task.Result, tasks[0].Result));
            scope.TryGet<int[]>(key, out var completed).Should().BeTrue();
            completed.Should().Equal(1, 2, 3);
        }

        [Test]
        public void should_share_failure_only_for_the_current_scope_and_retry_after_cleanup()
        {
            var calls = 0;
            var key = ImportListSnapshotScope.Fingerprint("account", "generated-token");
            var scope = ImportListSnapshotScope.Begin();
            try
            {
                Action read = () => scope.GetOrLoad<string>(key, () =>
                {
                    calls++;
                    throw new ImportListException(null, "Incomplete account list.");
                });
                read.Should().Throw<ImportListException>();
                read.Should().Throw<ImportListException>();
                calls.Should().Be(1);
                scope.TryGet<string>(key, out _).Should().BeFalse();
                scope.GetCompletedValues<string>().Should().BeEmpty();
            }
            finally
            {
                scope.Dispose();
            }

            ImportListSnapshotScope.Current.Should().BeNull();
            scope.RetainedBytes.Should().Be(0);
            using var retry = ImportListSnapshotScope.Begin();
            retry.GetOrLoad(key, () =>
            {
                calls++;
                return "complete";
            }).Should().Be("complete");
            calls.Should().Be(2);
        }

        [Test]
        public void should_isolate_an_unsaved_test_scope_and_restore_the_import_scope()
        {
            using var import = ImportListSnapshotScope.Begin();
            var key = ImportListSnapshotScope.Fingerprint("same-account-and-auth");
            import.GetOrLoad(key, () => "saved import result");
            using (var test = ImportListSnapshotScope.Begin())
            {
                test.GetOrLoad(key, () => "unsaved Test result").Should().Be("unsaved Test result");
                ImportListSnapshotScope.Current.Should().BeSameAs(test);
            }

            ImportListSnapshotScope.Current.Should().BeSameAs(import);
            import.GetOrLoad(key, () => "unexpected new load").Should().Be("saved import result");
        }

        [Test]
        public void should_release_every_payload_when_an_import_throws_and_forbid_stale_reuse()
        {
            ImportListSnapshotScope scope = null;
            Action import = () =>
            {
                using var active = ImportListSnapshotScope.Begin();
                scope = active;
                active.GetOrLoad(ImportListSnapshotScope.Fingerprint("private-account"), () => new[] { "member" });
                throw new InvalidOperationException("Import processing failed.");
            };
            import.Should().Throw<InvalidOperationException>();
            ImportListSnapshotScope.Current.Should().BeNull();
            scope.RetainedSnapshotCount.Should().Be(0);
            scope.RetainedBytes.Should().Be(0);
            scope.GetCompletedValues<string[]>().Should().BeEmpty();
            Action stale = () => scope.GetOrLoad(ImportListSnapshotScope.Fingerprint("private-account"), () => "unexpected");
            stale.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void should_partition_account_auth_and_query_schema_without_ambiguous_component_boundaries()
        {
            var original = ImportListSnapshotScope.Fingerprint("user-a", "auth-a", "ANIME", "schema-v1");
            original.Should().NotBe(ImportListSnapshotScope.Fingerprint("user-b", "auth-a", "ANIME", "schema-v1"));
            original.Should().NotBe(ImportListSnapshotScope.Fingerprint("user-a", "auth-b", "ANIME", "schema-v1"));
            original.Should().NotBe(ImportListSnapshotScope.Fingerprint("user-a", "auth-a", "TV", "schema-v1"));
            original.Should().NotBe(ImportListSnapshotScope.Fingerprint("user-a", "auth-a", "ANIME", "schema-v2"));
            ImportListSnapshotScope.Fingerprint("ab", "c").Should().NotBe(ImportListSnapshotScope.Fingerprint("a", "bc"));
            original.Should().NotContain("user-a").And.NotContain("auth-a");
        }

        [Test]
        public void should_never_start_a_load_when_inspecting_completed_public_snapshots()
        {
            using var scope = ImportListSnapshotScope.Begin();
            var key = ImportListSnapshotScope.Fingerprint("public-relations");
            scope.TryGet<string>(key, out _).Should().BeFalse();
            scope.GetCompletedValues<string>().Should().BeEmpty();
            scope.GetOrLoad(key, () => "validated public relations");
            scope.GetOrLoad(key, () => 7);
            scope.GetCompletedValues<string>().Should().Equal("validated public relations");
            scope.GetCompletedValues<int>().Should().Equal(7);
        }

        [Test]
        public void should_bound_snapshot_count_without_truncating_overflow_results()
        {
            using var scope = ImportListSnapshotScope.Begin();
            for (var index = 0; index < ImportListSnapshotScope.MaxSnapshots; index++)
            {
                scope.GetOrLoad(ImportListSnapshotScope.Fingerprint("selected-list", index.ToString()), () => new[] { 1, 2, 3 });
            }

            var calls = 0;
            var overflow = ImportListSnapshotScope.Fingerprint("overflow-list");
            Func<int[]> load = () =>
            {
                calls++;
                return new[] { 4, 5, 6 };
            };
            scope.GetOrLoad(overflow, load).Should().Equal(4, 5, 6);
            scope.GetOrLoad(overflow, load).Should().Equal(4, 5, 6);
            calls.Should().Be(2);
            scope.RetainedSnapshotCount.Should().Be(ImportListSnapshotScope.MaxSnapshots);
        }

        [Test]
        public void should_bound_retained_payload_size_and_keep_the_overflow_result_complete()
        {
            using var scope = ImportListSnapshotScope.Begin();
            for (var index = 0; index < 4; index++)
            {
                scope.GetOrLoad(
                    ImportListSnapshotScope.Fingerprint("compact-complete-account", index.ToString()),
                    () => new[] { 1, 2, 3 },
                    _ => ImportListSnapshotScope.MaxSnapshotBytes);
            }

            var calls = 0;
            var overflow = ImportListSnapshotScope.Fingerprint("another-complete-account");
            Func<int[]> load = () =>
            {
                calls++;
                return new[] { 4, 5, 6 };
            };
            scope.GetOrLoad(overflow, load, _ => 1).Should().Equal(4, 5, 6);
            scope.GetOrLoad(overflow, load, _ => 1).Should().Equal(4, 5, 6);
            scope.RetainedBytes.Should().Be(ImportListSnapshotScope.MaxRetainedBytes);
            calls.Should().Be(2);
        }

        [Test]
        public void should_count_actual_multibyte_response_bytes_and_reject_an_oversized_complete_response()
        {
            long bytes = 0;
            ImportListSnapshotScope.AddPayloadBytes(ref bytes, "é");
            bytes.Should().Be(2);
            bytes = ImportListSnapshotScope.MaxSnapshotBytes - 1;
            Action read = () => ImportListSnapshotScope.AddPayloadBytes(ref bytes, "é");
            read.Should().Throw<ImportListException>();
            bytes.Should().Be(ImportListSnapshotScope.MaxSnapshotBytes - 1);
        }

        [Test]
        public void should_copy_import_membership_and_nested_monitoring_objects_before_definition_updates()
        {
            var stored = new ImportListItemInfo
            {
                MalId = 21,
                Seasons = new List<Season> { new() { SeasonNumber = 1, Monitored = true, Images = new List<NzbDrone.Core.MediaCover.MediaCover> { new() { Url = "public-cover" } } } }
            };
            var first = ImportListSnapshotScope.CopyItems(new[] { stored }).Single();
            first.ImportListId = 10;
            first.Seasons[0].Monitored = false;
            first.Seasons[0].Images[0].Url = "changed";
            var second = ImportListSnapshotScope.CopyItems(new[] { stored }).Single();
            second.ImportListId.Should().Be(0);
            second.Seasons[0].Monitored.Should().BeTrue();
            second.Seasons[0].Images[0].Url.Should().Be("public-cover");
            stored.ImportListId.Should().Be(0);
        }
    }
}
