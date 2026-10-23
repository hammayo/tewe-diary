import { useEffect } from "react";
import { Navigate, Outlet, useLocation, useNavigate } from "react-router";
import { useAuth } from "@/auth/AuthProvider";
import { useFocusNav } from "@/focus/FocusProvider";
import { Footer } from "./Footer";
import { Navbar } from "./Navbar";

/** Every screen but Who's watching. Until a profile (or anonymous) is chosen, go and choose one. */
export function Layout() {
  const { chosen } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const { onBack } = useFocusNav();
  const atHome = location.pathname === "/";

  // Escape and Backspace go back (FR-3), except on Home where there is nowhere to go.
  useEffect(() => (atHome ? undefined : onBack(() => navigate(-1))), [atHome, navigate, onBack]);

  if (!chosen) return <Navigate to="/profiles" state={{ from: location.pathname + location.search }} replace />;

  return (
    <div className="flex min-h-screen flex-col">
      <Navbar />
      <div className="flex-1">
        <Outlet />
      </div>
      <Footer />
    </div>
  );
}
