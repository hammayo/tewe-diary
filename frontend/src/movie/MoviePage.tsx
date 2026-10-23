import { ChevronLeft } from "lucide-react";
import { useNavigate, useParams } from "react-router";
import { useMovie } from "@/api/queries";
import { useAuth } from "@/auth/AuthProvider";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiErrorPanel } from "@/shell/ApiErrorPanel";
import { MovieBody } from "./MovieBody";

export function MoviePage() {
  const { slug = "" } = useParams();
  const navigate = useNavigate();
  const { claims } = useAuth();
  const query = useMovie(slug);

  return (
    <main className="safe-x pb-16 pt-24">
      <button type="button" onClick={() => navigate(-1)} className="focus-ring mb-6 flex items-center gap-1 text-muted hover:text-white">
        <ChevronLeft className="size-5" aria-hidden="true" /> Back
      </button>
      {query.isPending ? (
        <div aria-busy="true" aria-label="Loading film" className="flex gap-10">
          <Skeleton className="aspect-[2/3] w-56" />
          <Skeleton className="h-64 flex-1" />
        </div>
      ) : query.isError ? (
        <ApiErrorPanel error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <MovieBody
          movie={query.data}
          canEdit={claims.canManage}
          canDelete={claims.canDelete}
          focusBase={0}
          onOpenMovie={(next) => navigate(`/movies/${next}`)}
        />
      )}
    </main>
  );
}
