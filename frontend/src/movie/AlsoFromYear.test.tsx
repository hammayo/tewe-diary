import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { MovieResponse } from "@/api/types";
import { FocusProvider } from "@/focus/FocusProvider";
import { server } from "@/test/server";
import { AlsoFromYear } from "./AlsoFromYear";

function movie(title: string, year: number): MovieResponse {
  const slug = `${title.toLowerCase().replaceAll(" ", "-")}-${year}`;
  return { id: slug, slug, title, yearOfRelease: year, genres: [], rating: null, userRating: null, posterUrl: title === "No Poster" ? null : "https://image.tmdb.org/p.jpg", details: null };
}

function setup(items: MovieResponse[], current = "arrival-2016") {
  server.use(
    http.get("*/api/v2/movies", ({ request }) => {
      const year = new URL(request.url).searchParams.get("year");
      return HttpResponse.json({ items: items.filter((m) => String(m.yearOfRelease) === year), page: 1, pageSize: 6, total: items.length, hasNextPage: false });
    }),
  );
  const onSelect = vi.fn();
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter>
        <FocusProvider>
          <AlsoFromYear slug={current} year={2016} onSelect={onSelect} />
        </FocusProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return { onSelect, user: userEvent.setup() };
}

describe("AlsoFromYear", () => {
  it("lists the other films from the year and leaves out the current one", async () => {
    // Arrange
    setup([movie("Arrival", 2016), movie("Moonlight", 2016), movie("Paterson", 2016), movie("Heat", 1995)]);

    // Act
    await screen.findByRole("heading", { name: "Also from 2016" });

    // Assert
    expect(screen.getByRole("button", { name: "Moonlight, 2016" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Paterson, 2016" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Arrival, 2016" })).not.toBeInTheDocument();
  });

  it("skips films that have no poster", async () => {
    // Arrange
    setup([movie("Arrival", 2016), movie("No Poster", 2016), movie("Moonlight", 2016)]);

    // Act
    await screen.findByRole("heading", { name: "Also from 2016" });

    // Assert
    expect(screen.queryByRole("button", { name: "No Poster, 2016" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Moonlight, 2016" })).toBeInTheDocument();
  });

  it("opens the film that is clicked", async () => {
    // Arrange
    const { onSelect, user } = setup([movie("Arrival", 2016), movie("Moonlight", 2016)]);

    // Act
    await user.click(await screen.findByRole("button", { name: "Moonlight, 2016" }));

    // Assert
    expect(onSelect).toHaveBeenCalledWith("moonlight-2016");
  });

  it("shows nothing when the current film is the only one from that year", async () => {
    // Arrange
    setup([movie("Arrival", 2016)]);

    // Act
    await new Promise((resolve) => setTimeout(resolve, 100));

    // Assert
    expect(screen.queryByRole("heading", { name: /Also from/ })).not.toBeInTheDocument();
  });
});
