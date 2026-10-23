import { Search } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { ApiError } from "@/api/client";
import { useMoviePage } from "@/api/queries";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { useFocusNav } from "@/focus/FocusProvider";
import { useDebounced } from "@/hooks/useDebounced";
import { MovieCard } from "@/home/MovieCard";
import { ApiErrorPanel } from "@/shell/ApiErrorPanel";

const SORTS = [
  { value: "title", label: "Title, A to Z" },
  { value: "-title", label: "Title, Z to A" },
  { value: "-yearofrelease", label: "Year, newest first" },
  { value: "yearofrelease", label: "Year, oldest first" },
];
const DEBOUNCE_MS = 300;
const CURRENT_YEAR = new Date().getFullYear(); // the list shows this year's films until the user searches

function setOrDelete(params: URLSearchParams, key: string, value: string | null) {
  if (value) params.set(key, value);
  else params.delete(key);
}

// D5: the URL is the state: /browse?title=&year=&sortBy=&page=. Rows of the focus grid: filters 0, posters 1 to n, pager last.
export function BrowsePage() {
  const [params, setParams] = useSearchParams();
  const navigate = useNavigate();
  const { register, focusAt } = useFocusNav();
  const urlTitle = params.get("title") ?? "";
  const urlYear = params.get("year") ?? "";
  const sortBy = params.get("sortBy") ?? "title";
  const page = Math.max(1, Number(params.get("page")) || 1);

  const [titleInput, setTitleInput] = useState(urlTitle);
  const [yearInput, setYearInput] = useState(urlYear);
  const title = useDebounced(titleInput, DEBOUNCE_MS);
  const year = useDebounced(yearInput, DEBOUNCE_MS);

  // Back and forward change the URL, so the boxes follow it (state adjusted while rendering, not in an effect).
  const [seenUrl, setSeenUrl] = useState({ title: urlTitle, year: urlYear });
  if (seenUrl.title !== urlTitle || seenUrl.year !== urlYear) {
    setSeenUrl({ title: urlTitle, year: urlYear });
    setTitleInput(urlTitle);
    setYearInput(urlYear);
  }
  // What the URL last held. The debounced values lag the boxes by 300 ms, so after Clear or Back they still hold
  // the old text; comparing against the URL would write it straight back.
  const lastWritten = useRef({ title: urlTitle, year: urlYear });

  // Typing writes the URL once the boxes settle. Any filter change goes back to page 1.
  useEffect(() => {
    if (title === lastWritten.current.title && year === lastWritten.current.year) return;
    lastWritten.current = { title, year };
    setParams(
      (prev) => {
        // Back or Clear already put these values in the URL: leave it, and the page number, alone.
        if ((prev.get("title") ?? "") === title && (prev.get("year") ?? "") === year) return prev;
        const next = new URLSearchParams(prev);
        setOrDelete(next, "title", title);
        setOrDelete(next, "year", year);
        next.delete("page");
        return next;
      },
      { replace: true },
    );
  }, [title, year, setParams]);

  function update(key: string, value: string | null) {
    setParams((prev) => {
      const next = new URLSearchParams(prev);
      setOrDelete(next, key, value);
      if (key !== "page") next.delete("page");
      return next;
    });
  }

  const showingLatest = urlTitle === "" && urlYear === ""; // nothing searched: show the current year's films
  const yearNumber = showingLatest ? CURRENT_YEAR : urlYear && /^\d+$/.test(urlYear) ? Number(urlYear) : undefined;
  const query = useMoviePage({ title: urlTitle || undefined, year: yearNumber, sortBy, page });
  const movies = query.data?.items ?? [];

  const grid = useRef<HTMLUListElement>(null);
  const [columns, setColumns] = useState(7);
  useEffect(() => {
    const el = grid.current;
    if (!el) return;
    const measure = () => setColumns(Math.max(1, getComputedStyle(el).gridTemplateColumns.split(" ").length));
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(el);
    return () => observer.disconnect();
  }, [movies.length]);

  const yearError = query.error instanceof ApiError && query.error.kind === "validation" ? query.error.fieldErrors.year ?? query.error.message : null;
  const showGridError = query.isError && !yearError;
  const lastRow = Math.ceil(movies.length / columns) + 1;
  const hasFilters = urlTitle !== "" || urlYear !== "" || params.has("sortBy");

  return (
    <main className="safe-x pb-12 pt-24">
      <div className="flex flex-wrap items-end gap-6">
        <div className="min-w-64 flex-1">
          <Label htmlFor="browse-title" className="mb-2 text-xs tracking-widest text-muted">TITLE</Label>
          <div className="relative">
            <Search className="absolute left-3 top-1/2 size-5 -translate-y-1/2 text-muted" aria-hidden="true" />
            <Input
              id="browse-title"
              ref={(el) => register(0, 0, el)}
              onMouseEnter={() => focusAt(0, 0)}
              value={titleInput}
              onChange={(e) => setTitleInput(e.target.value)}
              placeholder="Search titles"
              className="h-12 bg-black pl-10 text-base"
            />
          </div>
        </div>
        <div className="w-40">
          <Label htmlFor="browse-year" className="mb-2 text-xs tracking-widest text-muted">YEAR</Label>
          <Input
            id="browse-year"
            ref={(el) => register(0, 1, el)}
            inputMode="numeric"
            value={yearInput}
            onChange={(e) => setYearInput(e.target.value.replace(/\D/g, "").slice(0, 4))}
            placeholder="Any"
            aria-invalid={yearError ? true : undefined}
            aria-describedby={yearError ? "year-error" : undefined}
            className="h-12 bg-black text-base"
          />
        </div>
        <div className="w-56">
          <Label htmlFor="browse-sort" className="mb-2 text-xs tracking-widest text-muted">SORT</Label>
          <select
            id="browse-sort"
            ref={(el) => register(0, 2, el)}
            value={sortBy}
            onChange={(e) => update("sortBy", e.target.value === "title" ? null : e.target.value)}
            className="focus-ring h-12 w-full rounded-md border border-input bg-black px-3 text-base"
          >
            {SORTS.map((s) => (
              <option key={s.value} value={s.value}>{s.label}</option>
            ))}
          </select>
        </div>
        {hasFilters && (
          <Button type="button" variant="link" ref={(el) => register(0, 3, el)} onClick={() => navigate("/browse")} className="text-base text-muted underline">
            Clear
          </Button>
        )}
      </div>
      {yearError && <p id="year-error" role="alert" className="mt-2 text-sm text-red-400">{yearError}</p>}

      <p className="mt-6 text-sm text-muted" aria-live="polite">
        {query.data
          ? `${query.data.total.toLocaleString()} ${showingLatest ? `latest films from ${CURRENT_YEAR}` : "films"}`
          : "\u00a0"}
      </p>

      {query.isPending ? (
        <ul className="mt-4 grid grid-cols-[repeat(auto-fill,minmax(10rem,1fr))] gap-4" aria-busy="true" aria-label="Loading films">
          {Array.from({ length: 14 }, (_, i) => (
            <li key={i}><Skeleton className="aspect-[2/3] w-full" /></li>
          ))}
        </ul>
      ) : showGridError ? (
        <ApiErrorPanel error={query.error} onRetry={() => void query.refetch()} />
      ) : yearError ? null : movies.length === 0 ? (
        <p className="py-16 text-center text-lg text-muted">No films match these filters.</p>
      ) : (
        <ul ref={grid} className="mt-4 grid grid-cols-[repeat(auto-fill,minmax(10rem,1fr))] gap-6 py-4">
          {movies.map((movie, i) => (
            <li key={movie.id}>
              <MovieCard
                fluid
                movie={movie}
                ref={(el) => register(1 + Math.floor(i / columns), i % columns, el)}
                onSelect={(slug) => navigate(`/movies/${slug}`)}
                onHover={() => focusAt(1 + Math.floor(i / columns), i % columns)}
              />
            </li>
          ))}
        </ul>
      )}

      <nav aria-label="Pages" className="mt-8 flex items-center justify-center gap-4">
        <Button variant="outline" size="lg" disabled={page <= 1} ref={(el) => register(lastRow, 0, el)} onClick={() => update("page", String(page - 1))}>
          Previous
        </Button>
        <span aria-live="polite">Page {page}</span>
        <Button variant="outline" size="lg" disabled={!query.data?.hasNextPage} ref={(el) => register(lastRow, 1, el)} onClick={() => update("page", String(page + 1))}>
          Next
        </Button>
      </nav>
    </main>
  );
}
