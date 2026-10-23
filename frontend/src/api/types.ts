// Hand-written mirrors of the v2 contracts (PRD D6). Same shapes as v1.

export interface TrailerResponse {
  site: string; // "YouTube" or "Vimeo"
  key: string;
  name: string | null;
  url: string;
  embedUrl: string;
}

export interface Credit {
  tmdbPersonId: number;
  name: string;
  role: string | null;
  profileUrl: string | null;
}

export interface MovieDetails {
  tmdbId: number;
  imdbId: string | null;
  overview: string | null;
  tagline: string | null;
  runtimeMinutes: number | null;
  backdropUrl: string | null;
  trailer: TrailerResponse | null;
  directors: Credit[];
  writers: Credit[];
  cast: Credit[]; // up to 10
}

export interface MovieResponse {
  id: string;
  slug: string;
  title: string;
  yearOfRelease: number;
  genres: string[];
  rating: number | null; // average
  userRating: number | null; // caller only
  posterUrl: string | null;
  details: MovieDetails | null; // null on list items and on manual movies
  backdropUrl?: string; // phase 2, B1
  ratingCount?: number; // phase 2, B8
}

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
  hasNextPage: boolean;
}

export interface MovieRatingResponse {
  movieId: string;
  slug: string;
  rating: number;
}

export interface ValidationFailureResponse {
  errors: { propertyName: string; message: string }[];
}

export interface ListParams {
  title?: string;
  year?: number;
  sortBy?: string;
  page?: number;
  pageSize?: number;
}

export interface MovieInput {
  title: string;
  yearOfRelease: number;
  genres: string[];
}

export interface TokenRequest {
  userId: string;
  email: string;
  customClaims: Record<string, boolean>;
}
