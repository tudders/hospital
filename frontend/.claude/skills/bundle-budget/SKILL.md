---
name: bundle-budget
description: Use before adding a dependency, before adding an import to a shared module, when a build fails the size check, or when asked about frontend performance. Covers the gzip budgets, how to find what grew, and the rule for raising a budget.
---

# The budget is part of the build

`npm run build` runs `tsc -b`, then Vite, then `scripts/check-bundle-size.mjs`, which fails the build
when gzip output exceeds:

| Asset | Budget | Current |
|---|---|---|
| JS | 90 kB gzip | ~70 kB, most of it React 19 |
| CSS | 8 kB gzip | ~1.1 kB |
| HTML | 2 kB gzip | ~0.3 kB |

Gzip, because that is what crosses the wire, onto hospital hardware and hospital networks.

```
npm run build      # type-check, bundle, then enforce the budget
npm run size       # the check alone, against the existing dist/
```

## Before adding a dependency

The app currently has **no runtime dependency except React**. That is a feature of the demo, not an
accident. Before adding one:

1. Can it be fifteen lines of local code? `src/lib/` is full of examples - telemetry, redaction, the
   fetch wrapper, auth - each written rather than installed.
2. What does it cost gzipped, and is the remaining headroom (~20 kB) worth spending on it?
3. Does it pull a transitive tree, a polyfill, or a CSS file?
4. Is it dev-only? Then `--save-dev`, and it never reaches the bundle.

A new runtime dependency is a decision to raise with Sam, not a detail to slip into a commit.

## When the check fails

1. Read the per-file table the check prints - it is sorted by gzip size, largest first.
2. Compare against the previous build to see which chunk moved.
3. Look for the usual causes: a barrel import that pulls a whole library, an icon set imported whole,
   a polyfill, a dev-only helper imported from production code, an accidental second copy of a
   dependency.
4. Prefer deleting the cost to raising the ceiling: a narrower import, a dynamic `import()` for
   something off the critical path, or dropping the feature.

## Raising a budget

A budget is raised deliberately, with the reason, in the same commit as the thing that needed it, and
never to make a build pass. Record what the new number buys. `BUDGETS_KB` in
`scripts/check-bundle-size.mjs` is the single place it is written.

## Other performance rules in this app

- No runtime dependency that duplicates a platform API (`fetch`, `crypto.randomUUID`,
  `sendBeacon`, `performance.now` are all used directly).
- Telemetry is batched (5 s interval, 50 per batch, 500 queue ceiling) and flushed on page hide with
  `sendBeacon`, so recording never blocks a click or holds the page open.
- Recorder listeners are passive and capture-phase; scroll and resize are throttled to 250 ms.
- The event queue has a ceiling so a backend outage cannot grow memory without bound.
