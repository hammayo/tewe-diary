# Input: a TMDB list response. Arg $genres: {"<id>": "<name>"} map.
# Emits one object per movie with a usable release year; others are skipped.
.results[]
| select(.release_date != null and .release_date != "")
| {
    tmdb_id: .id,
    Title: .title,
    YearOfRelease: (.release_date[0:4] | tonumber),
    Genres: [ .genre_ids[] | $genres[tostring] // empty ],
    raw: .
  }
