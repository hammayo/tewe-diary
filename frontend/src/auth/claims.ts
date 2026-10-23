import type { DemoProfile } from "./profiles";

export interface Claims {
  signedIn: boolean;
  canManage: boolean;
  canDelete: boolean;
}

// D7: the UI only hides what the token's claims do not allow. The server stays the authority.
export function claimsFor(profile: DemoProfile | null): Claims {
  const isAdmin = profile?.claims.admin === true;
  const isTrusted = profile?.claims.trusted_member === true;
  return { signedIn: profile !== null, canManage: isAdmin || isTrusted, canDelete: isAdmin };
}
