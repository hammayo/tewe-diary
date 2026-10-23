import { useDeleteMovie } from "@/api/queries";
import type { MovieResponse } from "@/api/types";
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/api/client";

// The in-page confirmation before a delete (FR-10). Admin only; a 403 still shows a plain message (D7).
export function DeleteDialog({ movie, onClose, onDeleted }: { movie: MovieResponse; onClose: () => void; onDeleted: (title: string) => void }) {
  const remove = useDeleteMovie(movie.id);
  const forbidden = remove.error instanceof ApiError && remove.error.kind === "forbidden";

  return (
    <AlertDialog open onOpenChange={(open) => !open && onClose()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete {movie.title}?</AlertDialogTitle>
          <AlertDialogDescription>
            This removes the film and its ratings. It cannot be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        {remove.isError && (
          <p role="alert" className="text-sm text-red-400">
            {forbidden ? "This profile is not allowed to delete films." : remove.error.message}
          </p>
        )}
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <Button variant="destructive" disabled={remove.isPending} onClick={() => remove.mutate(undefined, { onSuccess: () => onDeleted(movie.title) })}>
            Delete
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
