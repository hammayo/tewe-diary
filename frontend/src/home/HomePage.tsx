import { useState } from "react";
import { MovieModal } from "@/movie/MovieModal";
import { HeroBanner } from "./HeroBanner";
import { MovieRow } from "./MovieRow";
import { enabledRows } from "./rows";
import { useHeroSlides } from "./useHeroSlides";

// Row 0 of the focus grid is the hero's buttons; the poster rows start at 1.
export function HomePage() {
  const hero = useHeroSlides();
  const slides = hero.data ?? [];
  const [openSlug, setOpenSlug] = useState<string | null>(null);
  return (
    <main>
      {slides.length > 0 ? <HeroBanner slides={slides} /> : <div className="h-28" />}
      {enabledRows.map((row, i) => (
        <MovieRow key={row.id} row={row} rowIndex={i + 1} onSelect={setOpenSlug} />
      ))}
      {openSlug && <MovieModal slug={openSlug} onClose={() => setOpenSlug(null)} />}
    </main>
  );
}
