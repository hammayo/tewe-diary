# PRD — React Frontend (TV-style)

|             |                                                                                                                     |
|-------------|---------------------------------------------------------------------------------------------------------------------|
| **Status**  | In progress — 2026-10-23 (phase 1 built on `feature/roadmap-react-frontend`; open items are in §11 and the findings below) |
| **Owner**   | Hammy Babar                                                                                                         |
| **Branch**  | `feature/roadmap-react-frontend` (proposed)                                                                         |
| **Doc set** | **PRD (this)** → SDD `_tasks/04-react-frontend-spec.md` → plan `_tasks/04-react-frontend-plan.md` (not written)     |
| **Note**    | `_tasks/` is gitignored, so those two docs exist only in my local checkout and are not linked.                      |

Status legend (used in all three docs): `Todo` · `In progress` · `Done` · `Blocked` · `Dropped` · `Deferred`
(`Deferred` means in the PRD on purpose, built in phase 2).

## 1. Problem

Everything the API does is invisible unless you use Postman or curl: the poster data, the movie page, the three
authorisation tiers and the error contract. A reviewer of this portfolio cannot see it working. The repo has a
placeholder React shell (`frontend/`, Vite + React + TypeScript, served by nginx on `:5173`) and nothing else.

A plain admin-style grid would show the API but not impress anyone. The app should feel like the home screen of
Netflix, Apple TV or HBO Max on a TV: a full-screen hero with a trailer playing behind the title, rows of posters and
big, focus-driven navigation.

## 2. Goals

1. A reviewer opens `http://localhost:5173`, picks a profile on a "Who's watching?" screen and sees what each tier
   unlocks, with no Postman or curl.
2. The home screen has the TV feel: a hero banner with a muted trailer autoplaying behind the title, poster rows and
   focus-driven navigation with big type, built first for a 1920×1080 screen.
3. The app is built in **two phases**. Phase 1 uses what the v2 API serves today (it matches v1). Phase 2 adds the rows
   that need a richer v2 (top rated, popular, genre, My Watch List) when the matching pieces in the parked browse design
   (`_03-v2-browse-api-prd.md`, §9) are built. The phase 1 code leaves a place for them (D12).
4. Phase 1 changes neither API: no CORS, no new endpoints, no migrations (NFR-1).
5. The app is small, typed, accessible and tested where it matters because the engineering decisions are the point,
   not the number of screens. It uses popular open-source UI libraries (Tailwind CSS, shadcn/ui) and runs in a
   container for dev, test and deploy (D17).

## 3. Non-goals

- **Phase 1:** anything the current API cannot serve: top rated, popular and genre rows, a genre filter, a watch list,
  "similar movies", search by cast or director. §8 maps each to the v2 piece that unlocks it.
- Gamepad input (the Gamepad API, as on SteamOS) and builds for real TV platforms (tvOS, webOS, Android TV). Both come
  later. The navigation layer is written so a gamepad is just another input source (D10).
- Phone layouts. The app is built for 1920×1080 and 1280×720 first and works down to 1024 px wide. Narrower screens are
  not a target.
- Real accounts. `Identity.Api` mints a token for any caller, so the profile screen is a **demo identity** (D3), not
  authentication.
- Playing films. "Play" means the trailer.
- Server-side rendering, a PWA, i18n, analytics, a light theme (the TV look is dark only).
- An end-to-end browser test (Playwright). A manual checklist covers it for now (§10).
- A generated API client. The types are written by hand (D6).
- Exhaustive UI tests: no snapshot tests, no tests of shadcn/ui components, no per-component suites (D18).

## 4. Users and use cases

| Who                 | Needs                                                                                                               |
|---------------------|---------------------------------------------------------------------------------------------------------------------|
| Anonymous visitor   | See the hero and rows, browse, search by title, filter by year, sort, open a movie and watch the trailer            |
| Signed-in member    | Rate and un-rate a movie and review their own ratings. In phase 2, keep a watch list that follows them              |
| Trusted member      | Create and edit movies on the Manage screen, with validation errors shown next to the field                         |
| Admin               | Delete a movie on the Manage screen                                                                                 |
| Reviewer            | See each tier's controls appear and disappear, and see the API's `400`, `401`, `403` and `404` handled cleanly      |

