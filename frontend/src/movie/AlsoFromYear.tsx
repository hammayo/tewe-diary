import { useMoviePage } from "@/api/queries";
import { useFocusNav } from "@/focus/FocusProvider";
import { MovieCard } from "@/home/MovieCard";

const SHOWN = 5;

/** Other films from the same year: `GET /movies?year=`. Absent while loading, on error or when there are none. */
export function AlsoFromYear({ slug, year, focusRow, onSelect }: { slug: string; year: number; focusRow?: number; onSelect: (slug: string) => void }) {
  const { register, focusAt } = useFocusNav();
  // The API sorts by title or year only, so take a full page and keep the films that have a poster.
  const query = useMoviePage({ year, sortBy: "title", pageSize: 25 });
  const others = (query.data?.items ?? []).filter((m) => m.slug !== slug && m.posterUrl).slice(0, SHOWN);
  if (others.length === 0) return null;

  return (
    <section aria-label={`Also from ${year}`}>
      <h2 className="mb-4 font-display text-2xl font-bold">Also from {year}</h2>
      <ul className="grid grid-cols-[repeat(auto-fill,minmax(8rem,10rem))] gap-6 py-2">
        {others.map((movie, i) => (
          <li key={movie.id}>
            <MovieCard
              fluid
              movie={movie}
              ref={focusRow === undefined ? undefined : (el) => register(focusRow, i, el)}
              onSelect={onSelect}
              onHover={focusRow === undefined ? undefined : () => focusAt(focusRow, i)}
            />
          </li>
        ))}
      </ul>
    </section>
  );
}
