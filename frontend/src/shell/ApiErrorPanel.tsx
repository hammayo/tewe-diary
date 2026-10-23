import { ApiError, type ApiErrorKind } from "@/api/client";
import { Button } from "@/components/ui/button";

const COPY: Record<Exclude<ApiErrorKind, "validation">, { title: string; text: string }> = {
  unauthorized: { title: "Choose a profile", text: "Your session ended. Choose a profile to continue." },
  forbidden: { title: "Not allowed", text: "This profile is not allowed to do that." },
  notFound: { title: "Not found", text: "We could not find that." },
  server: { title: "Something went wrong", text: "The server had a problem. Try again in a moment." },
  network: { title: "Cannot reach the API", text: "Check that the stack is running, then try again." },
};

/** One plain message per failure (FR-11). A 400 shows the API's own message. */
export function ApiErrorPanel({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const api = error instanceof ApiError ? error : null;
  const copy = !api || api.kind === "validation" ? { title: "Check your input", text: api?.message ?? "Unexpected error." } : COPY[api.kind];
  return (
    <div role="alert" className="mx-auto my-12 max-w-xl rounded-lg bg-surface p-8 text-center">
      <h2 className="font-display text-2xl font-bold">{copy.title}</h2>
      <p className="mt-2 text-muted">{copy.text}</p>
      {onRetry && (!api || api.kind === "server" || api.kind === "network") && (
        <Button className="mt-6" variant="secondary" onClick={onRetry}>
          Try again
        </Button>
      )}
    </div>
  );
}
