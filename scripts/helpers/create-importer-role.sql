-- One-time infra/DBA step: creates the least-privilege role the TMDB import runs as.
-- The import needs DML on the app tables plus temp tables, but NO DDL — migrations use a
-- separate DDL-capable role, and the web app its own read/write role. Ratings are intentionally
-- excluded: the importer never touches them.
--
-- Run with an admin role, substituting the database name and a real password:
--   psql -v dbname=movies -v importer_password='...' -f scripts/helpers/create-importer-role.sql
--
-- On Azure Database for PostgreSQL Flexible Server, create the login role via the portal /
-- azure_pg_admin if direct CREATE ROLE is restricted, then run the GRANTs below.

\set ON_ERROR_STOP on

do $$
begin
    if not exists (select 1 from pg_roles where rolname = 'movies_importer') then
        execute format('create role movies_importer login password %L', :'importer_password');
    end if;
end $$;

grant connect on database :"dbname" to movies_importer;
grant temporary on database :"dbname" to movies_importer;   -- for `create temp table _import`
grant usage on schema public to movies_importer;
grant select, insert, update, delete on movies, genres, movie_metadata to movies_importer;
