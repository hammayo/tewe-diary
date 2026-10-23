import { Link } from "react-router";

export function NotFoundPage() {
  return (
    <main className="safe-x pt-32 text-center">
      <h1 className="font-display text-5xl font-extrabold">Not found</h1>
      <p className="mt-4 text-muted">There is nothing at this address.</p>
      <Link to="/" className="btn-primary mt-8">
        Back to Home
      </Link>
    </main>
  );
}
