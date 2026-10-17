namespace Movies.Application.Models;

// The one trailer chosen at fetch time (see tmdb-details-to-ndjson.jq). Site is YouTube or Vimeo.
public record MovieTrailer(string Site, string Key, string? Name);
