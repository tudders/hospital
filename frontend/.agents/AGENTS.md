# AGENTS.md - Alcidion frontend

React 19 + TypeScript + Vite. No runtime dependency beyond React. Talks to the ASP.NET Core API in
`../backend` (default `http://localhost:5025`, override with `VITE_API_URL`).

Vendor-neutral instructions: any coding agent reads this file. Claude Code additionally loads
`.claude/` (settings, hooks, subagents, commands) and the skills mirrored into `.claude/skills/`.

## Commands

```
npm install
npm run dev       # http://localhost:5173 (5174 if taken; both are CORS-allowed by the API)
npm run test      # node --test, ~1s, no extra dependencies
npm run lint      # oxlint
npm run build     # tsc -b, vite build, then the gzip budget check - fails the build if over
npm run size      # the budget check alone, against dist/
```

## Layout

```
src/lib/telemetry.ts         session id, sequence numbers, per-request correlation ids, batched queue
src/lib/session-recorder.ts  what is captured: clicks, form changes, submits, keys, scroll, resize, visibility, errors
src/lib/redaction.ts         what may be said about an element or a value; unit-tested without a DOM
src/lib/api.ts               fetch wrapper: bearer token, X-Correlation-Id, problem details, api.request events
src/lib/auth.ts              token storage and role helpers
src/components/              Login, Patients, Admissions, ErrorAlert
src/index.css                design tokens and primitives (card, btn, table, badge, alert)
scripts/check-bundle-size.mjs  the performance budget
```

## Rules that are not negotiable

1. **Content never leaves the browser.** This is a clinical app: element text, field values and option
   labels are patient data. An element is described by role, author-given name and region; a typed
   value is reduced to its length; character keystrokes are not recorded at all. Only
   `src/lib/redaction.ts` decides how something is described, and its tests are the gate.
2. **Effects are idempotent and clean up.** StrictMode stays on. An effect that emits an event keys it
   to the thing it describes, not to the component's mounting - that is what the `session.start` and
   `page.view` bugs were.
3. **Every API call goes through `api()`** in `src/lib/api.ts`, so it carries the bearer token and a
   fresh `X-Correlation-Id`, parses problem details, and records an `api.request` event.
4. **An error shown to the user shows its correlation id.** That is what makes it findable in the
   backend logs.
5. **No new runtime dependency** without raising it first: the budget is 90 kB gzip of JS and the app
   sits near 70 kB, nearly all React. Dev-only tools go in `--save-dev`.
6. **No raw colours or pixel gaps in components.** Use the tokens in `src/index.css`; add a token if
   one is missing.
7. **Name your controls.** `data-track` on anything clickable, `data-region` on section wrappers,
   `aria-label` or `name` on inputs. An unnamed control is a hole in the session timeline.
8. **No `console.log` of events, values or response bodies** - same leak as recording them.
9. **Never modify `.env`**; `.env.example` documents the variables.

## Verification before reporting done

`npm run lint`, `npm run test` and `npm run build` green, with the output seen. For anything that
changes the UI, open it in the browser (`npm run dev`) and look at it - a green build is not a working
screen.

## Background

`../docs/lifecycle-and-hooks.md` section 2 walks a session through the UI: the React lifecycle, the
two StrictMode bugs, and the split between recording, redaction and transport.

## Skills

`skills/` holds the detailed playbooks (redaction, effects and StrictMode, bundle budget, correlation
ids, design system). `skills/vendor/` holds third-party skills copied verbatim at a pinned commit -
Vercel's React performance rules, design and diagnosis flows from `mattpocock/skills` - refreshed with
`node skills/vendor/update.mjs`, never edited in place. The Vercel rules cover React and Next.js; the
`server-*` set assumes RSC and does not apply to this Vite app (see `skills/vendor/README.md`). Where
a vendored skill and a first-party one disagree, the first-party one wins: it describes this code.
`.agents/skills/` is a generated mirror of both for agents that read this directory; it is gitignored.
Edit `skills/` and run `node .claude/hooks/sync-skills.mjs`.
