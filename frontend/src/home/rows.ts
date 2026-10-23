import type { ListParams } from "@/api/types";

// D12: rows are a registry. Rows that need a richer v2 are registered but switched off, and name the
// API piece they wait for (see _docs/prds/_03-v2-browse-api-prd.md, section 9). Nothing probes the API.
export interface RowDefinition {
  id: string;
  title: string;
  query: Pick<ListParams, "sortBy"> & { genre?: string };
  requires?: "B3" | "B5" | "B9" | "B11";
  enabled: boolean;
}

export const rows: RowDefinition[] = [
  // The API sorts by one key only, so ties (same year) come back in no fixed order. MovieRow drops repeats across pages.
  { id: "newest", title: "Newest", query: { sortBy: "-yearofrelease" }, enabled: true },
  { id: "a-z", title: "A to Z", query: { sortBy: "title" }, enabled: true },
  { id: "top-rated", title: "Top rated", query: { sortBy: "-rating" }, requires: "B9", enabled: false },
  { id: "popular", title: "Popular", query: { sortBy: "-popular" }, requires: "B9", enabled: false },
  { id: "genre", title: "Genres", query: {}, requires: "B5", enabled: false },
  { id: "watchlist", title: "My Watch List", query: {}, requires: "B11", enabled: false },
];

export const enabledRows = rows.filter((row) => row.enabled);