## 5. Decisions (my calls — please confirm in review)

| Id  | Decision                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
|-----|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| D1  | **Same-origin access, no CORS.** The browser only calls `/api/...` and `/identity/...` on its own origin. The Vite dev server (`server.proxy`) and the nginx container (`proxy_pass`) forward them to Movies.Api and Identity.Api. `VITE_API_URL` is removed. The container reaches the APIs over the compose network on plain HTTP, so the dev certificate never reaches the browser. Movies.Api redirects plain HTTP to HTTPS (`307`), so the proxy sends `X-Forwarded-Proto: https`, which the API honours.                                                                                                                                                                                                                                                                                                       |
| D2  | **Stack:** Vite, React, TypeScript (strict), React Router, TanStack Query for server state, **Tailwind CSS v4** (`@tailwindcss/vite`) and **shadcn/ui** for the component layer, Vitest + React Testing Library + MSW for tests, ESLint. shadcn/ui is copied into `frontend/src/components/ui/` (Radix UI primitives plus Tailwind classes), so the components are my code, not a runtime dependency. No state library and no spatial-navigation library. I am a backend engineer, so I use popular open-source UI libraries instead of hand-rolling widgets.                                                                                                                                |
| D3  | **"Who's watching?" profile screen.** Three demo profiles (Member, Trusted, Admin) chosen with the arrow keys or the mouse. Choosing one calls `POST /identity/token` with a stable user id and the matching claims (`trusted_member`, `admin`). An anonymous "Browse without a profile" tile is also offered. The screen says plainly that this is a demo identity.                                                                                                                                                                                                                                                                                                                         |
| D4  | **The JWT lives in memory only.** The user id and email are kept in `localStorage` (they are not secrets), so a reload keeps "My ratings" attached to the same user after the profile is chosen again. The token is never in storage, URLs or logs.                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| D5  | **URL is the state for browsing:** `/browse?title=&year=&sortBy=&page=` for the full grid. Other routes: `/` (home), `/movies/:slug`, `/ratings`, `/profiles` and `/manage`. Back, forward and shared links work.                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| D6  | **Hand-written TypeScript types** mirroring the v2 contracts (the same shapes as v1, about nine interfaces). Generating them from the Swagger document is a later step.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| D7  | **The server is the authority on roles.** The UI hides controls the token's claims do not allow, and still handles a `403` if one slips through.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| D8  | **The hero trailer autoplays, muted.** The iframe is injected after the page has loaded and gone idle, with `autoplay=1&mute=1&controls=0&playsinline=1`. It does not start when the user prefers reduced motion or has data saving on, and it pauses when the tab is hidden or the hero scrolls out of view. The hero then shows the still backdrop. The movie page and the Play button open a full-screen player with sound, started by the user.                                                                                                                                                                                                                                          |
| D9  | **Security headers from nginx:** a Content-Security-Policy limited to self, `image.tmdb.org` for images and YouTube and Vimeo for frames (the API emits trailers from both), plus `nosniff` and `Referrer-Policy`. nginx also falls back to `index.html` for unknown paths, so a reload on `/movies/:slug` works.                                                                                                                                                                                                                                                                                                                                                                            |
| D10 | **Focus-driven navigation, written in-house.** One small module owns focus: it moves it between rows and cards by direction (arrow keys), selects with Enter, goes back with Escape or Backspace, and remembers the last focused card in each row. Real DOM focus is used (`element.focus()`), so screen readers and the browser's focus ring still work. Keyboard and mouse are the only input adapters in phase 1 (hovering moves focus). Screens talk to the module in actions (`up`, `down`, `left`, `right`, `select`, `back`), never in key codes, so a gamepad adapter adds no screen changes later.                                                                                  |
| D11 | **The hero is picked by hand, with a fallback.** A small file in `frontend/` lists up to five movie slugs. Fewer than five (or a movie with no trailer and no backdrop) is topped up with the newest releases from the list endpoint. A hero slide needs the tagline, backdrop and trailer, which only the single-movie GET returns, so the hero fetches one detail per slide (cached for 60 s by the API).                                                                                                                                                                                                                                                                                  |
| D12 | **Rows are a registry, and rows that need a richer v2 are listed but switched off.** Each row declares the API piece it needs (`requires: "B9"`). Phase 1 turns on *Newest* and *A to Z*. Top rated, Popular, Genre and My Watch List are registered as `Deferred` and appear when their piece lands. Nothing probes the API at runtime.                                                                                                                                                                                                                                                                                                                                                     |
| D13 | **The Manage screen is plain.** Create, edit and delete live on `/manage`, outside the focus navigation, with normal tab order, labels and inline errors. It is built from shadcn/ui `Table`, `Input`, `Label`, `Button` and `AlertDialog` (the in-page delete confirmation). It is shown only to Trusted and Admin profiles. Forms with text fields do not suit a remote.                                                                                                                                                                                                                                                                                                                   |
| D14 | **The app calls `/api/v2`.** v2 matches v1 shape for shape (PRD 03), and the richer v2 will grow there. The version is one constant, so switching back to v1 is one line.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                    |
| D15 | **TV styling.** Dark only, `rem` units with the root size scaled to the viewport (24 px body text at 1080p), a 5% safe margin on every edge, and a focus ring plus a scale-up on the focused card. Tailwind v4 tokens in `src/index.css` (`@theme`) carry the palette and fonts, and the shadcn theme variables map onto them. The TV pieces (hero, rows, cards, focus ring) are my own components. shadcn/ui supplies the generic pieces (dialog, button, input, table, tooltip, skeleton, toast). The look is the design in `_docs/tewe-diary-ui-design.pdf`.                                                                                                                              |
| D16 | **Trailer player.** Play opens a full-screen shadcn/ui `Dialog` (Radix) with an on-screen **Close** button that takes focus first. A cross-origin iframe swallows key presses once clicked, so Escape cannot be the only way out. Radix traps focus inside the dialog and returns it to the card or button that opened it on close. Hero autoplay uses `autoplay=1&mute=1` for YouTube and `autoplay=1&muted=1&background=1` for Vimeo.                                                                                                                                                                                                                                                      |
| D17 | **Docker for dev, test and deploy.** One multi-stage `frontend/Dockerfile` with three targets: `dev` (Vite dev server with hot reload, source bind-mounted, on `:5174` so it can run beside the production container), `test` (runs lint, typecheck and Vitest, which is what CI calls) and `final` (Vite build served by unprivileged nginx with the CSP and the `/api` and `/identity` proxy from D1 and D9). Compose runs `final` by default as `frontend` on `:5173`. `docker compose --profile dev up frontend-dev` and `docker compose run --rm --build frontend-test` run the other two, so no local Node install is needed. The proxy targets come from environment variables (compose service names inside the network), not `localhost`. |
| D18 | **Unit tests stay essential.** Test the logic and the contract with the API, not the markup. Vitest with MSW covers the focus module, the API client and error mapping, role-gated controls, rate and un-rate and the Manage form's field errors. Components from shadcn/ui are not tested, and there are no snapshot tests. The manual checklist in §10 covers the rest.                                                                                                                                                                                                                                                                                                                    |

