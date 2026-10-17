using System.Data;
using FluentMigrator;

namespace Movies.Application.Database.Migrations;

// TMDB movie details (PRD FR-1…FR-8): imdb_id on movie_metadata, plus movie_details (1:1) and
// movie_credits (1:n). Expand-only, so it is safe to run before the new image is deployed. Both new
// tables cascade on movie delete, so MovieRepository.DeleteByIdAsync needs no change. CHECK
// constraints have no fluent equivalent and are added as raw SQL.
[Migration(2)]
public class M0002_MovieDetails : Migration
{
    public override void Up()
    {
        Alter.Table("movie_metadata").AddColumn("imdb_id").AsString().Nullable();
        // Non-unique on purpose: TMDB occasionally has duplicate entries sharing an IMDb id, and a
        // unique index would fail the whole import transaction.
        Create.Index("movie_metadata_imdb_id_idx").OnTable("movie_metadata").OnColumn("imdb_id");

        Create.Table("movie_details")
            .WithColumn("movieid").AsGuid().PrimaryKey("movie_details_pk")
                .ForeignKey("movie_details_movie_fk", "movies", "id").OnDelete(Rule.Cascade)
            .WithColumn("overview").AsString().Nullable()
            .WithColumn("tagline").AsString().Nullable()
            .WithColumn("runtime_minutes").AsInt32().Nullable()
            .WithColumn("poster_path").AsString().Nullable()
            .WithColumn("backdrop_path").AsString().Nullable()
            .WithColumn("trailer_site").AsString().Nullable()
            .WithColumn("trailer_key").AsString().Nullable()
            .WithColumn("trailer_name").AsString().Nullable();

        Execute.Sql("""
            alter table movie_details
                add constraint movie_details_trailer_site_chk check (trailer_site in ('YouTube', 'Vimeo')),
                add constraint movie_details_trailer_pair_chk check ((trailer_site is null) = (trailer_key is null));
            """);

        Create.Table("movie_credits")
            .WithColumn("movieid").AsGuid().NotNullable()
                .ForeignKey("movie_credits_movie_fk", "movies", "id").OnDelete(Rule.Cascade)
            .WithColumn("credit_type").AsString().NotNullable()
            .WithColumn("ordinal").AsInt32().NotNullable()
            .WithColumn("tmdb_person_id").AsInt64().NotNullable()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("role").AsString().Nullable()
            .WithColumn("profile_path").AsString().Nullable();

        Create.PrimaryKey("movie_credits_pk").OnTable("movie_credits")
            .Columns("movieid", "credit_type", "ordinal");

        Execute.Sql("""
            alter table movie_credits
                add constraint movie_credits_type_chk check (credit_type in ('director', 'writer', 'cast'));
            """);
    }

    public override void Down()
    {
        Delete.Table("movie_credits");
        Delete.Table("movie_details");
        Delete.Index("movie_metadata_imdb_id_idx").OnTable("movie_metadata");
        Delete.Column("imdb_id").FromTable("movie_metadata");
    }
}
