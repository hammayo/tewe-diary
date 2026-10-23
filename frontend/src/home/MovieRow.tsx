import { Star } from "lucide-react";
import { useMemo, useState } from "react";
import { useMovieList } from "@/api/queries";
import { useFocusNav } from "@/focus/FocusProvider";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiErrorPanel } from "@/shell/ApiErrorPanel";
import { MovieCard } from "./MovieCard";
import type { RowDefinition } from "./rows";

const LOAD_MORE_WITHIN = 3; // fetch the next page when focus is this close to the end

export function MovieRow({ row, rowIndex, onSelect }: { row: RowDefinition; rowIndex: number; onSelect: (slug: string) => void }) {
  const { register, focusAt } = useFocusNav();
  const query = useMovieList(row.query);
  const [focusedId, setFocusedId] = useState<string | null>(null);

  const movies = useMemo(() => {
    const seen = new Set<string>();
    return (query.data?.pages.flatMap((page) => page.items) ?? []).filter((m) => !seen.has(m.id) && seen.add(m.id));
  }, [query.data]);
  const focused = movies.find((m) => m.id === focusedId);

  function onCardFocus(index: number, id: string) {
    setFocusedId(id);
    if (index >= movies.length - LOAD_MORE_WITHIN && query.hasNextPage && !query.isFetchingNextPage) {
      void query.fetchNextPage();
    }
  }

  return (
    <section aria-label={row.title} className="mb-6">
      <h2 className="safe-x mb-2 font-display text-2xl font-bold">{row.title}</h2>
      {query.isError ? (
        <ApiErrorPanel error={query.error} onRetry={() => void query.refetch()} />
      ) : query.isPending ? (
        <div className="safe-x flex gap-4 py-6" aria-busy="true" aria-label={`Loading ${row.title}`}>
          {Array.from({ length: 9 }, (_, i) => (
            <Skeleton key={i} className="aspect-[2/3] w-[7.8rem] shrink-0" />
          ))}
        </div>
      ) : movies.length === 0 ? (
        <p className="safe-x py-6 text-muted">No films to show yet.</p>
      ) : (
        <div role="list" className="safe-x flex gap-4 overflow-x-auto py-6 [scroll-padding-inline:5vw] [scrollbar-width:thin]">
          {movies.map((movie, i) => (
            <div role="listitem" key={movie.id} className="shrink-0">
              <MovieCard
                movie={movie}
                ref={(el) => register(rowIndex, i, el)}
                onSelect={onSelect}
                onFocus={() => onCardFocus(i, movie.id)}
                onHover={() => focusAt(rowIndex, i)}
              />
            </div>
          ))}
        </div>
      )}
      {/* The focused film's details sit under the row, so they never need a request of their own. */}
      <p aria-hidden="true" className="safe-x h-6 text-base text-muted">
        {focused && (
          <>
            <span className="font-semibold text-white">{focused.title}</span> {focused.yearOfRelease}
            {focused.genres.length > 0 && <> · {focused.genres.slice(0, 3).join(", ")}</>}
            {focused.rating != null && (
              <>
                {" "}
                <Star className="mb-0.5 inline size-4 fill-star text-star" /> {focused.rating.toFixed(1)}
              </>
            )}
          </>
        )}
      </p>
    </section>
  );
}
