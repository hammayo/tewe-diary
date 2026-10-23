import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { request } from "./client";
import type { ListParams, MovieInput, MovieRatingResponse, MovieResponse, PagedResponse } from "./types";

const DEFAULT_PAGE_SIZE = 25; // the API's maximum for the rows

function queryString(params: ListParams): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== "") search.set(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}

export function fetchMovies(params: ListParams): Promise<PagedResponse<MovieResponse>> {
  return request(`/movies${queryString(params)}`);
}

export function fetchMovie(idOrSlug: string): Promise<MovieResponse> {
  return request(`/movies/${encodeURIComponent(idOrSlug)}`);
}

export function useMovieList(params: ListParams) {
  return useInfiniteQuery({
    queryKey: ["movies", params],
    queryFn: ({ pageParam }) => fetchMovies({ pageSize: DEFAULT_PAGE_SIZE, ...params, page: pageParam }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.hasNextPage ? last.page + 1 : undefined),
  });
}

/** One page at a time, for Browse. The previous page stays on screen while the next one loads. */
export function useMoviePage(params: ListParams) {
  return useQuery({
    queryKey: ["movies", "page", params],
    queryFn: () => fetchMovies({ pageSize: DEFAULT_PAGE_SIZE, ...params }),
    placeholderData: keepPreviousData,
  });
}

export function useMovie(idOrSlug: string, enabled = true) {
  return useQuery({ queryKey: ["movie", idOrSlug], queryFn: () => fetchMovie(idOrSlug), enabled });
}

export function useMyRatings(enabled = true) {
  return useQuery({
    queryKey: ["ratings", "me"],
    queryFn: () => request<MovieRatingResponse[]>("/ratings/me"),
    enabled,
  });
}

/** Refreshes everything a write can change: the rows, the movie pages and my ratings. */
function useInvalidateAll() {
  const client = useQueryClient();
  return () =>
    Promise.all([
      client.invalidateQueries({ queryKey: ["movies"] }),
      client.invalidateQueries({ queryKey: ["movie"] }),
      client.invalidateQueries({ queryKey: ["ratings"] }),
    ]);
}

export function useRateMovie(movie: { id: string; slug: string }) {
  const client = useQueryClient();
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: (rating: number) =>
      request<void>(`/movies/${movie.id}/ratings`, { method: "PUT", body: JSON.stringify({ rating }) }),
    onSuccess: (_data, rating) => {
      // Show my own rating at once (G9); the average catches up when the API cache expires.
      client.setQueryData<MovieResponse>(["movie", movie.slug], (old) => old && { ...old, userRating: rating });
      return invalidate();
    },
  });
}

export function useClearRating(movie: { id: string; slug: string }) {
  const client = useQueryClient();
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: () => request<void>(`/movies/${movie.id}/ratings`, { method: "DELETE" }),
    onSuccess: () => {
      client.setQueryData<MovieResponse>(["movie", movie.slug], (old) => old && { ...old, userRating: null });
      return invalidate();
    },
  });
}

export function useCreateMovie() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: (input: MovieInput) => request<MovieResponse>("/movies", { method: "POST", body: JSON.stringify(input) }),
    onSuccess: invalidate,
  });
}

export function useUpdateMovie(id: string) {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: (input: MovieInput) =>
      request<MovieResponse>(`/movies/${id}`, { method: "PUT", body: JSON.stringify(input) }),
    onSuccess: invalidate,
  });
}

export function useDeleteMovie(id: string) {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: () => request<void>(`/movies/${id}`, { method: "DELETE" }),
    onSuccess: invalidate,
  });
}
