import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it, vi } from "vitest";
import { server } from "@/test/server";
import { MovieForm } from "./MovieForm";

function setup() {
  const onSaved = vi.fn();
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { mutations: { retry: false } } })}>
      <MovieForm onSaved={onSaved} onCancel={() => {}} />
    </QueryClientProvider>,
  );
  return { onSaved, user: userEvent.setup() };
}

async function fillAndSave(user: ReturnType<typeof userEvent.setup>, title: string, year: string) {
  await user.type(screen.getByLabelText("Title"), title);
  await user.type(screen.getByLabelText("Year of release"), year);
  await user.click(screen.getByRole("button", { name: "Save" }));
}

describe("MovieForm", () => {
  it("shows the api message next to the matching field on a 400", async () => {
    // Arrange
    server.use(
      http.post("*/api/v2/movies", () =>
        HttpResponse.json(
          {
            errors: [
              { propertyName: "Title", message: "'Title' must not be empty." },
              { propertyName: "YearOfRelease", message: "'Year Of Release' must be less than or equal to '2026'." },
            ],
          },
          { status: 400 },
        ),
      ),
    );
    const { user } = setup();

    // Act
    await fillAndSave(user, "x", "2099");

    // Assert
    expect(await screen.findByText("'Title' must not be empty.")).toBeInTheDocument();
    expect(screen.getByLabelText("Title")).toHaveAccessibleDescription("'Title' must not be empty.");
    expect(screen.getByLabelText("Year of release")).toHaveAccessibleDescription(
      "'Year Of Release' must be less than or equal to '2026'.",
    );
    expect(screen.getByRole("alert")).toHaveTextContent("Fix 2 fields before saving.");
  });

  it("calls onSaved with the new slug after a successful save", async () => {
    // Arrange
    server.use(
      http.post("*/api/v2/movies", () => HttpResponse.json({ id: "1", slug: "new-film-2020", title: "New film" }, { status: 201 })),
    );
    const { onSaved, user } = setup();

    // Act
    await fillAndSave(user, "New film", "2020");

    // Assert
    await vi.waitFor(() => expect(onSaved).toHaveBeenCalledWith("new-film-2020"));
  });

  it("shows a clear message on a 403", async () => {
    // Arrange
    server.use(http.post("*/api/v2/movies", () => new HttpResponse(null, { status: 403 })));
    const { user } = setup();

    // Act
    await fillAndSave(user, "New film", "2020");

    // Assert
    expect(await screen.findByRole("alert")).toHaveTextContent(/not allowed/i);
  });
});
