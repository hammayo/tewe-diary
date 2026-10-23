import { Crown, ShieldCheck, User, type LucideIcon } from "lucide-react";

export interface DemoProfile {
  id: "member" | "trusted" | "admin";
  label: string;
  /** Stable per profile, so "My ratings" stays attached to the same user across reloads. */
  userId: string;
  email: string;
  claims: Record<string, boolean>;
  blurb: string;
  /** Tile colour on the Who's watching screen (Tailwind background class). */
  tone: string;
  /** Shown on the tile and in the navbar. */
  icon: LucideIcon;
}

// D3: demo identities, not authentication. Identity.Api mints a token for any caller.
export const profiles: DemoProfile[] = [
  {
    id: "member",
    label: "Member",
    userId: "6f1c2a40-5b0e-4c1a-9a53-0d7a1f4b2c01",
    email: "member@tewe.dev",
    claims: {},
    blurb: "Rate films, My ratings",
    tone: "bg-[#215eb0]",
    icon: User,
  },
  {
    id: "trusted",
    label: "Trusted",
    userId: "6f1c2a40-5b0e-4c1a-9a53-0d7a1f4b2c02",
    email: "trusted@tewe.dev",
    claims: { trusted_member: true },
    blurb: "Plus Manage",
    tone: "bg-[#2e7d4f]",
    icon: ShieldCheck,
  },
  {
    id: "admin",
    label: "Admin",
    userId: "6f1c2a40-5b0e-4c1a-9a53-0d7a1f4b2c03",
    email: "admin@tewe.dev",
    claims: { admin: true },
    blurb: "Plus delete on Manage",
    tone: "bg-[#a0541a]",
    icon: Crown,
  },
];
