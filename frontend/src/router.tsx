import { createBrowserRouter } from "react-router";
import { HomePage } from "@/home/HomePage";
import { ProfilesPage } from "@/profiles/ProfilesPage";
import { Layout } from "@/shell/Layout";
import { NotFoundPage } from "@/shell/NotFoundPage";

// Screens other than Home and Who's watching are code-split (NFR-3).
export const router = createBrowserRouter([
  { path: "/profiles", element: <ProfilesPage /> },
  {
    element: <Layout />,
    hydrateFallbackElement: <div aria-busy="true" />, // lazy routes can load before first render
    children: [
      { path: "/", element: <HomePage /> },
      { path: "/movies/:slug", lazy: async () => ({ Component: (await import("@/movie/MoviePage")).MoviePage }) },
      { path: "/browse", lazy: async () => ({ Component: (await import("@/browse/BrowsePage")).BrowsePage }) },
      { path: "/ratings", lazy: async () => ({ Component: (await import("@/ratings/RatingsPage")).RatingsPage }) },
      { path: "/manage", lazy: async () => ({ Component: (await import("@/manage/ManagePage")).ManagePage }) },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
]);
