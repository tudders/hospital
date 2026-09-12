# .agents (frontend)

The vendor-neutral corner of the repo: instructions any coding agent can read, without knowing which
tool is running.

```
.agents/AGENTS.md     the rules, commands and layout - the file to read first
.agents/skills/       generated mirror of ../skills (gitignored)
```

## Why it exists alongside .claude/

| Directory | Read by | Tracked |
|---|---|---|
| `skills/` | nothing directly - it is the source of truth | yes |
| `.agents/AGENTS.md` | any agent that supports the AGENTS.md convention | yes |
| `.agents/skills/` | agents that read the vendor-neutral skills path; Claude Code imports from it | no, generated |
| `.claude/` | Claude Code: settings, hooks, subagents, slash commands | yes, except `skills/` |
| `.claude/skills/` | Claude Code's project skill loader | no, generated |

One copy of every skill is tracked, in `skills/`. The two mirrors are produced by
`.claude/hooks/sync-skills.mjs`, which runs on session start and can be run by hand:

```
node .claude/hooks/sync-skills.mjs
```

Editing a mirror is pointless - the next sync overwrites it. Edit `skills/`.

A tool that wants only the rules and not the skills needs `AGENTS.md` alone. If a tool expects
`AGENTS.md` at the repository root, symlink or copy it there; it is kept here so the root stays
readable.
