import { Eye } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useLocation, useNavigate } from "react-router";
import { ApiErrorPanel } from "@/shell/ApiErrorPanel";
import { lastChosenProfileId, useAuth } from "@/auth/AuthProvider";
import { profiles, type DemoProfile } from "@/auth/profiles";
import { useFocusNav } from "@/focus/FocusProvider";
import { cn } from "@/lib/utils";
import { Footer } from "@/shell/Footer";
import { LogoMark } from "@/shell/LogoMark";

interface Tile {
  key: string;
  label: string;
  blurb: string;
  profile: DemoProfile | null;
}

const tiles: Tile[] = [
  ...profiles.map((p) => ({ key: p.id, label: p.label, blurb: p.blurb, profile: p })),
  { key: "anonymous", label: "Anonymous", blurb: "Browse, search, trailers", profile: null },
];

export function ProfilesPage() {
  const { choose, signedOutNotice } = useAuth();
  const { register, focusAt } = useFocusNav();
  const navigate = useNavigate();
  const from = (useLocation().state as { from?: string } | null)?.from ?? "/";
  const refs = useRef<(HTMLButtonElement | null)[]>([]);
  const [focused, setFocused] = useState(0);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    const last = tiles.findIndex((t) => t.key === lastChosenProfileId());
    refs.current[Math.max(0, last)]?.focus();
  }, []);

  async function pick(tile: Tile) {
    setError(null);
    try {
      await choose(tile.profile);
      navigate(from, { replace: true });
    } catch (e) {
      setError(e);
    }
  }

  return (
    <div className="flex h-dvh flex-col overflow-hidden">
      <header className="safe-x py-8">
        <span className="flex items-center gap-2 font-display text-lg font-extrabold tracking-[0.14em]">
          <LogoMark className="size-6" /> TEWE DIARY
        </span>
      </header>
      <main className="safe-x flex min-h-0 flex-1 flex-col items-center justify-center gap-8 pb-16 text-center">
        <h1 className="font-display text-5xl font-extrabold tracking-tight">Who's watching?</h1>
        {signedOutNotice && <p role="status" className="text-muted">{signedOutNotice}</p>}
        <ul className="flex items-start gap-10">
          {tiles.map((tile, i) => (
            <li key={tile.key} className="w-44">
              <button
                type="button"
                ref={(el) => {
                  refs.current[i] = el;
                  register(0, i, el);
                }}
                onClick={() => pick(tile)}
                onFocus={() => setFocused(i)}
                onMouseEnter={() => focusAt(0, i)}
                className="focus-ring group flex w-full flex-col items-center gap-3 rounded-md"
              >
                <span
                  className={cn(
                    "grid aspect-square w-full place-items-center rounded-md transition-transform duration-200 motion-reduce:transition-none",
                    "group-focus-visible:scale-110 group-focus-visible:outline-4 group-focus-visible:outline-white group-focus-visible:outline-offset-4",
                    tile.profile ? tile.profile.tone : "border-2 border-dashed border-subtle",
                  )}
                >
                  {tile.profile ? <tile.profile.icon className="size-14 text-white" aria-hidden="true" /> : <Eye className="size-12 text-subtle" aria-hidden="true" />}
                </span>
                <span className={cn("text-lg font-semibold", focused === i ? "text-white" : "text-muted")}>{tile.label}</span>
                <span className="text-sm text-muted">{tile.blurb}</span>
              </button>
            </li>
          ))}
        </ul>
        <p className="text-[16px] text-muted">
          <strong className="text-white">Demo identity only (no auth).</strong> profiles request tokens from Identity.Api; anyone can mint an Admin token, so do not deploy publicly.
        </p>
        {error != null && <ApiErrorPanel error={error} />}
      </main>
      <Footer />
    </div>
  );
}
