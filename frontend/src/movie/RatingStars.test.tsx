import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import { FocusProvider } from "@/focus/FocusProvider";
import { RatingStars } from "./RatingStars";

function setup(value: number | null, extra: { disabled?: boolean } = {}) {
  const onChange = vi.fn();
  const onClear = vi.fn();
  render(
    <MemoryRouter>
      <FocusProvider>
        <RatingStars value={value} onChange={onChange} onClear={onClear} {...extra} />
      </FocusProvider>
    </MemoryRouter>,
  );
  return { onChange, onClear, user: userEvent.setup() };
}

describe("RatingStars", () => {
  it("right arrow selects the next star and reports it", async () => {
    // Arrange
    const { onChange, user } = setup(3);
    screen.getByRole("radio", { name: "3 stars" }).focus();

    // Act
    await user.keyboard("{ArrowRight}");

    // Assert
    expect(onChange).toHaveBeenCalledWith(4);
  });

  it("left arrow at one star reports nothing", async () => {
    // Arrange
    const { onChange, user } = setup(1);
    screen.getByRole("radio", { name: "1 star" }).focus();

    // Act
    await user.keyboard("{ArrowLeft}");

    // Assert
    expect(onChange).not.toHaveBeenCalled();
  });

  it("remove my rating calls onClear", async () => {
    // Arrange
    const { onClear, user } = setup(4);

    // Act
    await user.click(screen.getByRole("button", { name: "Remove my rating" }));

    // Assert
    expect(onClear).toHaveBeenCalledOnce();
  });

  it("asks a signed-out user to choose a profile instead of showing stars", () => {
    // Arrange / Act
    setup(null, { disabled: true });

    // Assert
    expect(screen.queryAllByRole("radio")).toHaveLength(0);
    expect(screen.getByRole("link", { name: /choose a profile/i })).toBeInTheDocument();
  });
});
