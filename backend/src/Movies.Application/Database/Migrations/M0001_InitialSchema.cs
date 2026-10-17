using FluentMigrator;

namespace Movies.Application.Database.Migrations;

// Baseline schema. Verbatim port of the former DbInitializer DDL, kept as raw SQL so SQL
// remains the source of truth for what exists. `if not exists` makes it a safe no-op against
// databases that were already created by the old startup initializer. Later migrations use the
// fluent DSL. Only change vs the old DDL: the unique slug index is created without
// `concurrently`, which cannot run inside FluentMigrator's per-migration transaction.
[Migration(1)]
public class M0001_InitialSchema : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            create table if not exists movies (
                id UUID primary key,
                slug TEXT not null,
                title TEXT not null,
                yearofrelease integer not null);

            create unique index if not exists movies_slug_idx
                on movies using btree(slug);

            create table if not exists genres (
                movieId UUID references movies (Id),
                name TEXT not null);

            create table if not exists ratings (
                userid uuid,
                movieid uuid references movies (id),
                rating integer not null,
                primary key (userid, movieid));

            create table if not exists movie_metadata (
                movieid uuid primary key references movies (id) on delete cascade,
                tmdb_id bigint not null unique,
                raw jsonb not null,
                fetched_at timestamptz not null default now());
            """);
    }

    public override void Down()
    {
        Execute.Sql("""
            drop table if exists movie_metadata;
            drop table if exists ratings;
            drop table if exists genres;
            drop table if exists movies;
            """);
    }
}