## 6. Functional requirements

### Phase 1

| Id     | Requirement                          | Acceptance                                                                                                                                                                                                                                                                                                                                                                                                                                              | Status  |
|--------|--------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------|
| FR-1   | Same-origin API access and container | `npm run dev` and the container both reach Movies.Api at `/api` and Identity.Api at `/identity` with no CORS headers involved. `VITE_API_URL` is gone from compose, `.env.example` and the docs. The API version is one constant (D14). A reload on a deep link such as `/movies/:slug` serves the app, not a `404`. The app runs in a container for dev, test and deploy (D17)                                                                         | Todo    |
| FR-2   | "Who's watching?" profiles           | `/profiles` shows Member, Trusted, Admin and Browse tiles, chosen with the arrow keys and Enter or with the mouse. The chosen role shows in the header, and switching profile clears the token. A `401` returns to this screen with a message. Switching profile clears every cached query, so one profile never sees another's ratings                                                                                                                 | Todo    |
| FR-3   | Focus navigation                     | Arrow keys move focus between cards and rows, Enter selects and Escape or Backspace goes back, restoring the previous focus. The mouse moves focus on hover. The module is unit-tested without screens, and screens use actions only (D10)                                                                                                                                                                                                              | Todo    |
| FR-4   | Home with rows                       | `/` shows the hero and the *Newest* and *A to Z* rows from `GET /api/v2/movies`. Rows scroll sideways and load the next page (at most 25 movies) as focus nears the end. *Newest* breaks ties on title, so the order is stable. Movies without a poster show a text placeholder                                                                                                                                                                         | Todo    |
| FR-5   | Hero banner                          | Up to five slides from the hand-picked list, topped up with the newest releases (D11). Each shows the backdrop, title, year, tagline, **Play** and **More info**, with the muted trailer playing behind (D8). The slide advances when the trailer ends, or after 20 s without one. A visible pause and play control stops the motion. A movie with neither a trailer nor a backdrop, or a slug missing from the data, is skipped. Focus starts on Play  | Todo    |
| FR-6   | Browse and search                    | `/browse` shows a poster grid with a title box (debounced), a year field and a sort control (title, year, either direction). They update the URL. Clearing them resets the page to 1. Invalid input shows the API's message                                                                                                                                                                                                                             | Todo    |
| FR-7   | Movie page                           | `/movies/:slug` shows the backdrop, poster, title, year, genres, tagline, overview, runtime, directors, writers, cast with photos and the average rating. **Play** opens the trailer player (D16) with sound. A manual movie has no TMDB details, so its page shows only title, year, genres and rating, with no Play button. An **Also from <year>** row lists up to five other films from the same year that have a poster (`GET /api/v2/movies?year=`), and opens the one picked. Unknown slug shows a not-found page                                                                                       | Todo    |
| FR-8   | Rate and un-rate                     | A signed-in user picks 1 to 5 stars (left and right arrows, or the mouse) or removes their rating. The page shows their rating at once, using `userRating` from the GET, and the average refreshes. A signed-out user is asked to choose a profile                                                                                                                                                                                                      | Todo    |
| FR-9   | My ratings                           | `/ratings` shows the caller's rated movies as posters with their rating. It needs a profile, and says so when there is none                                                                                                                                                                                                                                                                                                                             | Todo    |
| FR-10  | Manage screen                        | `/manage` (Trusted and Admin only, D13) lists movies with search, and lets a Trusted or Admin user create and edit a movie (title, year, genres). `400` responses appear next to the matching field. An Admin can delete a movie after an in-page confirmation. A `403` shows a clear message. Saving an edit that changes the slug (it is built from title and year) opens the movie at its new slug and refreshes the cached rows                     | Todo    |
| FR-11  | Loading, empty and error states      | Every data view has a loading state (poster-shaped skeletons), an empty state and an error state with a retry. `401`, `403`, `404`, `400` and `5xx` each show a distinct, plain message                                                                                                                                                                                                                                                                 | Todo    |
| FR-12  | TMDB attribution                     | Every screen shows "This product uses the TMDB API but is not endorsed or certified by TMDB." in its bottom safe margin                                                                                                                                                                                                                                                                                                                                 | Todo    |
| FR-13  | Role-aware controls                  | The Manage link, and the create, edit and delete controls, appear only for the claims that allow them (D7)                                                                                                                                                                                                                                                                                                                                              | Todo    |

