-- Pure SQL (no psql meta-commands). Expects a populated temp table _import(doc jsonb).
-- MUST run inside a transaction (the temp tables are `on commit drop`).
--
-- Schema is owned by the FluentMigrator migrations (Movies.Application/Database/Migrations);
-- this script only inserts/updates data and must never define or alter structure.
--
-- Upserts movies/genres/movie_metadata keyed on tmdb_id, refreshing existing TMDB movies IN
-- PLACE (same movies.id, so ratings and genre FKs survive). Movies with no movie_metadata row
-- (i.e. manual / API-created) are never touched. Ratings are never touched.

-- 1. Stage and dedupe on tmdb_id. Slug MUST match Movies.Application/Models/Movie.GenerateSlug.
create temp table _staged on commit drop as
with parsed as (
    select
        (i.doc->>'tmdb_id')::bigint                            as tmdb_id,
        i.doc->>'Title'                                        as title,
        (i.doc->>'YearOfRelease')::int                         as yearofrelease,
        replace(lower(regexp_replace(i.doc->>'Title', '[^0-9A-Za-z _-]', '', 'g')), ' ', '-')
            || '-' || (i.doc->>'YearOfRelease')                as slug,
        i.doc->'Genres'                                        as genres,
        i.doc->'raw'                                           as raw,
        row_number() over (partition by (i.doc->>'tmdb_id')::bigint
                           order by (i.doc->>'tmdb_id'))        as rn
    from _import i
)
select tmdb_id, title, yearofrelease, slug, genres, raw
from parsed
where rn = 1;

-- 2. Resolve identity: reuse the existing movie id for a known tmdb_id, else a fresh uuid.
create temp table _resolved on commit drop as
select
    coalesce(mm.movieid, gen_random_uuid()) as movieid,
    s.tmdb_id, s.title, s.yearofrelease, s.slug, s.genres, s.raw
from _staged s
left join movie_metadata mm on mm.tmdb_id = s.tmdb_id;

-- 3a. Slug-collision guard: a staged movie must not take a slug already held by a DIFFERENT
--     movie (a manual insert or another TMDB movie). Those rows are reported and excluded.
create temp table _rejected on commit drop as
select r.tmdb_id, r.slug
from _resolved r
join movies m on m.slug = r.slug and m.id <> r.movieid;

delete from _resolved where tmdb_id in (select tmdb_id from _rejected);

-- 3b. Within-batch slug collision between two brand-new movies (same title + year, different
--     tmdb_id): keep the lowest tmdb_id, drop the rest, to satisfy the unique slug index.
delete from _resolved r
using (
    select slug, min(tmdb_id) as keep_tmdb
    from _resolved
    group by slug
    having count(*) > 1
) dup
where r.slug = dup.slug and r.tmdb_id <> dup.keep_tmdb;

-- 4. Report (before the upsert, while movie_metadata still reflects the prior state).
do $$
declare
    v_inserted int;
    v_updated  int;
    v_skipped  int;
begin
    select count(*) into v_skipped from _rejected;
    select
        count(*) filter (where mm.movieid is null),
        count(*) filter (where mm.movieid is not null)
    into v_inserted, v_updated
    from _resolved r
    left join movie_metadata mm on mm.movieid = r.movieid;

    raise notice 'Import: % inserted, % refreshed, % skipped (slug collision)',
        v_inserted, v_updated, v_skipped;
end $$;

-- 5. Upsert movies (conflict on the primary key id; known rows update in place).
insert into movies (id, slug, title, yearofrelease)
select movieid, slug, title, yearofrelease from _resolved
on conflict (id) do update
set slug          = excluded.slug,
    title         = excluded.title,
    yearofrelease = excluded.yearofrelease;

-- 6. Upsert movie_metadata (conflict on movieid; refresh raw + fetched_at for known movies).
insert into movie_metadata (movieid, tmdb_id, raw, fetched_at)
select movieid, tmdb_id, raw, now() from _resolved
on conflict (movieid) do update
set tmdb_id    = excluded.tmdb_id,
    raw        = excluded.raw,
    fetched_at = now();

-- 7. Refresh genres for the imported movies only (scoped to _resolved ids, so manual movies'
--    genres are never touched).
delete from genres where movieid in (select movieid from _resolved);

insert into genres (movieid, name)
select r.movieid, g.value
from _resolved r, jsonb_array_elements_text(r.genres) as g(value);
