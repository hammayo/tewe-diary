import { useQuery, useQueryClient } from "@tanstack/react-query";
import { fetchMovie, fetchMovies } from "@/api/queries";
import type { MovieResponse, TrailerResponse } from "@/api/types";
import { heroSlugs } from "./heroSlugs";

const MAX_SLIDES = 5;
const DETAIL_STALE_MS = 60_000; // the API caches a movie for 60 s

export interface HeroSlide {
  slug: string;
  title: string;
  yearOfRelease: number;
  runtimeMinutes: number | null;
  genres: string[];
  tagline: string | null;
  backdropUrl: string | null;
  trailer: TrailerResponse | null;
}

function toSlide(movie: MovieResponse): HeroSlide | null {
  const details = movie.details;
  if (!details || (!details.backdropUrl && !details.trailer)) return null; // nothing to show: skip it
  return {
    slug: movie.slug,
    title: movie.title,
    yearOfRelease: movie.yearOfRelease,
    runtimeMinutes: details.runtimeMinutes,
    genres: movie.genres,
    tagline: details.tagline,
    backdropUrl: details.backdropUrl,
    trailer: details.trailer,
  };
}

/** Hand-picked slides first, topped up with the newest releases (D11). One detail request per slide. */
export function useHeroSlides() {
  const client = useQueryClient();
  const detail = (slug: string) =>
    client
      .fetchQuery({ queryKey: ["movie", slug], queryFn: () => fetchMovie(slug), staleTime: DETAIL_STALE_MS })
      .catch(() => null); // a slug missing from the data is skipped, not fatal

  return useQuery({
    queryKey: ["hero-slides"],
    queryFn: async (): Promise<HeroSlide[]> => {
      const slides = (await Promise.all(heroSlugs.slice(0, MAX_SLIDES).map(detail)))
        .map((movie) => (movie ? toSlide(movie) : null))
        .filter((slide): slide is HeroSlide => slide !== null);

      if (slides.length < MAX_SLIDES) {
        const newest = await fetchMovies({ sortBy: "-yearofrelease", pageSize: 10 });
        for (const candidate of newest.items) {
          if (slides.length >= MAX_SLIDES) break;
          if (slides.some((s) => s.slug === candidate.slug)) continue;
          const full = await detail(candidate.slug);
          const slide = full ? toSlide(full) : null;
          if (slide) slides.push(slide);
        }
      }
      return slides;
    },
  });
}
