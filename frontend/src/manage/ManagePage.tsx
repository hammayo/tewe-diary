import { Plus } from "lucide-react";
import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useMovie, useMoviePage } from "@/api/queries";
import { useAuth } from "@/auth/AuthProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { useDebounced } from "@/hooks/useDebounced";
import { ApiErrorPanel } from "@/shell/ApiErrorPanel";
import { DeleteDialog } from "./DeleteDialog";
import { MovieForm } from "./MovieForm";
import { MovieTable } from "./MovieTable";

export function ManagePage() {
  const { claims } = useAuth();
  if (!claims.canManage) {
    return (
      <main className="safe-x pt-32 text-center">
        <h1 className="font-display text-5xl font-extrabold">Manage films</h1>
        <p className="mt-4 text-muted">Managing films needs the Trusted or Admin profile.</p>
      </main>
    );
  }
  return <ManageScreen canDelete={claims.canDelete} />;
}

// D13: outside the focus navigation, with normal tab order. The URL holds what is open:
// ?edit=<slug> or ?edit=new for the form, ?delete=<slug> for the confirmation.
function ManageScreen({ canDelete }: { canDelete: boolean }) {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<string | null>(null);
  const title = useDebounced(search, 300);

  const edit = params.get("edit");
  const del = canDelete ? params.get("delete") : null;
  const list = useMoviePage({ title: title || undefined, sortBy: "title" });
  const editing = useMovie(edit ?? "", edit !== null && edit !== "new");
  const deleting = useMovie(del ?? "", del !== null);

  const close = () => setParams({});
  const open = (key: "edit" | "delete", slug: string) => {
    setStatus(null);
    setParams({ [key]: slug });
  };

  function onSaved(slug: string) {
    if (edit && edit !== "new" && slug !== edit) {
      void navigate(`/movies/${slug}`); // the slug is built from title and year, so the old address is gone
      return;
    }
    setStatus(edit === "new" ? "Film created." : "Film saved.");
    close();
  }

  return (
    <main className="safe-x pb-12 pt-24">
      <div className="grid gap-8 lg:grid-cols-[minmax(0,1fr)_26rem]">
        <section aria-label="Films">
          <div className="mb-6 flex items-center justify-between gap-4">
            <h1 className="font-display text-5xl font-extrabold tracking-tight">Manage films</h1>
            <Button size="lg" className="bg-accent text-white hover:bg-accent/85" onClick={() => open("edit", "new")}>
              <Plus aria-hidden="true" /> New film
            </Button>
          </div>
          <label htmlFor="manage-search" className="sr-only">Search films</label>
          <Input id="manage-search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search films" className="mb-6 h-12 bg-black text-base" />
          {status && <p role="status" className="mb-4 text-green-300">{status}</p>}
          {list.isPending ? (
            <Skeleton className="h-64 w-full" aria-busy="true" />
          ) : list.isError ? (
            <ApiErrorPanel error={list.error} onRetry={() => void list.refetch()} />
          ) : list.data.items.length === 0 ? (
            <p className="py-12 text-center text-muted">No films match.</p>
          ) : (
            <MovieTable movies={list.data.items} editingSlug={edit} canDelete={canDelete} onEdit={(slug) => open("edit", slug)} onDelete={(slug) => open("delete", slug)} />
          )}
          <p className="mt-4 text-sm text-subtle">Delete shows for Admin only. Trusted sees Edit and New film.</p>
        </section>

        <aside aria-label="Film form">
          {edit === "new" && <MovieForm key="new" onSaved={onSaved} onCancel={close} />}
          {edit && edit !== "new" && editing.isError && <ApiErrorPanel error={editing.error} />}
          {edit && edit !== "new" && editing.data && <MovieForm key={editing.data.id} movie={editing.data} onSaved={onSaved} onCancel={close} />}
        </aside>
      </div>
      {deleting.data && (
        <DeleteDialog
          movie={deleting.data}
          onClose={close}
          onDeleted={(deletedTitle) => {
            setStatus(`Deleted ${deletedTitle}.`);
            close();
          }}
        />
      )}
    </main>
  );
}
