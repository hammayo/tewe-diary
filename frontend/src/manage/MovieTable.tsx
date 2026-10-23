import type { MovieResponse } from "@/api/types";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

interface Props {
  movies: MovieResponse[];
  editingSlug: string | null;
  canDelete: boolean;
  onEdit: (slug: string) => void;
  onDelete: (slug: string) => void;
}

export function MovieTable({ movies, editingSlug, canDelete, onEdit, onDelete }: Props) {
  return (
    <div className="overflow-x-auto rounded-lg border border-white/10">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="text-xs tracking-widest">TITLE</TableHead>
            <TableHead className="text-xs tracking-widest">YEAR</TableHead>
            <TableHead className="text-xs tracking-widest">GENRES</TableHead>
            <TableHead className="text-right text-xs tracking-widest">ACTIONS</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {movies.map((movie) => (
            <TableRow key={movie.id} data-state={movie.slug === editingSlug ? "selected" : undefined}>
              <TableCell className="font-semibold">{movie.title}</TableCell>
              <TableCell>{movie.yearOfRelease}</TableCell>
              <TableCell className="text-muted">{movie.genres.join(", ")}</TableCell>
              <TableCell className="space-x-2 text-right">
                {movie.slug === editingSlug ? (
                  <span className="text-sm text-muted">Editing</span>
                ) : (
                  <Button variant="link" className="underline" aria-label={`Edit ${movie.title}`} onClick={() => onEdit(movie.slug)}>
                    Edit
                  </Button>
                )}
                {canDelete && (
                  <Button variant="link" className="text-red-300 underline" aria-label={`Delete ${movie.title}`} onClick={() => onDelete(movie.slug)}>
                    Delete
                  </Button>
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
