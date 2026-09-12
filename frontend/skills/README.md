# Skills (frontend)

Canonical, tool-agnostic skills for this repo. One directory per skill, each a `SKILL.md` with
frontmatter (`name`, `description`) and a body short enough to be read in full.

| Skill | Covers |
|---|---|
| `telemetry-redaction` | What may never leave the browser, how an element is described, where the rule is enforced and tested |
| `effects-and-strictmode` | Render/commit/effect lifecycle, idempotent effects with cleanup, the two duplicate-event bugs StrictMode caught |
| `bundle-budget` | The gzip budgets enforced on every build, how to find what grew, when a budget may be raised |
| `correlation-ids` | Per-request correlation id, session id and sequence numbers, joining a UI error to backend logs |
| `design-system` | Tokens and primitives in `src/index.css`, and the `data-track` / `data-region` names the recorder depends on |

Each one states a rule the code already follows and names the file that enforces it, so a skill that
drifts from the code is a bug in the skill.

## Vendored skills

`skills/vendor/` holds skills written elsewhere, copied verbatim at a pinned commit and listed in
`skills/vendor/vendored.json`. They are tracked, never edited here, and refreshed with
`node skills/vendor/update.mjs`. See `vendor/README.md` for what is in there, what was deliberately
left out, and how to bump a pin.

The two kinds are not equal. A skill in this directory states a rule *this codebase already follows*
and names the file that enforces it. A vendored skill is general advice from another repo. Where they
disagree, the first-party skill wins, and the sync hook gives it the name.

## How they are consumed

This directory is the single source of truth. `.claude/hooks/sync-skills.mjs` mirrors it, on session
start, into the locations agents actually read - flattening `skills/` and `skills/vendor/` together,
because agents load skills from one flat directory:

```
skills/                -> .claude/skills/     Claude Code loads project skills from here
skills/vendor/         -> .agents/skills/     vendor-neutral location other agents read
```

Both mirrors are gitignored. Edit `skills/`, never a mirror; the next session start overwrites them.
To sync by hand:

```
node .claude/hooks/sync-skills.mjs
```

## Adding a skill

1. `skills/<kebab-name>/SKILL.md` with `name` matching the directory. A skill written here,
   not a copy of someone else's - those go through `skills/vendor/vendored.json`.
2. Write `description` as the trigger - when an agent should reach for it, in the words a task would
   use ("Use when adding a recorded event...").
3. Body: the rule, the wrong version, the right version, and the file that enforces it. No history, no
   restating the README.
4. Run the sync script, then `/skills` in Claude Code to confirm it loaded.
