import { useEffect, useRef, useState } from "react";
import type { TrailerResponse } from "@/api/types";
import { trailerSrc } from "@/movie/trailerSrc";

const MAX_PLAY_MS = 90_000; // ponytail: a trailer that never reports its end still moves on; tune if trailers get longer

/**
 * The muted trailer behind the hero. The iframe is added only once the browser is idle (D8), and `onEnded`
 * fires when the video reports it has finished (YouTube and Vimeo both answer postMessage).
 */
export function HeroTrailer({ trailer, onEnded }: { trailer: TrailerResponse; onEnded: () => void }) {
  const frame = useRef<HTMLIFrameElement>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    const idle = window.requestIdleCallback ?? ((cb: () => void) => window.setTimeout(cb, 300));
    const cancel = window.cancelIdleCallback ?? window.clearTimeout;
    const handle = idle(() => setReady(true));
    return () => cancel(handle);
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(onEnded, MAX_PLAY_MS);
    function onMessage(event: MessageEvent) {
      if (event.source !== frame.current?.contentWindow) return;
      try {
        const data = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
        const ended = trailer.site === "Vimeo" ? data?.event === "finish" : data?.event === "onStateChange" && data?.info === 0;
        if (ended) onEnded();
      } catch {
        // not a message about the player
      }
    }
    window.addEventListener("message", onMessage);
    return () => {
      window.clearTimeout(timer);
      window.removeEventListener("message", onMessage);
    };
  }, [trailer.site, onEnded]);

  function subscribe() {
    const target = frame.current?.contentWindow;
    if (!target) return;
    if (trailer.site === "Vimeo") target.postMessage(JSON.stringify({ method: "addEventListener", value: "finish" }), "*");
    else target.postMessage(JSON.stringify({ event: "listening", id: 1 }), "*");
  }

  if (!ready) return null;
  return (
    <iframe
      ref={frame}
      title=""
      aria-hidden="true"
      tabIndex={-1}
      src={trailerSrc(trailer, { background: true })}
      allow="autoplay; encrypted-media"
      onLoad={subscribe}
      className="pointer-events-none absolute inset-0 size-full scale-[1.35] border-0"
    />
  );
}
