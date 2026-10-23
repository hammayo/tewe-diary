import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { FocusProvider } from "@/focus/FocusProvider";
import { HeroBanner } from "./HeroBanner";
import type { HeroSlide } from "./useHeroSlides";

function slide(title: string): HeroSlide {
  return {
    slug: title.toLowerCase().replaceAll(" ", "-"),
    title,
    yearOfRelease: 2024,
    runtimeMinutes: 120,
    genres: ["Drama"],
    tagline: null,
    backdropUrl: null,
    trailer: null,
  };
}

const slides = [slide("Arrival"), slide("Dune"), slide("Oppenheimer")];

function setup() {
  render(
    <MemoryRouter>
      <FocusProvider>
        <HeroBanner slides={slides} />
      </FocusProvider>
    </MemoryRouter>,
  );
  return userEvent.setup();
}

describe("HeroBanner bars", () => {
  beforeEach(() => {
    // jsdom has neither; reduced motion keeps the 20 s auto-advance timer and trailers out of the test.
    vi.stubGlobal("matchMedia", () => ({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() }));
    vi.stubGlobal("IntersectionObserver", class { observe() {} disconnect() {} });
  });

  it("shows the first movie with its bar marked current", () => {
    // Arrange
    setup();

    // Act
    const current = screen.getByRole("button", { name: "Show Arrival" });

    // Assert
    expect(screen.getByRole("heading", { level: 1, name: "Arrival" })).toBeInTheDocument();
    expect(current).toHaveAttribute("aria-current", "true");
  });

  it("clicking a bar shows that movie", async () => {
    // Arrange
    const user = setup();

    // Act
    await user.click(screen.getByRole("button", { name: "Show Oppenheimer" }));

    // Assert
    expect(screen.getByRole("heading", { level: 1, name: "Oppenheimer" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Show Oppenheimer" })).toHaveAttribute("aria-current", "true");
    expect(screen.getByRole("button", { name: "Show Arrival" })).toHaveAttribute("aria-current", "false");
  });

  it("enter on a focused bar shows that movie", async () => {
    // Arrange
    const user = setup();
    screen.getByRole("button", { name: "Show Dune" }).focus();

    // Act
    await user.keyboard("{Enter}");

    // Assert
    expect(screen.getByRole("heading", { level: 1, name: "Dune" })).toBeInTheDocument();
  });

  it("right arrow from the pause button reaches the first bar", async () => {
    // Arrange
    const user = setup();
    screen.getByRole("button", { name: "Pause hero motion" }).focus();

    // Act
    await user.keyboard("{ArrowRight}");

    // Assert
    expect(screen.getByRole("button", { name: "Show Arrival" })).toHaveFocus();
  });
});
