import { describe, expect, it } from "vitest";
import { nextTarget, type Action } from "./focusNav";

type Cell = { row: number; col: number };

describe("nextTarget", () => {
  it.each<[string, number[], Cell, Action, Cell]>([
    ["right moves within a row", [5], { row: 0, col: 1 }, "right", { row: 0, col: 2 }],
    ["right at the last item stays", [5], { row: 0, col: 4 }, "right", { row: 0, col: 4 }],
    ["left moves within a row", [5], { row: 0, col: 2 }, "left", { row: 0, col: 1 }],
    ["left at the first item stays", [5], { row: 0, col: 0 }, "left", { row: 0, col: 0 }],
    ["down keeps the column", [5, 5], { row: 0, col: 3 }, "down", { row: 1, col: 3 }],
    ["down into a shorter row clamps to its last item", [5, 2], { row: 0, col: 4 }, "down", { row: 1, col: 1 }],
    ["down at the last row stays", [5, 5], { row: 1, col: 2 }, "down", { row: 1, col: 2 }],
    ["up at the first row stays", [5, 5], { row: 0, col: 2 }, "up", { row: 0, col: 2 }],
    ["up keeps the column", [5, 5], { row: 1, col: 3 }, "up", { row: 0, col: 3 }],
    ["down skips an empty row", [5, 0, 5], { row: 0, col: 2 }, "down", { row: 2, col: 2 }],
    ["right in an empty list stays", [0], { row: 0, col: 0 }, "right", { row: 0, col: 0 }],
  ])("%s", (_name, rows, from, action, expected) => {
    // Arrange / Act
    const target = nextTarget(rows, from, action);

    // Assert
    expect(target).toEqual(expected);
  });

  it("returning to a row restores its remembered column", () => {
    // Arrange
    const memory = new Map([[0, 1]]);

    // Act
    const target = nextTarget([5, 5], { row: 1, col: 4 }, "up", memory);

    // Assert
    expect(target).toEqual({ row: 0, col: 1 });
  });

  it("a remembered column beyond a shorter row clamps to its last item", () => {
    // Arrange
    const memory = new Map([[1, 4]]);

    // Act
    const target = nextTarget([5, 2], { row: 0, col: 0 }, "down", memory);

    // Assert
    expect(target).toEqual({ row: 1, col: 1 });
  });
});
