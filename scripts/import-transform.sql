-- Pure SQL (no psql meta-commands). Expects a populated temp table _import(doc jsonb).
-- Inserts new movies/genres/movie_metadata, deduped on tmdb_id. Ratings untouched.
create temp table _new on commit drop as
select
    gen_random_uuid()                                       as id,
    (i.doc->>'tmdb_id')::bigint                             as tmdb_id,
    i.doc->>'Title'                                         as title,
    (i.doc->>'YearOfRelease')::int                          as yearofrelease,
    -- Slug MUST match Movies.Application/Models/Movie.GenerateSlug.
    replace(lower(regexp_replace(i.doc->>'Title', '[^0-9A-Za-z _-]', '', 'g')), ' ', '-')
        || '-' || (i.doc->>'YearOfRelease')                 as slug,
    i.doc->'Genres'                                         as genres,
    i.doc->'raw'                                            as raw
from _import i
where not exists (
    select 1 from movie_metadata m where m.tmdb_id = (i.doc->>'tmdb_id')::bigint
);

insert into movies (id, slug, title, yearofrelease)
select id, slug, title, yearofrelease from _new;

insert into genres (movieid, name)
select n.id, g.value
from _new n, jsonb_array_elements_text(n.genres) as g(value);

insert into movie_metadata (movieid, tmdb_id, raw)
select id, tmdb_id, raw from _new;