### Phase 2 (needs the richer v2, see §8)

| Id     | Requirement                 | Acceptance                                                                                                                                                            | Status    |
|--------|-----------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------|
| FR-14  | Top rated and Popular rows  | Two registered rows (D12) ordered by `sort=rating` and `sort=popular`, shown when B3 and B9 exist. Top rated ignores movies with too few ratings                      | Deferred  |
| FR-15  | Genre rows                  | One row per genre from `GET /api/v2/genres`, each filtered with `genre=`, shown when B3 and B5 exist                                                                  | Deferred  |
| FR-16  | My Watch List               | A row and a **My List** button on the movie page add and remove a movie. The list follows the signed-in user across devices (B11). Signed out, the row is absent      | Deferred  |
| FR-17  | Richer cards                | Cards use `backdropUrl`, `rating` and `ratingCount` from the list response (B1, B8), so the hero's backdrop and the rating badges need no detail request              | Deferred  |

## 7. Non-functional requirements

| Id      | Requirement                                                                                                                                                                                                                                                                                                                                                                                                   | Status |
|---------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------|
| NFR-1   | **No backend change in phase 1.** v2, v1, Identity.Api, their ports and their contracts are untouched. Only `frontend/`, compose, CI and docs change. Phase 2 backend work is tracked in `_03-v2-browse-api-prd.md`, not here                                                                                                                                                                                 | Todo   |
| NFR-2   | **Types and lint:** TypeScript `strict`, ESLint clean, no `any` in app code                                                                                                                                                                                                                                                                                                                                   | Todo   |
| NFR-3   | **Bundle:** the initial JavaScript is at most 150 KB gzipped, with shadcn/ui and Radix included (import only the components used). Routes are code-split, so the Manage screen does not load on the home screen                                                                                                                                                                                               | Todo   |
| NFR-4   | **Lighthouse** on the production build (desktop preset, local, trailer disabled): Performance at least 90, Accessibility at least 95, Best Practices at least 95. The trailer is excluded because a third-party iframe skews the lab score                                                                                                                                                                    | Todo   |
| NFR-5   | **No layout shift:** poster, card and backdrop boxes have a fixed aspect ratio, images load lazily off screen, and cumulative layout shift is under 0.1                                                                                                                                                                                                                                                       | Todo   |
| NFR-6   | **Tests, kept to the essentials (D18):** Vitest, React Testing Library and MSW. The focus module has table-driven tests (direction, edges, memory). Beyond that, a small set of tests for the logic that can break: the API client and error mapping, role-gated controls, rate and un-rate, and the Manage form's field errors. No snapshot tests, no tests of shadcn/ui components, no per-component suites | Todo   |
| NFR-7   | **Token handling:** the JWT is never written to storage, the URL or the console. No secret is in the bundle                                                                                                                                                                                                                                                                                                   | Todo   |
| NFR-8   | **Security headers:** nginx sends the CSP, `X-Content-Type-Options` and `Referrer-Policy` from D9, and the app works under that policy, including the autoplaying trailer                                                                                                                                                                                                                                     | Todo   |
| NFR-9   | **Accessible by construction:** real DOM focus on every focusable element, a focus ring with at least 3:1 contrast, rows and cards with roles and labels, images with `alt` text, forms with labels and associated errors, the star control operable by keyboard, and no keyboard trap                                                                                                                        | Todo   |
| NFR-10  | **10-foot legibility:** body text at least 24 px at 1080p, titles larger, a 5% safe margin on every edge, and text over artwork on a gradient that keeps a 4.5:1 contrast                                                                                                                                                                                                                                     | Todo   |
| NFR-11  | **Motion manners:** the trailer is always muted, never starts under `prefers-reduced-motion` or data saving, pauses when the tab is hidden or the hero is off screen, and card animations are switched off under reduced motion. The hero has a visible pause control (WCAG 2.2.2)                                                                                                                            | Todo   |
| NFR-12  | **Responsive range:** usable from 1024 to 2560 px wide, checked at 1280×720 and 1920×1080, with no horizontal page scroll (rows scroll inside themselves)                                                                                                                                                                                                                                                     | Todo   |
| NFR-13  | **Input latency:** a key press moves focus and paints in under 100 ms with 1,000 cards loaded (rows load pages as needed, so the DOM stays small)                                                                                                                                                                                                                                                             | Todo   |
| NFR-14  | **CI:** `frontend.yml` runs install, lint, typecheck, test and build on `frontend/**` changes, using the same container targets as local work (D17). The production image still builds                                                                                                                                                                                                                        | Todo   |
| NFR-15  | **Docs:** `README.md` and `_docs/_getting-started.md` describe running the frontend (container and bare `npm`), the profiles, the proxy, how to run the tests and how to edit the hero list                                                                                                                                                                                                                   | Todo   |
| NFR-16  | **Browsers:** the latest Chrome, Safari and Firefox on desktop. The autoplay, focus and Lighthouse checks run in Chrome, and the autoplay check is repeated in Safari and Firefox. TV browsers come with the TV builds                                                                                                                                                                                        | Todo   |
| NFR-17  | **Containerised workflow:** `docker compose up` serves the production build, `--profile dev` gives hot reload and `run --rm frontend-test` runs the checks, all without Node on the host. The image runs as a non-root user and stays small (nginx-unprivileged on Alpine)                                                                                                                                    | Todo   |

