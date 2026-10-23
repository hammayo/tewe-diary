import { Crown, Eye, ShieldCheck, User, type LucideIcon } from "lucide-react";
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

const profileIcons: Record<DemoProfile["id"], LucideIcon> = { member: User, trusted: ShieldCheck, admin: Crown };

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
    <div className="flex min-h-screen flex-col">
      <header className="safe-x py-8">
        <span className="flex items-center gap-2 font-display text-lg font-extrabold tracking-[0.14em]">
          <LogoMark className="size-6" /> TEWE DIARY
        </span>
      </header>
      <main className="safe-x flex flex-1 flex-col items-center justify-center gap-10 text-center">
        <h1 className="font-display text-6xl font-extrabold tracking-tight">Who's watching?</h1>
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
                  {tile.profile ? <ProfileIcon id={tile.profile.id} /> : <Eye className="size-12 text-subtle" aria-hidden="true" />}
                </span>
                <span className={cn("text-lg font-semibold", focused === i ? "text-white" : "text-muted")}>{tile.label}</span>
                <span className="text-sm text-muted">{tile.blurb}</span>
              </button>
            </li>
          ))}
        </ul>
        <p aria-live="polite" className="text-muted">
          Focused: {tiles[focused].label}. Press Enter to continue.
        </p>
        {error != null && <ApiErrorPanel error={error} />}
      </main>
      <div className="safe-x flex items-end justify-between gap-8 pb-2 text-sm text-muted">
        <p className="max-w-2xl">
          <strong className="text-white">Demo identity, not authentication.</strong> Choosing a profile asks Identity.Api for a token
          with that tier's claims. Anyone can mint an Admin token, so this build must not be deployed publicly.
        </p>
      </div>
      <Footer />
    </div>
  );
}

function ProfileIcon({ id }: { id: DemoProfile["id"] }) {
  const Icon = profileIcons[id];
  return <Icon className="size-14 text-white" aria-hidden="true" />;
}
