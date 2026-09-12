# Alcidion frontend

React 19 + TypeScript + Vite. No runtime dependencies beyond React.

## Run

```
npm install
npm run dev       # http://localhost:5173 (5174 if taken; both are CORS-allowed by the API)
npm run build     # type-check, bundle, then enforce the size budget
npm run test      # node --test, no extra dependencies
npm run lint
npm run size      # gzip budget check against dist/
```

Set `VITE_API_URL` (see `.env.example`) if the API is not on `http://localhost:5025`.

## Layout

```
src/lib/telemetry.ts        session id, sequence numbers, per-request correlation ids, batched queue -> POST /api/telemetry/events
src/lib/session-recorder.ts captures clicks, form changes, submits, keys, scroll, resize, visibility and errors
src/lib/redaction.ts        how an element and a field value may be described; unit-tested, no DOM needed
src/lib/api.ts              fetch wrapper: bearer token, X-Correlation-Id, problem-details errors, api.request events
src/lib/auth.ts             token storage + role helpers
src/components/             Login, Patients, Admissions, ErrorAlert
src/index.css               design tokens + primitives (card, btn, table, badge, alert)
```

## Observability

Every API call gets a fresh correlation id. The backend echoes it and stamps its logs and trace span with it. The frontend records an `api.request` event carrying that id plus the session id, so any error shown in the UI (the alert prints the correlation id) can be found in backend logs and joined back to the session's event stream.

## Session recording

`startSessionRecording()` records an ordered timeline of what the user did: clicks, form changes,
submits, non-character keys, scroll, resize, visibility changes, navigation and uncaught errors. Each
event carries the session id, a sequence number and a millisecond offset from session start, so the
session replays in the order it happened even if a batch arrives late.

It never records content. This is a clinical app, so element text, field values and option labels are
patient data: an element is described by its role, its `data-track`/`aria-label`/`name` and its
`data-region`, and a typed value is reduced to its length. Character keystrokes are not recorded at
all, since a keystroke stream would reconstruct the field. DOM mutation capture is deliberately out
of scope.

## Performance budget

`scripts/check-bundle-size.mjs` runs on every build and fails it if gzip output exceeds the budget
(JS 90 kB, CSS 8 kB, HTML 2 kB). Current: about 70 kB gzip of JS, most of it React 19. Raise a budget
deliberately, with a reason, rather than to make a build pass.

## Agent tooling

```
skills/        five playbooks, tool-agnostic: telemetry redaction, effects and StrictMode,
               bundle budget, correlation ids, design system
.agents/       AGENTS.md - the rules, commands and layout any coding agent should read first
.claude/       Claude Code config: settings, hooks, two audit subagents, /verify /session /component
```

`skills/` is the single tracked copy; `.claude/hooks/sync-skills.mjs` mirrors it into
`.claude/skills/` and `.agents/skills/` on session start, and both mirrors are gitignored. Hooks are
Node scripts: they guard `.env` and history-rewriting git commands, ask before a runtime dependency or
a budget change, oxlint and leak-check every written file, and run lint, tests and the build before a
session turn can end when `src/` changed. See `.claude/README.md`.
