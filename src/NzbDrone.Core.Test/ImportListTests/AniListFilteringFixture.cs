using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList.List;
using NzbDrone.Core.ImportLists.Exceptions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class AniListFilteringFixture : CoreTest
    {
        private AniListSettings _settings;

        [SetUp]
        public void Setup()
        {
            _settings = new AniListSettings { Username = "someone" };
        }

        private static object Entry(int id, string status = "CURRENT", Dictionary<string, bool> customLists = null, string releaseStatus = "FINISHED")
        {
            return new
            {
                status,
                customLists,
                media = new { id, status = releaseStatus, title = new { userPreferred = "Example anime" } }
            };
        }

        private static ImportListResponse Response(params object[] entries)
        {
            var content = JsonSerializer.Serialize(new
            {
                data = new
                {
                    Page = new
                    {
                        pageInfo = new { currentPage = 1, hasNextPage = false },
                        mediaList = entries
                    }
                }
            });
            var request = new HttpRequest("https://graphql.anilist.co");
            var response = new HttpResponse(request, new HttpHeader(), Encoding.UTF8.GetBytes(content));

            return new ImportListResponse(new ImportListRequest(request), response);
        }

        private void EnableStatus(string status)
        {
            _settings.ImportCurrent = status == "CURRENT";
            _settings.ImportPlanning = status == "PLANNING";
            _settings.ImportCompleted = status == "COMPLETED";
            _settings.ImportDropped = status == "DROPPED";
            _settings.ImportPaused = status == "PAUSED";
            _settings.ImportRepeating = status == "REPEATING";
        }

        private void ExcludeStatus(string status)
        {
            _settings.ExcludeCurrent = status == "CURRENT";
            _settings.ExcludePlanning = status == "PLANNING";
            _settings.ExcludeCompleted = status == "COMPLETED";
            _settings.ExcludeDropped = status == "DROPPED";
            _settings.ExcludePaused = status == "PAUSED";
            _settings.ExcludeRepeating = status == "REPEATING";
        }

        [Test]
        public void should_preserve_default_watching_and_planning_inclusions_without_custom_filters()
        {
            var parser = new AniListParser(_settings);

            var result = parser.ParseResponse(Response(Entry(1), Entry(2, "PLANNING"), Entry(3, "COMPLETED")));

            result.Select(item => item.AniListId).Should().Equal(1, 2);
        }

        [TestCase("CURRENT")]
        [TestCase("PLANNING")]
        [TestCase("COMPLETED")]
        [TestCase("DROPPED")]
        [TestCase("PAUSED")]
        [TestCase("REPEATING")]
        public void should_apply_each_selected_watching_status_locally(string status)
        {
            EnableStatus(status);
            var parser = new AniListParser(_settings);

            parser.ParseResponse(Response(Entry(1, status))).Should().ContainSingle().Which.AniListId.Should().Be(1);
        }

        [TestCase("CURRENT")]
        [TestCase("PLANNING")]
        [TestCase("COMPLETED")]
        [TestCase("DROPPED")]
        [TestCase("PAUSED")]
        [TestCase("REPEATING")]
        public void should_let_each_excluded_watching_status_override_its_inclusion(string status)
        {
            EnableStatus(status);
            ExcludeStatus(status);
            _settings.IncludedCustomLists = new[] { "Seasonal" };
            var parser = new AniListParser(_settings);

            parser.ParseResponse(Response(Entry(1, status, new Dictionary<string, bool> { { "Seasonal", true } }))).Should().BeEmpty();
            parser.ExcludedMediaIds.Should().Contain(1);
        }

        [Test]
        public void should_require_custom_membership_and_selected_watching_and_release_statuses()
        {
            _settings.IncludedCustomLists = new[] { "Seasonal" };
            _settings.ImportReleasing = false;
            var onList = new Dictionary<string, bool> { { "Seasonal", true } };
            var parser = new AniListParser(_settings);

            var result = parser.ParseResponse(Response(
                Entry(1, "CURRENT", onList),
                Entry(2, "COMPLETED", onList),
                Entry(3, "CURRENT", onList, "RELEASING"),
                Entry(4, "CURRENT", new Dictionary<string, bool> { { "Seasonal", false } })));

            result.Should().ContainSingle().Which.AniListId.Should().Be(1);
        }

        [Test]
        public void should_include_members_of_any_selected_custom_list()
        {
            _settings.IncludedCustomLists = new[] { "Seasonal", "Rewatch" };
            var parser = new AniListParser(_settings);

            var result = parser.ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "Seasonal", true }, { "Rewatch", false } }),
                Entry(2, customLists: new Dictionary<string, bool> { { "Seasonal", false }, { "Rewatch", true } }),
                Entry(3, customLists: new Dictionary<string, bool> { { "Seasonal", false }, { "Rewatch", false } })));

            result.Select(item => item.AniListId).Should().Equal(1, 2);
        }

        [Test]
        public void should_combine_legacy_custom_list_with_new_inclusions()
        {
            _settings.CustomList = "Legacy";
            _settings.IncludedCustomLists = new[] { "Seasonal" };
            var parser = new AniListParser(_settings);

            var result = parser.ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "Legacy", true }, { "Seasonal", false } }),
                Entry(2, customLists: new Dictionary<string, bool> { { "Legacy", false }, { "Seasonal", true } }),
                Entry(3, customLists: new Dictionary<string, bool> { { "Legacy", false }, { "Seasonal", false } })));

            result.Select(item => item.AniListId).Should().Equal(1, 2);
        }

        [Test]
        public void should_veto_watching_anime_on_any_excluded_custom_list()
        {
            _settings.IncludedCustomLists = new[] { "Seasonal" };
            _settings.ExcludedCustomLists = new[] { "Do not import", "Manual only" };
            var parser = new AniListParser(_settings);

            var result = parser.ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "Seasonal", true }, { "Do not import", true }, { "Manual only", false } }),
                Entry(2, customLists: new Dictionary<string, bool> { { "Seasonal", true }, { "Do not import", false }, { "Manual only", true } }),
                Entry(3, customLists: new Dictionary<string, bool> { { "Seasonal", true }, { "Do not import", false }, { "Manual only", false } })));

            result.Should().ContainSingle().Which.AniListId.Should().Be(3);
            parser.ExcludedMediaIds.Should().BeEquivalentTo(new[] { 1, 2 });
        }

        [Test]
        public void should_veto_members_of_a_list_selected_for_both_inclusion_and_exclusion()
        {
            _settings.IncludedCustomLists = new[] { "Seasonal" };
            _settings.ExcludedCustomLists = new[] { "Seasonal" };

            new AniListParser(_settings).ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "Seasonal", true } }))).Should().BeEmpty();
        }

        [Test]
        public void should_not_treat_false_or_absent_excluded_flags_as_membership()
        {
            _settings.ExcludedCustomLists = new[] { "Excluded" };
            var parser = new AniListParser(_settings);

            var result = parser.ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "Excluded", false } }),
                Entry(2, customLists: new Dictionary<string, bool> { { "Other", true } })));

            result.Select(item => item.AniListId).Should().Equal(1, 2);
            parser.ExcludedMediaIds.Should().BeEmpty();
        }

        [TestCase("Excluded", 2)]
        [TestCase("excluded", 1)]
        public void should_respect_exact_case_distinct_excluded_list_names(string selectedName, int retainedId)
        {
            _settings.ExcludedCustomLists = new[] { selectedName };
            var parser = new AniListParser(_settings);

            var result = parser.ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "Excluded", true }, { "excluded", false } }),
                Entry(2, customLists: new Dictionary<string, bool> { { "Excluded", false }, { "excluded", true } })));

            result.Should().ContainSingle().Which.AniListId.Should().Be(retainedId);
        }

        [Test]
        public void should_allow_unique_case_insensitive_excluded_list_names()
        {
            _settings.ExcludedCustomLists = new[] { "EXCLUDED" };

            new AniListParser(_settings).ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "Excluded", true } }))).Should().BeEmpty();
        }

        [Test]
        public void should_fail_when_an_excluded_name_has_multiple_nonexact_case_matches()
        {
            _settings.ExcludedCustomLists = new[] { "EXCLUDED" };
            var parser = new AniListParser(_settings);

            Assert.Throws<ImportListException>(() => parser.ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "Excluded", true }, { "excluded", false } }))));
        }

        [Test]
        public void should_keep_exact_case_distinct_included_names_separate()
        {
            _settings.IncludedCustomLists = new[] { "seasonal" };

            new AniListParser(_settings).ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "Seasonal", true }, { "seasonal", false } }))).Should().BeEmpty();
        }

        [Test]
        public void should_match_a_unique_included_name_case_insensitively()
        {
            _settings.IncludedCustomLists = new[] { "SEASONAL" };

            new AniListParser(_settings).ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "Seasonal", true } }))).Should().ContainSingle();
        }

        [Test]
        public void should_preserve_spaces_in_custom_list_names()
        {
            _settings.IncludedCustomLists = new[] { " Seasonal " };
            _settings.ExcludedCustomLists = new[] { " Excluded " };

            var result = new AniListParser(_settings).ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { " Seasonal ", true }, { " Excluded ", true } }),
                Entry(2, customLists: new Dictionary<string, bool> { { " Seasonal ", true }, { "Excluded", true } }),
                Entry(3, customLists: new Dictionary<string, bool> { { "Seasonal", true }, { " Excluded ", false } })));

            result.Should().ContainSingle().Which.AniListId.Should().Be(2);
        }

        [Test]
        public void should_not_merge_ambiguous_case_insensitive_included_names()
        {
            _settings.IncludedCustomLists = new[] { "SEASONAL" };

            new AniListParser(_settings).ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "Seasonal", true }, { "seasonal", false } }))).Should().BeEmpty();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_fail_when_exclusion_membership_data_is_null_or_missing(bool omitField)
        {
            _settings.ExcludedCustomLists = new[] { "Excluded" };
            var parser = new AniListParser(_settings);
            var entry = omitField
                ? (object)new { status = "CURRENT", media = new { id = 1, status = "FINISHED", title = new { userPreferred = "Example anime" } } }
                : Entry(1);

            Assert.Throws<ImportListException>(() => parser.ParseResponse(Response(entry)));
        }

        [Test]
        public void should_fail_on_missing_exclusion_data_even_when_other_filters_would_remove_the_entry()
        {
            _settings.IncludedCustomLists = new[] { "Seasonal" };
            _settings.ExcludedCustomLists = new[] { "Excluded" };

            Assert.Throws<ImportListException>(() => new AniListParser(_settings).ParseResponse(Response(Entry(1, "COMPLETED", releaseStatus: "CANCELLED"))));
        }

        [TestCase(null)]
        [TestCase("UNKNOWN")]
        public void should_fail_when_exclusions_cannot_evaluate_the_returned_watching_status(string status)
        {
            _settings.ExcludeCompleted = true;

            Assert.Throws<ImportListException>(() => new AniListParser(_settings).ParseResponse(Response(Entry(1, status))));
        }

        [Test]
        public void should_keep_null_custom_data_compatible_without_exclusions()
        {
            new AniListParser(_settings).ParseResponse(Response(Entry(1))).Should().ContainSingle();
            _settings.IncludedCustomLists = new[] { "Seasonal" };
            new AniListParser(_settings).ParseResponse(Response(Entry(1))).Should().BeEmpty();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_veto_duplicate_media_ids_regardless_of_entry_order(bool excludedFirst)
        {
            _settings.IncludedCustomLists = new[] { "Seasonal" };
            _settings.ExcludedCustomLists = new[] { "Excluded" };
            var allowed = Entry(1, customLists: new Dictionary<string, bool> { { "Seasonal", true }, { "Excluded", false } });
            var excluded = Entry(1, customLists: new Dictionary<string, bool> { { "Seasonal", false }, { "Excluded", true } });
            var parser = new AniListParser(_settings);

            parser.ParseResponse(excludedFirst ? Response(excluded, allowed) : Response(allowed, excluded)).Should().BeEmpty();
            parser.ExcludedMediaIds.Should().Equal(1);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_veto_duplicate_media_ids_with_an_excluded_watching_status(bool excludedFirst)
        {
            _settings.ImportCompleted = true;
            _settings.ExcludeCompleted = true;
            var allowed = Entry(1);
            var excluded = Entry(1, "COMPLETED");
            var parser = new AniListParser(_settings);

            parser.ParseResponse(excludedFirst ? Response(excluded, allowed) : Response(allowed, excluded)).Should().BeEmpty();
            parser.ExcludedMediaIds.Should().Equal(1);
        }
    }
}
