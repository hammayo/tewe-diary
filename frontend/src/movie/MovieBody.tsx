import { Pencil, Play, Trash2 } from "lucide-react";
import { useRef, useState } from "react";
import { Link } from "react-router";
import { useClearRating, useRateMovie } from "@/api/queries";
import type { Credit, MovieResponse } from "@/api/types";
import { useAuth } from "@/auth/AuthProvider";
import { useFocusNav } from "@/focus/FocusProvider";
import { CastList } from "./CastList";
import { RatingStars } from "./RatingStars";
import { TrailerPlayer } from "./TrailerPlayer";

interface Props {
  movie: MovieResponse;
  canEdit: boolean;
  canDelete: boolean;
  /** Focus-grid row to start registering at (the full page). Omit in the modal, where Radix owns focus. */
  focusBase?: number;
}

function names(credits: Credit[]): string {
  return credits.map((c) => (c.role ? `${c.name} (${c.role})` : c.name)).join(", ");
}

function runtimeText(minutes: number | null | undefined): string | null {
  return minutes ? `${Math.floor(minutes / 60)}h ${String(minutes % 60).padStart(2, "0")}m` : null;
}

/** The movie's content, shared by the page and the modal. A manual movie has `details: null` (FR-7). */
export function MovieBody({ movie, canEdit, canDelete, focusBase }: Props) {
  const { claims } = useAuth();
  const { register } = useFocusNav();
  const rate = useRateMovie(movie);
  const clear = useClearRating(movie);
  const playRef = useRef<HTMLButtonElement | null>(null);
  const [playerOpen, setPlayerOpen] = useState(false);
  const details = movie.details;
  const runtime = runtimeText(details?.runtimeMinutes);
  const writeError = rate.error ?? clear.error;

  return (
    <div className="flex flex-wrap gap-10">
      {movie.posterUrl ? (
        <img src={movie.posterUrl} alt={`Poster for ${movie.title}`} className="aspect-[2/3] w-56 rounded-lg bg-surface object-cover shadow-2xl" />
      ) : (
        <div className="flex aspect-[2/3] w-56 items-end rounded-lg bg-surface-raised p-4 font-display text-2xl font-bold">{movie.title}</div>
      )}

      <div className="min-w-0 flex-1 basis-[28rem] space-y-4">
        <h1 id="movie-title" className="font-display text-6xl font-extrabold leading-none tracking-tight">
          {movie.title}
        </h1>
        <p className="flex flex-wrap items-center gap-3 text-lg">
          <span>{movie.yearOfRelease}</span>
          {runtime && <span>· {runtime}</span>}
          {movie.genres.map((genre) => (
            <span key={genre} className="rounded-full border border-white/40 px-3 py-0.5 text-base">
              {genre}
            </span>
          ))}
        </p>
        {details?.tagline && <p className="text-xl italic text-white/90">{details.tagline}</p>}
        {details?.overview && <p className="max-w-4xl text-lg leading-relaxed text-white/85">{details.overview}</p>}
        {details && (details.directors.length > 0 || details.writers.length > 0) && (
          <p className="flex flex-wrap gap-x-6 text-base text-muted">
            {details.directors.length > 0 && (
              <span>
                <b className="font-semibold text-white">Director</b> {names(details.directors)}
              </span>
            )}
            {details.writers.length > 0 && (
              <span>
                <b className="font-semibold text-white">Writers</b> {names(details.writers)}
              </span>
            )}
          </p>
        )}

        <div className="flex flex-wrap items-end gap-8 pt-2">
          {details?.trailer && (
            <button
              type="button"
              ref={(el) => {
                playRef.current = el;
                if (focusBase !== undefined) register(focusBase, 0, el);
              }}
              onClick={() => setPlayerOpen(true)}
              className="btn-primary"
            >
              <Play className="size-5 fill-current" aria-hidden="true" /> Play trailer
            </button>
          )}
          <div>
            <p className="mb-1 text-xs font-semibold tracking-widest text-muted">YOUR RATING</p>
            <RatingStars
              value={movie.userRating}
              disabled={!claims.signedIn}
              focusRow={focusBase === undefined ? undefined : focusBase + 1}
              onChange={(n) => rate.mutate(n)}
              onClear={() => clear.mutate()}
            />
          </div>
          <div>
            <p className="text-xs font-semibold tracking-widest text-muted">AVERAGE</p>
            <p className="text-3xl font-bold">
              {movie.rating !== null ? (
                <>
                  {movie.rating.toFixed(1)} <span className="text-lg font-normal text-muted">/ 5</span>
                </>
              ) : (
                <span className="text-lg font-normal text-muted">No ratings yet</span>
              )}
            </p>
          </div>
        </div>
        {writeError && (
          <p role="alert" className="text-sm text-red-400">
            {writeError.message}
          </p>
        )}
        {claims.signedIn && <p className="text-sm text-subtle">The average can trail by up to a minute.</p>}

        {(canEdit || canDelete) && (
          <div className="flex gap-3 pt-2">
            {canEdit && (
              <Link to={`/manage?edit=${movie.slug}`} className="btn-secondary">
                <Pencil className="size-4" aria-hidden="true" /> Edit
              </Link>
            )}
            {canDelete && (
              <Link to={`/manage?delete=${movie.slug}`} className="btn-secondary text-red-300">
                <Trash2 className="size-4" aria-hidden="true" /> Delete
              </Link>
            )}
          </div>
        )}
      </div>

      {details && (
        <div className="basis-full">
          <CastList cast={details.cast} />
        </div>
      )}
      {details?.trailer && (
        <TrailerPlayer trailer={details.trailer} title={movie.title} open={playerOpen} onClose={() => setPlayerOpen(false)} returnFocusTo={playRef} />
      )}
    </div>
  );
}
