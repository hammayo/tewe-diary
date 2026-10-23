import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import type { ReactNode } from "react";
import { describe, expect, it } from "vitest";
import { request } from "@/api/client";
import { server } from "@/test/server";
import { AuthProvider, useAuth } from "./AuthProvider";
import { claimsFor } from "./claims";
import { profiles } from "./profiles";

const [member, trusted, admin] = profiles;

function setup() {
  const client = new QueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>
      <AuthProvider>{children}</AuthProvider>
    </QueryClientProvider>
  );
  return { client, ...renderHook(() => useAuth(), { wrapper }) };
}

describe("claimsFor", () => {
  it.each([
    ["member", member, { signedIn: true, canManage: false, canDelete: false }],
    ["trusted", trusted, { signedIn: true, canManage: true, canDelete: false }],
    ["admin", admin, { signedIn: true, canManage: true, canDelete: true }],
    ["no profile", null, { signedIn: false, canManage: false, canDelete: false }],
  ])("gives %s the expected abilities", (_name, profile, expected) => {
    // Arrange / Act
    const claims = claimsFor(profile);

    // Assert
    expect(claims).toEqual(expected);
  });
});

describe("AuthProvider", () => {
  it("clears every cached query when a profile is chosen", async () => {
    // Arrange
    server.use(http.post("*/identity/token", () => HttpResponse.text("a.b.c")));
    const { client, result } = setup();
    client.setQueryData(["ratings", "me"], [{ slug: "arrival-2016", rating: 4 }]);

    // Act
    await act(() => result.current.choose(member));

    // Assert
    expect(client.getQueryData(["ratings", "me"])).toBeUndefined();
    expect(result.current.profile).toBe(member);
  });

  it("signs out and shows a notice when the api answers 401", async () => {
    // Arrange
    server.use(
      http.post("*/identity/token", () => HttpResponse.text("a.b.c")),
      http.get("*/api/v2/ratings/me", () => new HttpResponse(null, { status: 401 })),
    );
    const { result } = setup();
    await act(() => result.current.choose(trusted));

    // Act
    await act(() => request("/ratings/me").catch(() => undefined));

    // Assert
    await waitFor(() => expect(result.current.profile).toBeNull());
    expect(result.current.signedOutNotice).toMatch(/choose a profile/i);
  });
});
