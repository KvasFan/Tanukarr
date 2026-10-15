using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(253)]
    public class movie_metadata_and_special_members : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Series")
                .AddColumn("MediaType").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("AnimeGroupSpecialNumber").AsInt32().Nullable();

            // A user-editable collection role is not a catalogue media type. Only
            // existing anime members with one persisted native feature are eligible;
            // other entries keep their type until their owning provider refreshes it.
            Execute.Sql("""
                UPDATE "Series" SET "MediaType" = 1
                WHERE "AnimeGroupMemberKind" = 1 AND "MetadataSource" IN (2, 3)
                  AND (SELECT COUNT(*) FROM "Episodes" WHERE "SeriesId" = "Series"."Id") = 1
                  AND EXISTS (SELECT 1 FROM "Episodes" WHERE "SeriesId" = "Series"."Id" AND "SeasonNumber" = 1 AND "EpisodeNumber" = 1)
                """);
            Create.Index("IX_Series_AnimeGroupId_Special")
                .OnTable("Series").OnColumn("AnimeGroupId").Ascending()
                .OnColumn("AnimeGroupMemberKind").Ascending()
                .OnColumn("AnimeGroupSpecialNumber").Ascending().WithOptions().Unique();
            Alter.Table("NamingConfig").AddColumn("MovieFormat").AsString().NotNullable()
                .WithDefaultValue("{Series Title} ({Series Year}) {Quality Full}");

            // Anchors alone cannot protect a member that owns several native seasons.
            // These guards execute in the same transaction as each write, including
            // writers outside SeriesService. Native season zero never reserves a slot.
            foreach (var operation in new[] { "INSERT", "UPDATE" })
            {
                IfDatabase(ProcessorIdConstants.SQLite).Execute.Sql($"""
                    CREATE TRIGGER "TR_Series_CollectionSeasonRange_{operation}" BEFORE {operation} ON "Series"
                    WHEN NEW."AnimeGroupingMode" <> 2 AND NEW."AnimeGroupMemberKind" = 0
                      AND NEW."AnimeGroupId" IS NOT NULL AND NEW."AnimeGroupSeasonNumber" > 0
                    BEGIN
                        SELECT RAISE(ABORT, 'Collection season range overlaps: Series.AnimeGroupSeasonNumber')
                        WHERE EXISTS (
                            SELECT 1 FROM "Series" other
                            WHERE other."Id" <> NEW."Id" AND other."AnimeGroupingMode" <> 2
                              AND other."AnimeGroupMemberKind" = 0 AND other."AnimeGroupId" = NEW."AnimeGroupId"
                              AND other."AnimeGroupSeasonNumber" > 0
                              AND other."AnimeGroupSeasonNumber" <> NEW."AnimeGroupSeasonNumber"
                              AND other."AnimeGroupSeasonNumber" < NEW."AnimeGroupSeasonNumber" + MAX(1, (
                                  SELECT COUNT(DISTINCT COALESCE(json_extract(value, '$.seasonNumber'), json_extract(value, '$.SeasonNumber')))
                                  FROM json_each(COALESCE(NEW."Seasons", '[]'))
                                  WHERE COALESCE(json_extract(value, '$.seasonNumber'), json_extract(value, '$.SeasonNumber')) > 0))
                              AND NEW."AnimeGroupSeasonNumber" < other."AnimeGroupSeasonNumber" + MAX(1, (
                                  SELECT COUNT(DISTINCT COALESCE(json_extract(value, '$.seasonNumber'), json_extract(value, '$.SeasonNumber')))
                                  FROM json_each(COALESCE(other."Seasons", '[]'))
                                  WHERE COALESCE(json_extract(value, '$.seasonNumber'), json_extract(value, '$.SeasonNumber')) > 0)));
                    END;
                    """);
            }

            IfDatabase(ProcessorIdConstants.PostgreSQL).Execute.Sql("""
                CREATE FUNCTION tanukarr_validate_collection_season_range() RETURNS trigger AS $$
                BEGIN
                    IF NEW."AnimeGroupingMode" <> 2 AND NEW."AnimeGroupMemberKind" = 0
                       AND NEW."AnimeGroupId" IS NOT NULL AND NEW."AnimeGroupSeasonNumber" > 0 THEN
                        PERFORM pg_advisory_xact_lock(hashtextextended(NEW."AnimeGroupId", 0));
                        IF EXISTS (
                            SELECT 1 FROM "Series" other
                            WHERE other."Id" <> NEW."Id" AND other."AnimeGroupingMode" <> 2
                              AND other."AnimeGroupMemberKind" = 0 AND other."AnimeGroupId" = NEW."AnimeGroupId"
                              AND other."AnimeGroupSeasonNumber" > 0
                              AND other."AnimeGroupSeasonNumber" <> NEW."AnimeGroupSeasonNumber"
                              AND other."AnimeGroupSeasonNumber" < NEW."AnimeGroupSeasonNumber" + GREATEST(1, (
                                  SELECT COUNT(DISTINCT COALESCE(value ->> 'seasonNumber', value ->> 'SeasonNumber')::integer)
                                  FROM jsonb_array_elements(COALESCE(NULLIF(NEW."Seasons", 'null'), '[]')::jsonb)
                                  WHERE COALESCE(value ->> 'seasonNumber', value ->> 'SeasonNumber')::integer > 0))
                              AND NEW."AnimeGroupSeasonNumber" < other."AnimeGroupSeasonNumber" + GREATEST(1, (
                                  SELECT COUNT(DISTINCT COALESCE(value ->> 'seasonNumber', value ->> 'SeasonNumber')::integer)
                                  FROM jsonb_array_elements(COALESCE(NULLIF(other."Seasons", 'null'), '[]')::jsonb)
                                  WHERE COALESCE(value ->> 'seasonNumber', value ->> 'SeasonNumber')::integer > 0))) THEN
                            RAISE EXCEPTION 'Collection season range overlaps: Series.AnimeGroupSeasonNumber'
                                USING ERRCODE = '23505', CONSTRAINT = 'IX_Series_AnimeGroupSeasonRange';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER "TR_Series_CollectionSeasonRange" BEFORE INSERT OR UPDATE ON "Series"
                    FOR EACH ROW EXECUTE FUNCTION tanukarr_validate_collection_season_range();
                """);
        }
    }
}
