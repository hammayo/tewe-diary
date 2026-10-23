import type { TrailerResponse } from "@/api/types";

/** Embed URL for a trailer. Autoplay is always muted (D8); the full-screen player starts with sound on a user action. */
export function trailerSrc(trailer: TrailerResponse, { background }: { background: boolean }): string {
  const url = new URL(trailer.embedUrl);
  const set = (entries: Record<string, string>) => Object.entries(entries).forEach(([k, v]) => url.searchParams.set(k, v));

  if (trailer.site === "Vimeo") {
    set(background ? { autoplay: "1", muted: "1", background: "1", api: "1" } : { autoplay: "1" });
  } else {
    set(
      background
        ? { autoplay: "1", mute: "1", controls: "0", playsinline: "1", rel: "0", enablejsapi: "1", origin: window.location.origin }
        : { autoplay: "1", playsinline: "1", rel: "0" },
    );
  }
  return url.toString();
}
