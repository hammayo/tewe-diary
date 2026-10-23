import { http, HttpResponse } from "msw";
import { afterEach, describe, expect, it, vi } from "vitest";
import { server } from "@/test/server";
import { ApiError, fetchToken, onUnauthorized, request, setToken } from "./client";

const tokenRequest = { userId: "11111111-1111-1111-1111-111111111111", email: "m@x.dev", customClaims: {} };

async function failureOf(promise: Promise<unknown>): Promise<ApiError> {
  try {
    await promise;
  } catch (error) {
    return error as ApiError;
  }
  throw new Error("expected the request to fail");
}

afterEach(() => {
  setToken(null);
  onUnauthorized(() => {});
});

describe("request errors", () => {
  it("maps a 400 body to field errors keyed by lower-cased property name", async () => {
    // Arrange
    server.use(
      http.post("*/api/v2/movies", () =>
        HttpResponse.json(
          { errors: [{ propertyName: "Title", message: "'Title' must not be empty." }] },
          { status: 400 },
        ),
      ),
    );

    // Act
    const error = await failureOf(request("/movies", { method: "POST", body: "{}" }));

    // Assert
    expect(error.kind).toBe("validation");
    expect(error.fieldErrors.title).toBe("'Title' must not be empty.");
  });

  it.each([
    [401, "unauthorized"],
    [403, "forbidden"],
    [404, "notFound"],
    [500, "server"],
  ])("maps a %i to %s", async (status, kind) => {
    // Arrange
    server.use(http.get("*/api/v2/movies/x", () => new HttpResponse(null, { status })));

    // Act
    const error = await failureOf(request("/movies/x"));

    // Assert
    expect(error.kind).toBe(kind);
    expect(error.status).toBe(status);
  });
});

describe("request auth", () => {
  it("sends the bearer token once it is set and drops it when cleared", async () => {
    // Arrange
    const seen: (string | null)[] = [];
    server.use(
      http.get("*/api/v2/ratings/me", ({ request: req }) => {
        seen.push(req.headers.get("authorization"));
        return HttpResponse.json([]);
      }),
    );

    // Act
    setToken("abc");
    await request("/ratings/me");
    setToken(null);
    await request("/ratings/me");

    // Assert
    expect(seen).toEqual(["Bearer abc", null]);
  });

  it("calls the unauthorized handler on a 401", async () => {
    // Arrange
    const handler = vi.fn();
    onUnauthorized(handler);
    server.use(http.get("*/api/v2/ratings/me", () => new HttpResponse(null, { status: 401 })));

    // Act
    await failureOf(request("/ratings/me"));

    // Assert
    expect(handler).toHaveBeenCalledOnce();
  });
});

describe("fetchToken", () => {
  it.each([
    ["an unquoted jwt", "abc.def.ghi"],
    ["a quoted jwt", '"abc.def.ghi"'],
  ])("reads %s from the identity response", async (_name, body) => {
    // Arrange
    server.use(http.post("*/identity/token", () => HttpResponse.text(body)));

    // Act
    const token = await fetchToken(tokenRequest);

    // Assert
    expect(token).toBe("abc.def.ghi");
  });
});
