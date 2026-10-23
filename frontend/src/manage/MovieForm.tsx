import { X } from "lucide-react";
import { useState, type FormEvent, type KeyboardEvent } from "react";
import { ApiError } from "@/api/client";
import { useCreateMovie, useUpdateMovie } from "@/api/queries";
import type { MovieResponse } from "@/api/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

interface Props {
  /** Present when editing. */
  movie?: MovieResponse;
  onSaved: (slug: string) => void;
  onCancel: () => void;
}

type FieldKey = "title" | "yearofrelease" | "genres";

function FieldError({ id, message }: { id: string; message?: string }) {
  return message ? (
    <p id={`${id}-error`} className="mt-1 text-sm text-red-400">
      {message}
    </p>
  ) : null;
}

// D13: a plain form outside the focus navigation. Errors from the API's 400 sit next to their field.
export function MovieForm({ movie, onSaved, onCancel }: Props) {
  const create = useCreateMovie();
  const update = useUpdateMovie(movie?.id ?? "");
  const save = movie ? update : create;

  const [title, setTitle] = useState(movie?.title ?? "");
  const [year, setYear] = useState(movie ? String(movie.yearOfRelease) : "");
  const [genres, setGenres] = useState<string[]>(movie?.genres ?? []);
  const [genreInput, setGenreInput] = useState("");
  const [localYearError, setLocalYearError] = useState<string | null>(null);

  const error = save.error instanceof ApiError ? save.error : null;
  const fieldErrors: Record<string, string> = { ...(error?.kind === "validation" ? error.fieldErrors : {}) };
  if (localYearError) fieldErrors.yearofrelease = localYearError;
  const errorCount = Object.keys(fieldErrors).length;

  function addGenre() {
    const value = genreInput.trim();
    if (value && !genres.includes(value)) setGenres([...genres, value]);
    setGenreInput("");
  }

  function onGenreKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter" || event.key === ",") {
      event.preventDefault();
      addGenre();
    } else if (event.key === "Backspace" && genreInput === "" && genres.length > 0) {
      setGenres(genres.slice(0, -1));
    }
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!/^\d{4}$/.test(year)) {
      setLocalYearError("Enter a four-digit year.");
      return;
    }
    setLocalYearError(null);
    const pending = genreInput.trim();
    const allGenres = pending && !genres.includes(pending) ? [...genres, pending] : genres;
    save.mutate({ title, yearOfRelease: Number(year), genres: allGenres }, { onSuccess: (saved) => onSaved(saved.slug) });
  }

  const describedBy = (key: FieldKey, id: string) => (fieldErrors[key] ? `${id}-error` : undefined);

  return (
    <form onSubmit={onSubmit} noValidate className="space-y-5 rounded-lg bg-surface p-6">
      <h2 className="font-display text-2xl font-bold">{movie ? "Edit film" : "New film"}</h2>
      {errorCount > 0 && (
        <p role="alert" className="rounded-md border border-red-500/40 bg-red-950/40 p-3 text-red-300">
          Fix {errorCount} {errorCount === 1 ? "field" : "fields"} before saving.
        </p>
      )}
      {error?.kind === "forbidden" && (
        <p role="alert" className="rounded-md border border-red-500/40 bg-red-950/40 p-3 text-red-300">
          This profile is not allowed to save films.
        </p>
      )}
      {error && error.kind !== "validation" && error.kind !== "forbidden" && (
        <p role="alert" className="rounded-md border border-red-500/40 bg-red-950/40 p-3 text-red-300">
          {error.kind === "unauthorized" ? "Your session ended. Choose a profile to continue." : error.message}
        </p>
      )}

      <div>
        <Label htmlFor="movie-title">Title</Label>
        <Input
          id="movie-title"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          aria-invalid={fieldErrors.title ? true : undefined}
          aria-describedby={describedBy("title", "movie-title")}
          className="mt-1 h-11 bg-black"
        />
        <FieldError id="movie-title" message={fieldErrors.title} />
      </div>
      <div>
        <Label htmlFor="movie-year">Year of release</Label>
        <Input
          id="movie-year"
          inputMode="numeric"
          value={year}
          onChange={(e) => setYear(e.target.value.replace(/\D/g, "").slice(0, 4))}
          aria-invalid={fieldErrors.yearofrelease ? true : undefined}
          aria-describedby={describedBy("yearofrelease", "movie-year")}
          className="mt-1 h-11 w-40 bg-black"
        />
        <FieldError id="movie-year" message={fieldErrors.yearofrelease} />
      </div>
      <div>
        <Label htmlFor="movie-genres">Genres</Label>
        <div className="mt-1 flex flex-wrap items-center gap-2 rounded-md border border-input bg-black p-2">
          {genres.map((genre) => (
            <span key={genre} className="flex items-center gap-1 rounded-full bg-surface-raised py-1 pl-3 pr-1 text-sm">
              {genre}
              <button
                type="button"
                aria-label={`Remove ${genre}`}
                onClick={() => setGenres(genres.filter((g) => g !== genre))}
                className="focus-ring grid size-6 place-items-center rounded-full hover:bg-white/10"
              >
                <X className="size-3.5" aria-hidden="true" />
              </button>
            </span>
          ))}
          <input
            id="movie-genres"
            value={genreInput}
            onChange={(e) => setGenreInput(e.target.value)}
            onKeyDown={onGenreKeyDown}
            onBlur={addGenre}
            placeholder="Add genre"
            aria-describedby={describedBy("genres", "movie-genres")}
            className="min-w-32 flex-1 bg-transparent px-1 py-1 text-base outline-none"
          />
        </div>
        <FieldError id="movie-genres" message={fieldErrors.genres} />
      </div>
      <p className="text-sm text-muted">
        The slug is built from title and year. Saving a change to either opens the film at its new address and refreshes the cached rows.
      </p>
      <div className="flex gap-3">
        <Button type="submit" size="lg" disabled={save.isPending} className="bg-white text-ink hover:bg-white/85">
          Save
        </Button>
        <Button type="button" size="lg" variant="outline" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  );
}
