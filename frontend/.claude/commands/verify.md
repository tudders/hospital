---
description: Lint, test, build and budget-check the frontend, then report honestly - including what only the browser can confirm
allowed-tools: Bash, Read, Grep, Glob
---

Verify the frontend and report what is actually true.

1. `git status --porcelain` - list what changed, so the report is scoped to it.
2. `npm run lint`
3. `npm run test` - `node --test`, and the redaction tests are the ones that matter most.
4. `npm run build` - type-check, bundle, then the gzip budget check. Report the budget table as
   printed: actual against budget, and the headroom left.

Then report, in this order:

- **Changed**: the files, one line each, what changed in them.
- **Result**: lint, tests (counts), build, budget numbers. State failures with their output.
- **Unverified**: what none of the above can prove - whether the screen looks right, whether an effect
  double-fires under StrictMode, whether a recorded event carries what you expect, anything that needs
  the API running. Say it plainly rather than implying the change is proven.
- **Next**: the single most useful next step.

$ARGUMENTS can narrow the run (a script name, or a test file). With no arguments, run all four steps.

If UI changed, finish by saying exactly what to open and look at, or open it: `npm run dev`, then the
page and the interaction that exercises the change. A green build is not a working screen, and UI work
is not done until it has been seen in the browser.

Do not weaken, skip or delete a test, and do not raise a budget to make this pass. If a test or a
budget is wrong, say so and leave it failing.
