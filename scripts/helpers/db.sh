# Docker db helpers shared by the ops scripts (sourced, not executed). Needs REPO_ROOT set.

# Runs SQL from stdin in the db container: unaligned, space-separated, no headers; errors fail the call.
db_query() {
  docker compose -f "$REPO_ROOT/docker-compose.yml" exec -T db \
    sh -c 'psql -q -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tA -F " " -v ON_ERROR_STOP=1'
}

# Prints one line:
#   movies tmdb with_details credits posters trailers taglines directors writers cast last_import
# "with_details" = TMDB movies imported from a details fetch (movie_metadata.raw has 'credits').
# last_import is the newest movie_metadata.fetched_at as "YYYY-MM-DD HH:MM UTC" (empty if none) and
# comes last because it contains spaces: read it with `read -r ... last_import`.
db_counts() {
  db_query <<'SQL'
select (select count(*) from movies),
       (select count(*) from movie_metadata),
       (select count(*) from movie_metadata where raw ? 'credits'),
       (select count(*) from movie_credits),
       (select count(*) from movie_details where poster_path is not null),
       (select count(*) from movie_details where trailer_key is not null),
       (select count(*) from movie_details where tagline is not null),
       (select count(*) from movie_credits where credit_type = 'director'),
       (select count(*) from movie_credits where credit_type = 'writer'),
       (select count(*) from movie_credits where credit_type = 'cast'),
       (select to_char(max(fetched_at) at time zone 'UTC', 'YYYY-MM-DD HH24:MI "UTC"') from movie_metadata);
SQL
}
