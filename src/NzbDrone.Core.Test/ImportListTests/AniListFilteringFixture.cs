using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
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
        public void should_ignore_removed_status_exclusions_when_importing_selected_statuses(string status)
        {
            _settings = Json.Deserialize<AniListSettings>("""
                {"username":"someone","excludeCurrent":true,"excludePlanning":true,"excludeCompleted":true,
                 "excludeDropped":true,"excludePaused":true,"excludeRepeating":true}
                """);
            EnableStatus(status);
            _settings.IncludedCustomLists = new[] { "Seasonal" };
            _settings.ExcludedCustomLists = new[] { "Manual only" };
            var parser = new AniListParser(_settings);

            parser.ParseResponse(Response(
                Entry(1, status, new Dictionary<string, bool> { { "Seasonal", true }, { "Manual only", false } }),
                Entry(2, status, new Dictionary<string, bool> { { "Seasonal", false }, { "Manual only", true } })))
                .Should().ContainSingle().Which.AniListId.Should().Be(1);
            parser.ExcludedMediaIds.Should().Equal(2);
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

        [TestCase(true)]
        [TestCase(false)]
        public void should_import_the_eligible_inclusion_union_minus_the_exclusion_union(bool restrictToIncludedLists)
        {
            _settings.IncludedCustomLists = restrictToIncludedLists ? new[] { "A", "B" } : Array.Empty<string>();
            _settings.ExcludedCustomLists = new[] { "C", "D" };
            var parser = new AniListParser(_settings);

            var result = parser.ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "A", true } }),
                Entry(2, customLists: new Dictionary<string, bool> { { "B", true } }),
                Entry(3, customLists: new Dictionary<string, bool> { { "A", true }, { "B", true }, { "C", true } }),
                Entry(4, customLists: new Dictionary<string, bool> { { "A", true }, { "D", true } }),
                Entry(5, customLists: new Dictionary<string, bool> { { "Outside", true } }),
                Entry(6, "COMPLETED", new Dictionary<string, bool> { { "A", true } }),
                Entry(7, customLists: new Dictionary<string, bool> { { "B", true } }, releaseStatus: "CANCELLED"),
                Entry(8, customLists: new Dictionary<string, bool> { { "C", true } })));

            result.Select(item => item.AniListId).Should().Equal(restrictToIncludedLists ? new[] { 1, 2 } : new[] { 1, 2, 5 });
            parser.ExcludedMediaIds.Should().BeEquivalentTo(new[] { 3, 4, 8 });
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

        [TestCase(false)]
        [TestCase(true)]
        public void should_preserve_legacy_import_membership_after_moving_it_to_modern_tags(bool systemTextJson)
        {
            const string legacy = """
                {"username":"someone","customList":" Legacy ","includedCustomLists":["Modern"],"excludedCustomLists":["Manual only"]}
                """;
            var settings = systemTextJson ? STJson.Deserialize<AniListSettings>(legacy) : Json.Deserialize<AniListSettings>(legacy);
            var parser = new AniListParser(settings);

            var result = parser.ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { "Legacy", true }, { "Modern", false }, { "Manual only", false } }),
                Entry(2, customLists: new Dictionary<string, bool> { { "Legacy", false }, { "Modern", true }, { "Manual only", false } }),
                Entry(3, customLists: new Dictionary<string, bool> { { "Legacy", false }, { "Modern", false }, { "Manual only", true } }),
                Entry(4, customLists: new Dictionary<string, bool> { { "Legacy", false }, { "Modern", false }, { "Manual only", false } })));

            result.Select(item => item.AniListId).Should().Equal(1, 2);
            parser.ExcludedMediaIds.Should().Equal(3);
            settings.CustomList.Should().BeEmpty();
        }

        [Test]
        public void should_subtract_excluded_watching_members_from_explicit_custom_inclusions()
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
        public void should_remove_members_of_a_list_selected_for_both_inclusion_and_exclusion()
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
        public void should_reject_an_ambiguous_exclusion_even_when_explicit_inclusion_is_confirmed()
        {
            _settings.IncludedCustomLists = new[] { "Selected" };
            _settings.ExcludedCustomLists = new[] { "EXCLUDED" };

            Assert.Throws<ImportListException>(() => new AniListParser(_settings).ParseResponse(Response(Entry(1,
                customLists: new Dictionary<string, bool> { { "Selected", true }, { "Excluded", true }, { "excluded", false } }))));
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

            _settings.IncludedCustomLists = Array.Empty<string>();
            var exclusionOnly = new AniListParser(_settings).ParseResponse(Response(
                Entry(1, customLists: new Dictionary<string, bool> { { " Excluded ", true } }),
                Entry(2, customLists: new Dictionary<string, bool> { { "Excluded", true } })));
            exclusionOnly.Should().ContainSingle().Which.AniListId.Should().Be(2);
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
        public void should_apply_custom_exclusions_independently_of_an_unknown_list_status(string status)
        {
            _settings.ExcludedCustomLists = new[] { "Excluded" };
            var parser = new AniListParser(_settings);

            parser.ParseResponse(Response(
                Entry(1, status, new Dictionary<string, bool> { { "Excluded", true } }),
                Entry(1, customLists: new Dictionary<string, bool> { { "Excluded", false } }))).Should().BeEmpty();
            parser.ExcludedMediaIds.Should().Contain(1);
        }

        [Test]
        public void should_keep_null_custom_data_compatible_without_custom_filters_but_fail_unknown_inclusions()
        {
            new AniListParser(_settings).ParseResponse(Response(Entry(1))).Should().ContainSingle();
            _settings.IncludedCustomLists = new[] { "Seasonal" };
            Assert.Throws<ImportListException>(() => new AniListParser(_settings).ParseResponse(Response(Entry(1))));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_subtract_excluded_duplicate_media_ids_regardless_of_entry_order(bool excludedFirst)
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
        public void should_keep_exclusion_vetoes_when_no_custom_inclusion_is_selected(bool excludedFirst)
        {
            _settings.ExcludedCustomLists = new[] { "Excluded" };
            var allowed = Entry(1, customLists: new Dictionary<string, bool> { { "Excluded", false } });
            var excluded = Entry(1, customLists: new Dictionary<string, bool> { { "Excluded", true } });
            var parser = new AniListParser(_settings);

            parser.ParseResponse(excludedFirst ? Response(excluded, allowed) : Response(allowed, excluded)).Should().BeEmpty();
            parser.ExcludedMediaIds.Should().Equal(1);
        }

        [TestCase("COMPLETED", "FINISHED")]
        [TestCase("CURRENT", "CANCELLED")]
        public void should_not_allow_custom_inclusion_to_bypass_import_status_or_release_filters(string status, string releaseStatus)
        {
            _settings.IncludedCustomLists = new[] { "Included" };
            _settings.ExcludedCustomLists = new[] { "Excluded" };
            var parser = new AniListParser(_settings);

            parser.ParseResponse(Response(Entry(
                1,
                status,
                new Dictionary<string, bool> { { "Included", true }, { "Excluded", true } },
                releaseStatus))).Should().BeEmpty();
            parser.ExcludedMediaIds.Should().Equal(1);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_keep_imported_statuses_independent_after_legacy_status_exclusions_are_removed(bool completedFirst)
        {
            _settings = Json.Deserialize<AniListSettings>("""
                {"username":"someone","excludeCurrent":true,"excludePlanning":true,"excludeCompleted":true,
                 "excludeDropped":true,"excludePaused":true,"excludeRepeating":true}
                """);
            _settings.ImportCompleted = true;
            var current = Entry(1);
            var completed = Entry(1, "COMPLETED");
            var parser = new AniListParser(_settings);

            parser.ParseResponse(completedFirst ? Response(completed, current) : Response(current, completed))
                .Select(item => item.AniListId).Should().Equal(1, 1);
            parser.ExcludedMediaIds.Should().BeEmpty();
        }
    }
}
