export type Action = "up" | "down" | "left" | "right";
export interface Cell {
  row: number;
  col: number;
}

/**
 * Where focus goes next on a grid of rows. `rows[i]` is the number of items in row i.
 * Moving vertically goes to the remembered column of the destination row (`memory`), else keeps the
 * current column, clamped to the row's length. Empty rows are skipped. At an edge focus stays put.
 */
export function nextTarget(rows: number[], from: Cell, action: Action, memory: Map<number, number> = new Map()): Cell {
  if (action === "left") return { row: from.row, col: Math.max(0, from.col - 1) };
  if (action === "right") {
    const last = Math.max(0, (rows[from.row] ?? 0) - 1);
    return { row: from.row, col: Math.min(last, from.col + 1) };
  }

  const step = action === "down" ? 1 : -1;
  for (let row = from.row + step; row >= 0 && row < rows.length; row += step) {
    if (rows[row] > 0) {
      return { row, col: Math.min(memory.get(row) ?? from.col, rows[row] - 1) };
    }
  }
  return from;
}