## 8. Gaps against the current v2 API and quick wins

I checked each thing the TV-style UI needs against `GET /api/v2/movies`, `GET /api/v2/movies/{idOrSlug}` and
`GET /api/v2/ratings/me` (identical to v1). The B-ids refer to the parked browse design in `_03-v2-browse-api-prd.md`
§9, which keeps the API side of each fix.

| Id  | Need in the UI                      | What the API gives today                                                                                                   | Gap                                                                             | Fix                                                                                                                                    |
|-----|-------------------------------------|----------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------|
| G1  | Hero: tagline, backdrop, trailer    | List items carry `posterUrl` and the average `rating`. Backdrop, tagline, overview and trailer are on the single GET only  | One detail request per hero slide (at most five, cached for 60 s)               | Phase 1 accepts it (D11). Later, `backdropUrl` on list cards (B1); tagline and trailer stay on the detail                              |
| G2  | Top rated row                       | Sort by title or year only. The list has `rating` but cannot sort by it                                                    | No rating sort, and no rating count to ignore one-vote movies                   | `sort=rating` and `ratingCount` (B8, B9)                                                                                               |
| G3  | Popular row                         | Nothing                                                                                                                    | No popularity signal                                                            | A ranking column from TMDB and `sort=popular` (B9). My guess for "popular": TMDB popularity                                            |
| G4  | Genre rows                          | `genres` on every movie                                                                                                    | No filter, no list of genres                                                    | `genre=` (B3) and `GET /api/v2/genres` (B5)                                                                                            |
| G5  | My Watch List                       | Nothing                                                                                                                    | A per-user list needs storage and endpoints. It is the only write feature       | New **B11** in PRD 03: a table, an expand-only migration, `GET`, `PUT` and `DELETE` under `/api/v2/me/watchlist`, any JWT, not cached  |
| G6  | My ratings with titles              | `GET /ratings/me` returns movie id, slug and rating                                                                        | The page needs a request per rated movie                                        | `GET /api/v2/me/ratings` returning cards (B9)                                                                                          |
| G7  | Fast typing search                  | `title` filter on the list, which also counts rows                                                                         | Counts every time; no suggestions                                               | `GET /api/v2/search?q=` (B4) when the browse screen wants live suggestions                                                             |
| G8  | Sharp artwork                       | Sizes are configuration in `appsettings.json`: posters `w500`, backdrops `w1280`                                           | `w500` is soft for a large poster on the movie page; `w1280` suits a 1080p hero | Phase 1 uses the posters as they are. A larger poster size is a configuration change if it looks soft                                  |
| G9  | Rating average right after rating   | Rating writes do not evict the movie cache, which lasts 60 s                                                               | The average can lag by a minute                                                 | Phase 1: show `userRating` at once. Phase 2: evict on rating writes, or return the new average (open)                                  |
| G10 | Row paging                          | `page`, `pageSize`, `total` and `hasNextPage`                                                                              | None. `total` costs a count per request                                         | B3 drops the count for browse                                                                                                          |
| G11 | Age certification on cards          | Nothing                                                                                                                    | Netflix-style cards show it; the data has none                                  | Optional, with the ranking data (B9). Not needed for either phase                                                                      |

