# Input: one TMDB /movie/{id} response fetched with append_to_response=credits,videos.
# Emits one NDJSON line (same top-level shape as tmdb-to-ndjson.jq) with credits and videos
# trimmed to what the import stores. Movies without a usable release year are skipped.
def writer_jobs: ["Screenplay", "Writer", "Story", "Novel", "Author"];

# One trailer: YouTube/Vimeo only; Trailer else Teaser; official first; "Official Trailer" first;
# newest first. sort_by is stable, so the recency order survives the preference sort.
# published_at is sorted as an ISO string (fromdate rejects TMDB's millisecond timestamps).
def pick_trailer:
  [ .[] | select(.site == "YouTube" or .site == "Vimeo") ] as $v
  | ([ $v[] | select(.type == "Trailer") ] | if length > 0 then . else [ $v[] | select(.type == "Teaser") ] end)
  | sort_by(.published_at) | reverse
  | sort_by([ (.official | not), (.name != "Official Trailer") ])
  | .[:1]
  | map({ key, site, type, official, name, published_at });

select(.release_date != null and .release_date != "")
| {
    tmdb_id: .id,
    Title: .title,
    YearOfRelease: (.release_date[0:4] | tonumber),
    Genres: [ (.genres // [])[].name ],
    raw: (
      . + {
        credits: {
          cast: ((.credits.cast // []) | sort_by(.order) | .[:10]
                 | map({ id, name, character, order, profile_path })),
          crew: [ (.credits.crew // [])[]
                  | select(.job == "Director"
                           or (.department == "Writing" and ([.job] | inside(writer_jobs))))
                  | { id, name, job, department, profile_path } ]
        },
        videos: { results: ((.videos.results // []) | pick_trailer) }
      }
    )
  }
