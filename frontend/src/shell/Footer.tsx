// FR-12: the TMDB attribution sits in the bottom safe margin of every screen.
export function Footer({ className = "" }: { className?: string }) {
  return (
    <footer className={`safe-x py-6 text-right text-sm text-subtle ${className}`}>
      This product uses the TMDB API but is not endorsed or certified by TMDB.
    </footer>
  );
}
