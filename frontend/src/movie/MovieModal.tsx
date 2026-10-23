import { useMovie } from "@/api/queries";
import { useAuth } from "@/auth/AuthProvider";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiErrorPanel } from "@/shell/ApiErrorPanel";
import { MovieBody } from "./MovieBody";

/** The same content as the movie page, over the Home screen. Radix traps focus and gives it back on close. */
export function MovieModal({ slug, onClose }: { slug: string; onClose: () => void }) {
  const { claims } = useAuth();
  const query = useMovie(slug);

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] !max-w-[min(72rem,calc(100vw-2rem))] overflow-y-auto bg-surface p-8">
        <DialogTitle className="sr-only">{query.data?.title ?? "Film details"}</DialogTitle>
        <DialogDescription className="sr-only">Details, rating and cast.</DialogDescription>
        {query.isPending ? (
          <Skeleton className="h-96 w-full" />
        ) : query.isError ? (
          <ApiErrorPanel error={query.error} onRetry={() => void query.refetch()} />
        ) : (
          <MovieBody movie={query.data} canEdit={claims.canManage} canDelete={claims.canDelete} />
        )}
      </DialogContent>
    </Dialog>
  );
}
