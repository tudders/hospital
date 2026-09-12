---
description: Build, test and report the backend honestly - what passed, what failed, what is still unverified
allowed-tools: Bash, Read, Grep, Glob
---

Verify the backend and report what is actually true.

1. `git status --porcelain` - list what changed, so the report is scoped to it.
2. `dotnet build --nologo` - stop here if it fails: show the first error, its file:line, and the
   cause, not a guess.
3. `dotnet test --nologo` - show the pass/fail counts per test project. For any failure: the test
   name, the assertion, and the reason it failed.
4. `dotnet format Alcidion.sln --verify-no-changes` on the changed files only
   (`--include <file> ...`), skipping this if nothing is changed.

Then report, in this order:

- **Changed**: the files, one line each, what changed in them.
- **Result**: build, tests (counts), format. State failures with their output.
- **Unverified**: anything the suite does not cover - an endpoint not exercised over HTTP, a logging
  or tracing change with no assertion that the output renders, behaviour only reachable with a real
  database. Say it plainly rather than implying the change is proven.
- **Next**: the single most useful next step.

$ARGUMENTS can narrow the run (a `--filter` expression, or a project path). With no arguments, verify
everything.

Do not weaken, skip or delete a test to make this pass. If a test is wrong, say that it is wrong and
why, and leave it failing.
