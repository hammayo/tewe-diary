import { fileURLToPath, URL } from "node:url";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vitest/config";

// D1: same origin, no CORS. The APIs redirect plain HTTP to HTTPS unless told the request was HTTPS.
const moviesUpstream = process.env.MOVIES_UPSTREAM ?? "http://localhost:5001";
const identityUpstream = process.env.IDENTITY_UPSTREAM ?? "http://localhost:5003";
const forwardedHttps = { "X-Forwarded-Proto": "https" };

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
  server: {
    host: true,
    port: 5173,
    proxy: {
      "/api": { target: moviesUpstream, headers: forwardedHttps },
      "/identity": {
        target: identityUpstream,
        headers: forwardedHttps,
        rewrite: (path) => path.replace(/^\/identity/, ""),
      },
    },
  },
  test: { environment: "jsdom", setupFiles: ["./src/test/setup.ts"], globals: false },
});
