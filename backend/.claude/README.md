# .claude (backend)

Claude Code configuration for this repo. Everything here is tracked except the generated skills
mirror and local overrides.

```
settings.json              permissions and hooks
hooks/sync-skills.mjs      SessionStart: mirror skills/ + skills/vendor/ into .claude/skills and .agents/skills, print orientation
hooks/guard-sensitive.mjs  PreToolUse: refuse edits to credential files and history-rewriting git commands
hooks/check-touched.mjs    PostToolUse: grep the written .cs file for this repo's known defect classes
hooks/verify.mjs           Stop: run dotnet test if C# changed this session
agents/invariant-auditor.md     subagent: races, lock scope, normalization, DI lifetimes, context boundaries
agents/api-surface-reviewer.md  subagent: status codes, auth coverage, audit, correlation, response shape
commands/verify.md         /verify - build, test, format, and an honest report of what is unverified
commands/slice.md          /slice - add a use case test-first, through every layer
commands/trace.md          /trace - follow a correlation id from the browser to the audit line
skills/                    generated mirror of ../skills and ../skills/vendor, flattened (gitignored)
```

Hooks are Node scripts (`node .claude/hooks/*.mjs`): Node is already required by the frontend repo,
it parses the hook payload from stdin without a JSON tool, and it behaves the same on Windows and
Linux, which `bash` on this machine does not - `C:\Windows\System32\bash.exe` is WSL. If Node is
missing, the hooks fail quietly and nothing else breaks.

## The hooks in one line each

- **sync-skills** keeps one tracked copy of every skill. Edit `skills/`, never a mirror.
  Third-party skills live in `skills/vendor/`, pinned to an upstream commit and refreshed with
  `node skills/vendor/update.mjs`; they are mirrored alongside the first-party ones, which win a
  name collision.
- **guard-sensitive** is the backstop for bypass-permissions mode: no writes to `.env`,
  `appsettings.Production.json`, `*.pfx`, `*.pubxml`; no push to main, force push, `reset --hard`,
  `clean -f` or `branch -D`. It denies and asks you to decide.
- **check-touched** is advisory: `DateTime.Now` instead of `IClock`, `async void`, blocking on a Task,
  `Task.Run`/`Thread.Sleep` in a concurrency test, a non-`TryAdd` insert, a bare `StatusCode()` in a
  controller, Admissions referencing Patients. It never blocks an edit.
- **verify** runs `dotnet test` on Stop when `.cs` or `.csproj` changed, and blocks the turn ending on
  a red suite. A red suite is legitimate mid-TDD, so it can be skipped with
  `touch .claude/verify-skip` or `ALCIDION_SKIP_VERIFY=1` - the marker file is gitignored and should
  be deleted once the suite is green again.

## Trust the workspace once

`permissions.allow` is ignored until the workspace is trusted: start Claude Code interactively here
once and accept the trust dialog, or set
`projects["C:/Users/sam/Desktop/DEV/alcidion/backend"].hasTrustDialogAccepted: true` in
`~/.claude.json`. Until then every allowlisted command still prompts (and `claude -p` prints the
warning). Hooks and skills load either way.

## Local overrides

`settings.local.json` is gitignored; put machine-specific permissions there rather than widening
`settings.json`.
