# .claude (frontend)

Claude Code configuration for this repo. Everything here is tracked except the generated skills mirror
and local overrides.

```
settings.json              permissions and hooks
hooks/sync-skills.mjs      SessionStart: mirror skills/ + skills/vendor/ into .claude/skills and .agents/skills, print orientation
hooks/guard-sensitive.mjs  PreToolUse: deny credential-file edits and history-rewriting git; ask about runtime deps and budget changes
hooks/lint-touched.mjs     PostToolUse: oxlint the written file, plus the redaction and lifecycle greps
hooks/verify.mjs           Stop: lint, test and build if src/ or scripts/ changed this session
agents/redaction-auditor.md    subagent: could anything reconstruct a patient identifier?
agents/performance-auditor.md  subagent: bundle size, render cost, observability overhead
commands/verify.md         /verify - lint, test, build, budget, and an honest report of what is unverified
commands/session.md        /session - drive the app and read back the recorded timeline
commands/component.md      /component - add a component with the lifecycle, naming and token rules applied
skills/                    generated mirror of ../skills and ../skills/vendor, flattened (gitignored)
```

Hooks are Node scripts (`node .claude/hooks/*.mjs`): Node is already this repo's toolchain, it parses
the hook payload from stdin without a JSON tool, and it behaves the same on Windows and Linux, which
`bash` on this machine does not - `C:\Windows\System32\bash.exe` is WSL.

## The hooks in one line each

- **sync-skills** keeps one tracked copy of every skill. Edit `skills/`, never a mirror.
  Third-party skills live in `skills/vendor/`, pinned to an upstream commit and refreshed with
  `node skills/vendor/update.mjs`; they are mirrored alongside the first-party ones, which win a
  name collision.
- **guard-sensitive** is the backstop for bypass-permissions mode. Denies: writes to `.env` or
  `.npmrc`, push to main, force push, `reset --hard`, `clean -f`, `branch -D`. Asks: `npm install` of a
  runtime dependency (it ships to the browser), and edits to `BUDGETS_KB` in the size check.
- **lint-touched** runs oxlint on the file and greps for this app's specific leaks: reading
  `textContent`, recording a field value, `console.log` in `src/`, a bare `fetch` outside `lib/api.ts`,
  an `async useEffect`, a raw hex colour in a component, a printable key added to
  `KEYS_WORTH_RECORDING`. Advisory - it never blocks an edit.
- **verify** runs lint, tests and the build (which enforces the gzip budget) on Stop when `src/` or
  `scripts/` changed, and blocks the turn ending on a failure. Skip it with `touch .claude/verify-skip`
  or `ALCIDION_SKIP_VERIFY=1` - the marker is gitignored and should be deleted once green.

What no hook can check is whether the screen is right. UI work is not done until it has been opened in
the browser.

## Trust the workspace once

`permissions.allow` is ignored until the workspace is trusted: start Claude Code interactively here
once and accept the trust dialog, or set
`projects["C:/Users/sam/Desktop/DEV/alcidion/frontend"].hasTrustDialogAccepted: true` in
`~/.claude.json`. Until then every allowlisted command still prompts (and `claude -p` prints the
warning). Hooks and skills load either way.

## Local overrides

`settings.local.json` is gitignored; put machine-specific permissions there rather than widening
`settings.json`.
