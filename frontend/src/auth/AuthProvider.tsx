import { useQueryClient } from "@tanstack/react-query";
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { fetchToken, onUnauthorized, setToken } from "@/api/client";
import { claimsFor, type Claims } from "./claims";
import type { DemoProfile } from "./profiles";

interface AuthValue {
  profile: DemoProfile | null;
  claims: Claims;
  /** Choose a profile, or `null` to browse anonymously. Clears every cached query. */
  choose(profile: DemoProfile | null): Promise<void>;
  signedOutNotice: string | null;
  /** False until a profile (or "Browse without a profile") has been chosen in this page load. */
  chosen: boolean;
}

const STORAGE_KEY = "tewe.profile";
const AuthContext = createContext<AuthValue | null>(null);

/** The last chosen profile id, so the Who's watching screen can start on it. Not a secret (D4). */
export function lastChosenProfileId(): string | null {
  try {
    return (JSON.parse(localStorage.getItem(STORAGE_KEY) ?? "null") as { id?: string } | null)?.id ?? null;
  } catch {
    return null;
  }
}

function remember(profile: DemoProfile): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ id: profile.id, userId: profile.userId, email: profile.email }));
  } catch {
    // Storage can be blocked; the screen then simply starts on the first tile.
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [profile, setProfile] = useState<DemoProfile | null>(null);
  const [signedOutNotice, setSignedOutNotice] = useState<string | null>(null);
  const [chosen, setChosen] = useState(false);

  const choose = useCallback(
    async (next: DemoProfile | null) => {
      if (next) {
        const jwt = await fetchToken({ userId: next.userId, email: next.email, customClaims: next.claims });
        setToken(jwt);
        remember(next);
      } else {
        setToken(null);
      }
      setProfile(next);
      setChosen(true);
      setSignedOutNotice(null);
      queryClient.clear(); // one profile never sees another's ratings (FR-2)
    },
    [queryClient],
  );

  useEffect(
    () =>
      void onUnauthorized(() => {
        setToken(null);
        setProfile(null);
        setChosen(false);
        queryClient.clear();
        setSignedOutNotice("Your session ended. Choose a profile to continue.");
      }),
    [queryClient],
  );

  const value = useMemo(
    () => ({ profile, claims: claimsFor(profile), choose, signedOutNotice, chosen }),
    [profile, choose, signedOutNotice, chosen],
  );
  return <AuthContext value={value}>{children}</AuthContext>;
}

export function useAuth(): AuthValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useAuth must be used inside AuthProvider");
  return value;
}
