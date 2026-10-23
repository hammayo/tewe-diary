import { createContext, useContext, useEffect, useMemo, useRef, type ReactNode } from "react";
import { nextTarget, type Action } from "./focusNav";

export type FocusAction = Action | "select" | "back";

interface FocusNav {
  /** Register (or with `null`, remove) the element at a grid position. Rows and columns start at 0. */
  register(row: number, col: number, el: HTMLElement | null): void;
  /** Mouse adapter: hovering moves real DOM focus without scrolling the page. */
  focusAt(row: number, col: number): void;
  /** Screens speak actions, never key codes (D10), so a gamepad adapter needs no screen changes. */
  /** Returns true when it moved focus or ran a handler, so the key press is consumed only then. */
  dispatch(action: FocusAction): boolean;
  /** Handle "back" (Escape, Backspace). The latest handler wins. Returns the unsubscribe function. */
  onBack(handler: () => void): () => void;
  /** The focusable cells by row, in column order (used to remember the last column of each row). */
  grid(): HTMLElement[][];
}

const FocusContext = createContext<FocusNav | null>(null);

const KEY_ACTIONS: Record<string, FocusAction> = {
  ArrowUp: "up",
  ArrowDown: "down",
  ArrowLeft: "left",
  ArrowRight: "right",
  Escape: "back",
  Backspace: "back",
};

// Enter is not mapped: a focused button already activates on Enter. "select" exists for other input sources.
// Up and down leave a text box for the grid, which is registered around it. Everything else belongs to the control.
function typingInside(target: EventTarget | null, action: FocusAction): boolean {
  if (!(target instanceof HTMLElement)) return false;
  if (target.closest("textarea, select, [contenteditable='true']")) return true;
  return target.closest("input") !== null && action !== "up" && action !== "down";
}

function insideDialog(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && target.closest("[role='dialog'], [role='alertdialog']") !== null;
}

export function FocusProvider({ children }: { children: ReactNode }) {
  const cells = useRef(new Map<string, HTMLElement>());
  const memory = useRef(new Map<number, number>());
  const backHandlers = useRef<(() => void)[]>([]);

  const nav = useMemo<FocusNav>(() => {
    const key = (row: number, col: number) => `${row}:${col}`;

    // Rows of the focusable cells in column order. A disabled or unregistered control leaves no gap, so every
    // item stays reachable whatever its registered column.
    function grid(): HTMLElement[][] {
      const rows: { col: number; el: HTMLElement }[][] = [];
      for (const [k, el] of cells.current) {
        if ((el as HTMLButtonElement).disabled) continue;
        const [row, col] = k.split(":").map(Number);
        (rows[row] ??= []).push({ col, el });
      }
      return Array.from(rows, (row) => (row ?? []).sort((a, b) => a.col - b.col).map((c) => c.el));
    }

    function currentCell(rows: HTMLElement[][]): { row: number; col: number } | null {
      for (let row = 0; row < rows.length; row++) {
        const col = rows[row].indexOf(document.activeElement as HTMLElement);
        if (col >= 0) return { row, col };
      }
      return null;
    }

    function focusCell(rows: HTMLElement[][], row: number, col: number, preventScroll = false) {
      rows[row]?.[col]?.focus({ preventScroll });
    }

    function dispatch(action: FocusAction): boolean {
      if (action === "back") {
        const handler = backHandlers.current.at(-1);
        handler?.();
        return handler !== undefined;
      }
      if (action === "select") {
        (document.activeElement as HTMLElement | null)?.click();
        return true;
      }

      const rows = grid();
      const from = currentCell(rows);
      if (!from) {
        // Nothing of ours has focus yet: the first arrow press lands on the first item.
        const first = rows.find((row) => row?.length > 0)?.[0];
        first?.focus();
        return first !== undefined;
      }
      const to = nextTarget(rows.map((row) => row?.length ?? 0), from, action, memory.current);
      const moved = to.row !== from.row || to.col !== from.col;
      focusCell(rows, to.row, to.col);
      return moved;
    }

    return {
      register(row, col, el) {
        if (el) cells.current.set(key(row, col), el);
        else cells.current.delete(key(row, col));
      },
      focusAt(row, col) {
        const rows = grid();
        const el = cells.current.get(key(row, col));
        const index = el ? rows[row]?.indexOf(el) : -1;
        if (index !== undefined && index >= 0) focusCell(rows, row, index, true);
      },
      dispatch,
      grid,
      onBack(handler) {
        backHandlers.current.push(handler);
        return () => {
          backHandlers.current = backHandlers.current.filter((h) => h !== handler);
        };
      },
    };
  }, []);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      const action = KEY_ACTIONS[event.key];
      if (!action || event.altKey || event.ctrlKey || event.metaKey) return;
      if (typingInside(event.target, action) || insideDialog(event.target)) return;
      if (nav.dispatch(action)) event.preventDefault(); // keys nothing handled keep scrolling the page
    }

    // Remember the last focused column of each row, however focus got there (keys, mouse, Tab).
    function onFocusIn(event: FocusEvent) {
      const rows = nav.grid();
      for (let row = 0; row < rows.length; row++) {
        const col = rows[row]?.indexOf(event.target as HTMLElement) ?? -1;
        if (col >= 0) memory.current.set(row, col);
      }
    }

    window.addEventListener("keydown", onKeyDown);
    document.addEventListener("focusin", onFocusIn);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      document.removeEventListener("focusin", onFocusIn);
    };
  }, [nav]);

  return <FocusContext value={nav}>{children}</FocusContext>;
}

export function useFocusNav(): FocusNav {
  const nav = useContext(FocusContext);
  if (!nav) throw new Error("useFocusNav must be used inside FocusProvider");
  return nav;
}
