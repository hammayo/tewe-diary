import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router";
import { ApiError } from "@/api/client";
import { AuthProvider } from "@/auth/AuthProvider";
import { FocusProvider } from "@/focus/FocusProvider";
import "./index.css";
import { router } from "./router";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      // A 4xx will not change on a retry; a server or network blip gets one more go.
      retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 1,
    },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <FocusProvider>
          <RouterProvider router={router} />
        </FocusProvider>
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
);