**Quick wins that cost the backend little (all additive, so v1 is untouched):**

1. `backdropUrl` and `ratingCount` on list items (G1, G2, FR-17). They are fields already in the data.
2. `sort=rating` on the list (G2). One more `ORDER BY` in the existing query.
3. `GET /api/v2/genres` and a `genre` filter (G4). Genres are already in a table.

**Quick wins on the frontend side (no backend):** call `/api/v2` through one constant (D14), the row registry (D12),
the action-based focus module (D10) and a card component that already reads `backdropUrl` and `ratingCount` when
present. Phase 2 then turns rows on instead of rewriting screens.

## 9. Risks and known limitations

- **The ratings average can lag by up to a minute.** The movie responses are cached for 60 seconds and rating writes
  do not evict that cache (G9). The UI shows the user's own rating at once and refreshes the average, but the average
  may not change until the cache expires. I will confirm this in the first spike (§10).
- **`userRating` for a signed-in caller is correct.** Checked 2026-10-22: the default output-cache policy neither
  stores nor serves a request that carries an `Authorization` header, so a signed-in `GET` bypasses the cached
  anonymous body. On the running stack, v1 and v2 both returned the caller's own `userRating` (4) when signed in and
  `null` when anonymous. The movie page can use `userRating` from the GET.
