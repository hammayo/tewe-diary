namespace Movies.Tests.Shared;

// Literal TMDB-shaped NDJSON lines (the enriched shape produced by tmdb-details-to-ndjson.jq), shared
// by import, repository and API tests so every layer asserts against the same movie.
public static class TmdbFixtures
{
    public const string OdysseySlug = "the-odyssey-2026";

    public const string OdysseyDetailsLine = """
        {"tmdb_id":1368337,"Title":"The Odyssey","YearOfRelease":2026,"Genres":["Adventure"],"raw":{"id":1368337,"imdb_id":"tt33764258","overview":"Odysseus sails home.","tagline":"Defy the gods.","runtime":173,"poster_path":"/poster.jpg","backdrop_path":"/backdrop.jpg","credits":{"cast":[{"id":1892,"name":"Matt Damon","character":"Odysseus","order":0,"profile_path":"/damon.jpg"},{"id":1136406,"name":"Tom Holland","character":"Telemachus","order":1,"profile_path":null}],"crew":[{"id":525,"name":"Christopher Nolan","job":"Director","department":"Directing","profile_path":"/nolan.jpg"},{"id":525,"name":"Christopher Nolan","job":"Writer","department":"Writing","profile_path":"/nolan.jpg"}]},"videos":{"results":[{"key":"Mzw2ttJD2qQ","site":"YouTube","type":"Trailer","official":true,"name":"Official Trailer","published_at":"2025-12-22T14:00:04.000Z"}]}}}
        """;

    // The same movie as a list-only payload (no credits/videos), as older NDJSON files look.
    public const string OdysseyListOnlyLine = """
        {"tmdb_id":1368337,"Title":"The Odyssey","YearOfRelease":2026,"Genres":["Adventure"],"raw":{"id":1368337,"overview":"list overview","poster_path":"/list-poster.jpg"}}
        """;
}
