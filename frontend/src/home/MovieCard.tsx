import { forwardRef } from "react";
import type { MovieResponse } from "@/api/types";
import { cn } from "@/lib/utils";

interface Props {
  movie: MovieResponse;
  onSelect: (slug: string) => void;
  onFocus?: () => void;
  onHover?: () => void;
  /** Fill the grid cell instead of the fixed row width, with the title and year over the poster. */
  fluid?: boolean;
}

// Focus ring and scale come from :focus, so keyboard focus and mouse hover (which moves focus, D10) look the same.
export const MovieCard = forwardRef<HTMLButtonElement, Props>(function MovieCard({ movie, onSelect, onFocus, onHover, fluid = false }, ref) {
  return (
    <button
      ref={ref}
      type="button"
      aria-label={`${movie.title}, ${movie.yearOfRelease}`}
      onClick={() => onSelect(movie.slug)}
      onFocus={onFocus}
      onMouseEnter={onHover}
      className={cn(
        "relative aspect-[2/3] overflow-hidden rounded-md bg-surface outline-none transition duration-200 ease-out focus:z-10 focus:scale-110 focus:ring-4 focus:ring-white focus:ring-offset-4 focus:ring-offset-ink motion-reduce:transition-none",
        fluid ? "w-full" : "w-[7.8rem] shrink-0",
      )}
    >
      {movie.posterUrl ? (
        <>
          <img src={movie.posterUrl} alt="" loading="lazy" className="size-full object-cover" />
          {fluid && (
            <span className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/90 to-transparent p-3 pt-10 text-left">
              <span className="block font-display text-sm font-bold leading-tight">{movie.title}</span>
              <span className="text-xs text-muted">{movie.yearOfRelease}</span>
            </span>
          )}
        </>
      ) : (
        <span className="flex size-full flex-col justify-end gap-1 rounded-md border-2 border-dashed border-subtle/60 p-3 text-left">
          <span className="font-display text-sm font-bold leading-tight">{movie.title}</span>
          <span className="text-xs text-muted">{movie.yearOfRelease}</span>
        </span>
      )}
    </button>
  );
});
