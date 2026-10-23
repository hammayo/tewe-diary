import { Star } from "lucide-react";
import { Link, useNavigate } from "react-router";
import { useQueries } from "@tanstack/react-query";
import { fetchMovie, useMyRatings } from "@/api/queries";
import { useAuth } from "@/auth/AuthProvider";
import { Skeleton } from "@/components/ui/skeleton";
import { useFocusNav } from "@/focus/FocusProvider";
import { MovieCard } from "@/home/MovieCard";
import { ApiErrorPanel } from "@/shell/ApiErrorPanel";

function Stars({ value }: { value: number }) {
  return (
    <span role="img" aria-label={`Rated ${value} of 5`} className="flex">
      {[1, 2, 3, 4, 5].map((n) => (
        <Star key={n} className={n <= value ? "size-4 fill-star text-star" : "size-4 text-star"} aria-hidden="true" />
      ))}
    </span>
  );
}

// G6: the API gives movie ids and ratings only, so each film is fetched by slug (cached reads).
export function RatingsPage() {
  const { claims } = useAuth();
  const navigate = useNavigate();
  const { register, focusAt } = useFocusNav();
  const ratings = useMyRatings(claims.signedIn);
  const movies = useQueries({
    queries: (ratings.data ?? []).map((r) => ({ queryKey: ["movie", r.slug], queryFn: () => fetchMovie(r.slug) })),
  });

  if (!claims.signedIn) {
    return (
      <main className="safe-x pt-32 text-center">
        <h1 className="font-display text-5xl font-extrabold">My ratings</h1>
        <p className="mt-4 text-muted">Ratings belong to a profile.</p>
        <Link to="/profiles" className="btn-primary mt-8">Choose a profile</Link>
      </main>
    );
  }

  return (
    <main className="safe-x pb-12 pt-24">
      <h1 className="flex items-baseline gap-4 font-display text-6xl font-extrabold tracking-tight">
        My ratings
        {ratings.data && <span className="text-lg font-normal text-muted">{ratings.data.length} {ratings.data.length === 1 ? "film" : "films"} rated</span>}
      </h1>
      {ratings.isPending ? (
        <ul className="mt-8 grid grid-cols-[repeat(auto-fill,minmax(10rem,1fr))] gap-6" aria-busy="true" aria-label="Loading ratings">
          {Array.from({ length: 8 }, (_, i) => (
            <li key={i}><Skeleton className="aspect-[2/3] w-full" /></li>
          ))}
        </ul>
      ) : ratings.isError ? (
        <ApiErrorPanel error={ratings.error} onRetry={() => void ratings.refetch()} />
      ) : ratings.data.length === 0 ? (
        <p className="py-16 text-center text-lg text-muted">
          You have not rated any films yet. Open a film and pick a star rating.
        </p>
      ) : (
        <ul className="mt-8 grid grid-cols-[repeat(auto-fill,minmax(10rem,1fr))] gap-6">
          {ratings.data.map((rating, i) => {
            const movie = movies[i]?.data;
            return (
              <li key={rating.movieId}>
                {movie ? (
                  <>
                    <MovieCard
                      fluid
                      movie={movie}
                      ref={(el) => register(i, 0, el)}
                      onSelect={(slug) => navigate(`/movies/${slug}`)}
                      onHover={() => focusAt(i, 0)}
                    />
                    <div className="mt-3 flex items-center justify-between text-sm text-muted">
                      <span>{movie.yearOfRelease}</span>
                      <Stars value={rating.rating} />
                    </div>
                  </>
                ) : (
                  <Skeleton className="aspect-[2/3] w-full" />
                )}
              </li>
            );
          })}
        </ul>
      )}
    </main>
  );
}
