import { Info, Pause, Play } from "lucide-react";
import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { useFocusNav } from "@/focus/FocusProvider";
import { useAllowMotion } from "@/hooks/useAllowMotion";
import { useHeroVisible } from "@/hooks/useHeroVisible";
import { cn } from "@/lib/utils";
import { TrailerPlayer } from "@/movie/TrailerPlayer";
import { HeroTrailer } from "./HeroTrailer";
import type { HeroSlide } from "./useHeroSlides";

const NO_TRAILER_MS = 20_000; // FR-5: how long a slide without a trailer stays

function runtimeText(minutes: number | null): string | null {
  return minutes ? `${Math.floor(minutes / 60)}h ${String(minutes % 60).padStart(2, "0")}m` : null;
}

// Row 0 of the focus grid: Play, More info, pause. Focus starts on Play (FR-5).
export function HeroBanner({ slides }: { slides: HeroSlide[] }) {
  const { register, focusAt } = useFocusNav();
  const section = useRef<HTMLElement>(null);
  const playButton = useRef<HTMLButtonElement | null>(null);
  const [index, setIndex] = useState(0);
  const [paused, setPaused] = useState(false);
  const [playerOpen, setPlayerOpen] = useState(false);
  const allowMotion = useAllowMotion();
  const visible = useHeroVisible(section);

  const slide = slides[index % slides.length]; // the list can shrink after a refetch
  const next = useCallback(() => setIndex((i) => (i + 1) % slides.length), [slides.length]);
  const playTrailer = allowMotion && visible && !paused && !playerOpen && slide.trailer !== null;
  const advancing = allowMotion && !paused && !playerOpen && slides.length > 1;

  useEffect(() => playButton.current?.focus({ preventScroll: true }), []);

  // A slide without a playing trailer moves on after 20 s (FR-5). A playing trailer reports its own end.
  useEffect(() => {
    if (!advancing || playTrailer) return;
    const timer = window.setTimeout(next, NO_TRAILER_MS);
    return () => window.clearTimeout(timer);
  }, [advancing, playTrailer, index, next]);

  const runtime = runtimeText(slide.runtimeMinutes);
  return (
    <section ref={section} aria-roledescription="carousel" aria-label="Featured" className="relative h-[70vh] min-h-[28rem] overflow-hidden">
      {slide.backdropUrl && <img src={slide.backdropUrl} alt="" className="absolute inset-0 size-full object-cover" />}
      {playTrailer && slide.trailer && <HeroTrailer key={slide.slug} trailer={slide.trailer} onEnded={next} />}
      <div className="absolute inset-0 bg-gradient-to-r from-ink via-ink/70 to-transparent" />
      <div className="absolute inset-x-0 bottom-0 h-1/3 bg-gradient-to-t from-ink to-transparent" />

      <div className="safe-x relative flex h-full max-w-[40rem] flex-col justify-center gap-4 pt-16">
        <p className="text-sm font-semibold tracking-[0.2em] text-muted">
          FEATURED · {index + 1} OF {slides.length}
        </p>
        <h1 className="font-display text-7xl font-extrabold leading-none tracking-tight">{slide.title}</h1>
        <p className="flex flex-wrap gap-x-3 text-lg text-white/90">
          <span>{slide.yearOfRelease}</span>
          {runtime && <span>· {runtime}</span>}
          <span>· {slide.genres.join(", ")}</span>
        </p>
        {slide.tagline && <p className="text-xl italic text-white/90">{slide.tagline}</p>}
        <div className="mt-2 flex items-center gap-3">
          {slide.trailer && (
            <button
              type="button"
              ref={(el) => {
                playButton.current = el;
                register(0, 0, el);
              }}
              onMouseEnter={() => focusAt(0, 0)}
              onClick={() => setPlayerOpen(true)}
              className="btn-primary"
            >
              <Play className="size-5 fill-current" aria-hidden="true" /> Play
            </button>
          )}
          <Link
            to={`/movies/${slide.slug}`}
            ref={(el) => register(0, 1, el)}
            onMouseEnter={() => focusAt(0, 1)}
            className="btn-secondary"
          >
            <Info className="size-5" aria-hidden="true" /> More info
          </Link>
          <button
            type="button"
            ref={(el) => register(0, 2, el)}
            onMouseEnter={() => focusAt(0, 2)}
            aria-pressed={paused}
            aria-label={paused ? "Resume hero motion" : "Pause hero motion"}
            onClick={() => setPaused((p) => !p)}
            className="focus-ring grid size-10 place-items-center rounded-full border-2 border-white/70"
          >
            {paused ? <Play className="size-4 fill-current" aria-hidden="true" /> : <Pause className="size-4 fill-current" aria-hidden="true" />}
          </button>
        </div>
      </div>

      <div className="safe-x absolute inset-x-0 bottom-8 flex justify-end gap-2" aria-hidden="true">
        {slides.map((s, i) => (
          <span key={s.slug} className={cn("h-1 w-10 rounded-full", i === index ? "bg-white" : "bg-white/30")} />
        ))}
      </div>

      {slide.trailer && <TrailerPlayer trailer={slide.trailer} title={slide.title} open={playerOpen} onClose={() => setPlayerOpen(false)} returnFocusTo={playButton} />}
    </section>
  );
}