- **"My ratings" has no titles** (G6). The page fetches each movie by slug (cached reads), which is acceptable for a
  handful of ratings and slow for hundreds.
- **An autoplaying third-party trailer is fragile.** Browsers allow muted autoplay, but YouTube can refuse to embed some
  videos, and an iframe skews Lighthouse. A slide whose trailer fails to play shows its backdrop. Spike it first (§10).
- **Vimeo trailers behave differently.** The API can emit either site. The UI supports both (D9, D16); a trailer that does not
  autoplay shows its backdrop.
- **Hand-picked slugs must exist in the imported data.** A missing slug is skipped (FR-5), so a wrong list shrinks the
  hero silently. The spike picks five real movies with trailers.
- **Hand-written focus navigation is the riskiest code in the app.** Edge cases are rows of different lengths,
  focus restore after back, and mouse and keyboard fighting over focus. It gets its own tests (NFR-6) and stays small.
- **Phase 1 rows are thin.** Newest and A to Z will not look like a full catalogue app until phase 2. The registry
  (D12) makes the missing rows visible in the code and in this PRD, not in the UI.
- **Sign-in is not authentication.** Anyone can mint an Admin token. That is acceptable for a demo behind local
  Docker and must be stated on the profile screen and in the README. It must not be deployed publicly as is.
- **shadcn/ui and Radix add weight and their own focus handling.** Radix dialogs trap and restore focus, which
  matches D16 but must not fight the in-house focus module (D10) on the TV screens. The TV pieces stay my own
  components, and only the components in use are copied in, to hold NFR-3.
- **Dependency choices.** Vite 8 is used as the shell had it. TypeScript is pinned to 6.0 because `typescript-eslint` does not
  support 7 yet. React is 19 because React Router 8 requires it. `frontend/.npmrc` sets `legacy-peer-deps`, because npm 10 crashes
  resolving the peer set when Vitest is installed without it.
- **Hotlinking TMDB images** relies on TMDB's CDN staying available. Cards fall back to a text placeholder.
- **Output-cached lists** can show a just-created movie late if the cache were not evicted. v2 evicts the tag on
  create, update and delete, so the UI also invalidates its own queries.

## 9a. Findings from the build

- **Newest paging is unstable (backend, needs a decision).** `GET /movies?sortBy=-yearofrelease` orders by one column
  with no tiebreak (`MovieRepository`), and the API rejects a two-key sort. Page 1 and page 2 overlapped on 17 of 25 films
  and three pages returned 41 unique of 75, while `sortBy=title` is clean. The UI drops repeats across pages, so nothing
  shows twice, but some films never reach the Newest row. A title or id tiebreak in the query fixes it, which breaks NFR-1
  (no backend change in phase 1). It belongs with the parked browse design (`_03-v2-browse-api-prd.md`).
- **The "ended" signal for the hero trailer and autoplay in Safari and Firefox are unverified.** The muted iframe loads
  under the CSP in Chrome with no console errors. Advancing on a real end of playback, Vimeo trailers, and Safari and
  Firefox are still to check (NFR-16, FR-5).
- **Measured so far.** Initial JavaScript is about 145 KB gzipped (index 90.0 KB plus the shared chunk 55.4 KB; Manage is a
  separate 4.5 KB chunk). Lighthouse (desktop, production build) on `/profiles`: Performance 99, Accessibility 100, Best
  Practices 100, CLS 0. Home and the other screens sit behind the in-memory token, so they are not audited yet (NFR-4, NFR-5).
