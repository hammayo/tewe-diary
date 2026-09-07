-- Pure SQL (no psql meta-commands). Expects a populated temp table _import(doc jsonb).
-- Inserts new movies/genres/movie_metadata, deduped on tmdb_id and slug. Ratings untouched.
create temp table _new on commit drop as
with ranked_by_tmdb as (
    select
        gen_random_uuid()                                       as id,
        (i.doc->>'tmdb_id')::bigint                             as tmdb_id,
        i.doc->>'Title'                                         as title,
        (i.doc->>'YearOfRelease')::int                          as yearofrelease,
        -- Slug MUST match Movies.Application/Models/Movie.GenerateSlug.
        replace(lower(regexp_replace(i.doc->>'Title', '[^0-9A-Za-z _-]', '', 'g')), ' ', '-')
            || '-' || (i.doc->>'YearOfRelease')                 as slug,
        i.doc->'Genres'                                         as genres,
        i.doc->'raw'                                            as raw,
        row_number() over (partition by (i.doc->>'tmdb_id')::bigint order by (i.doc->>'tmdb_id')) as rn_tmdb
    from _import i
    where not exists (
        select 1 from movie_metadata m where m.tmdb_id = (i.doc->>'tmdb_id')::bigint
    )
),
deduped_tmdb as (
    select id, tmdb_id, title, yearofrelease, slug, genres, raw
    from ranked_by_tmdb
    where rn_tmdb = 1
),
ranked_by_slug as (
    select
        id, tmdb_id, title, yearofrelease, slug, genres, raw,
        row_number() over (partition by slug order by tmdb_id) as rn_slug
    from deduped_tmdb
    where not exists (
        select 1 from movies mv where mv.slug = deduped_tmdb.slug
    )
)
select id, tmdb_id, title, yearofrelease, slug, genres, raw
from ranked_by_slug
where rn_slug = 1;

insert into movies (id, slug, title, yearofrelease)
select id, slug, title, yearofrelease from _new;

insert into genres (movieid, name)
select n.id, g.value
from _new n, jsonb_array_elements_text(n.genres) as g(value);

insert into movie_metadata (movieid, tmdb_id, raw)
select id, tmdb_id, raw from _new;
