import { Eye, Search } from "lucide-react";
import type { ReactNode } from "react";
import { Link, NavLink } from "react-router";
import { useAuth } from "@/auth/AuthProvider";
import { useScrolled } from "@/hooks/useScrolled";
import { cn } from "@/lib/utils";
import { LogoMark } from "./LogoMark";

function NavItem({ to, children }: { to: string; children: ReactNode }) {
  return (
    <NavLink
      to={to}
      end
      className={({ isActive }) =>
        cn("focus-ring text-sm transition-colors hover:text-white", isActive ? "font-semibold text-white" : "text-muted")
      }
    >
      {children}
    </NavLink>
  );
}

// Transparent over the hero, solid once scrolled. The Manage link is for Trusted and Admin only (FR-13).
export function Navbar() {
  const { profile, claims } = useAuth();
  const scrolled = useScrolled(8);
  return (
    <header
      className={cn(
        "fixed inset-x-0 top-0 z-40 transition-colors duration-300 motion-reduce:transition-none",
        scrolled ? "bg-ink-deep" : "bg-gradient-to-b from-black/75 to-transparent",
      )}
    >
      <nav aria-label="Main" className="safe-x flex items-center gap-7 py-4">
        <Link to="/" className="focus-ring flex items-center gap-2 font-display text-lg font-extrabold tracking-[0.14em]">
          <LogoMark className="size-6" /> TEWE DIARY
        </Link>
        <NavItem to="/">Home</NavItem>
        <NavItem to="/browse">Browse</NavItem>
        {claims.signedIn && <NavItem to="/ratings">My ratings</NavItem>}
        {claims.canManage && <NavItem to="/manage">Manage</NavItem>}
        <span className="flex-1" />
        <Link to="/browse" aria-label="Search" className="focus-ring">
          <Search className="size-6" aria-hidden="true" />
        </Link>
        <Link to="/profiles" className="focus-ring flex items-center gap-3 text-sm" aria-label="Switch profile">
          {profile ? (
            <span className={cn("size-8 rounded-sm", profile.tone)} aria-hidden="true" />
          ) : (
            <span className="grid size-8 place-items-center rounded-sm border border-dashed border-subtle" aria-hidden="true">
              <Eye className="size-4 text-subtle" />
            </span>
          )}
          {profile?.label ?? "Anonymous"}
        </Link>
      </nav>
    </header>
  );
}