- **The list cannot tell a TMDB film from a manual one**, so the Manage table has no Source column (list items always carry
  `details: null`).

## 10. Success metrics

- A reviewer can run `bash scripts/stack-up.sh`, open `http://localhost:5173` and complete this checklist without any
  other tool, using only the keyboard and then only the mouse: pick a profile, watch the hero trailer start muted,
  move through the rows with the arrow keys, open a movie and play its trailer with sound, search by title on
  `/browse`, rate and un-rate as a Member, open "My ratings", create and edit a movie on `/manage` as Trusted and see a
  field error, and delete it as Admin.
- NFR-3, NFR-4 and NFR-5 are met on the production build, with the Lighthouse scores recorded in this PRD.
- CI is green and the test suite covers every phase 1 FR and the focus module.
- Neither backend project has a changed file in phase 1 (NFR-1).

**First spikes (before the plan):**

1. Average lag: call `GET /api/v2/movies/{slug}` anonymously and then with a token that has rated that movie, and
   compare the average. The `userRating` part is done (§9).
2. Trailer: embed a TMDB trailer with `autoplay=1&mute=1` under the nginx CSP and confirm it starts in current Chrome,
   Safari and Firefox.
3. Hero list: pick five real movies from the imported data that have a trailer and a backdrop, and record their slugs.

## 11. Traceability and status (master tracker)

The plan's task statuses roll up here. Update this table whenever a plan task changes status.

| Req     | SDD section  | Plan task(s)  | Status        |
|---------|--------------|---------------|---------------|
| FR-1      | —              | T2              | Done        |
| FR-2      | —              | T4, T6          | Done        |
| FR-3      | —              | T5              | Done        |
| FR-4      | —              | T7              | Done        |
| FR-5      | —              | T8              | In progress |
| FR-6      | —              | T10             | Done        |
| FR-7      | —              | T9              | Done        |
| FR-8      | —              | T9              | Done        |
| FR-9      | —              | T10             | Done        |
| FR-10     | —              | T11             | Done        |
| FR-11     | —              | T6, T7          | Done        |
| FR-12     | —              | T6              | Done        |
| FR-13     | —              | T4, T6, T11     | Done        |
| FR-14   | —            | —             | Deferred      |
| FR-15   | —            | —             | Deferred      |
| FR-16   | —            | —             | Deferred      |
| FR-17   | —            | —             | Deferred      |
| NFR-1     | —              | T13             | Done        |
| NFR-2     | —              | T1              | Done        |
| NFR-3     | —              | T13             | Done        |
| NFR-4     | —              | T13             | In progress |
| NFR-5     | —              | T13             | In progress |
| NFR-6     | —              | T3 to T11       | Done        |
| NFR-7     | —              | T3, T4          | Done        |
| NFR-8     | —              | T2              | Done        |
| NFR-9     | —              | T13             | In progress |
| NFR-10    | —              | T6              | In progress |
| NFR-11    | —              | T8              | In progress |
| NFR-12    | —              | T13             | In progress |
| NFR-13    | —              | T13             | In progress |
| NFR-14    | —              | T12             | In progress |
| NFR-15    | —              | T12             | Done        |
| NFR-16    | —              | T13             | In progress |
| NFR-17    | —              | T2              | Done        |

## 12. Change log

| Date         | Change                                                                                                                                                                                                      |
|--------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 2026-10-22   | First draft: TV-style app in two phases, decisions D1 to D16, FR-1 to FR-17, NFR-1 to NFR-16, gap table                                                                                                     |
| 2026-10-23   | Tailwind CSS v4 and shadcn/ui replace plain CSS (D2, D13, D15, D16). Added D17 (Docker for dev, test, deploy), D18 (essential tests only), NFR-17. Updated FR-1, NFR-3, NFR-6, NFR-14, NFR-15 and the risks |
| 2026-10-23   | Phase 1 built. D1 notes the forwarded-proto header, D17 moves the dev container to :5174, findings added (9a), §11 statuses updated |
