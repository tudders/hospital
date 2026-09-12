# Vendored skills (frontend)

Skills written by someone else, copied here verbatim at a pinned commit. They are tracked so a
checkout is self-contained and a bump is reviewable as a diff, but this repo is only a consumer:
upstream owns the content.

**Never edit a directory in here.** The next `update.mjs` run overwrites it, and a local edit is
invisible to anyone reading the upstream repo. If a vendored skill is wrong for this codebase, say so
in a first-party skill in `skills/` - that is the one agents should believe when the two disagree.

## What is here and why

| Skill | From | Why it earns its place |
|---|---|---|
| `vercel-react-best-practices` | vercel | 70 indexed rules on re-renders, effects, bundle size and data fetching |
| `codebase-design` | pocock | Deep-module vocabulary for arguing about where a seam goes |
| `diagnosing-bugs` | pocock | Root cause before fixes, which is the house rule anyway |
| `domain-modeling` | pocock | Terminology and ADRs, so the UI and the API use the same words |
| `research` | pocock | Gathering primary-source facts into a Markdown file instead of guessing |
| `resolving-merge-conflicts` | pocock | Conflict resolution that reasons about both intents |
| `grilling` | pocock | Stress-testing a plan before it becomes code |
| `handoff` | pocock | Compacting a session for the next agent |
| `writing-for-agents` | pocock | We author skills and AGENTS.md here; this is how to write them |

### Reading `vercel-react-best-practices` in a Vite app

It is a React **and Next.js** guide. This app is React 19 on Vite with no server runtime, so:

- `server-*` rules assume React Server Components and Next's request lifecycle. Not applicable.
- `bundle-dynamic-imports`, `bundle-preload`, `bundle-barrel-imports`, `bundle-analyzable-paths`
  are about bundler behaviour: the ideas hold, but the mechanism is Rollup and `import()`, not
  `next/dynamic`. `skills/bundle-budget` is the rule that actually gates a build here.
- `async-api-routes` and `rendering-resource-hints` describe Next primitives we do not have.
- Everything under `rerender-`, `js-`, `client-`, `advanced-`, and the rest of `rendering-` applies
  directly, and `rerender-derived-state-no-effect` and `rerender-move-effect-to-event` say the same
  thing as `skills/effects-and-strictmode` from a performance angle.

Where it and a first-party skill disagree, the first-party skill wins: it describes code that exists.

From `mattpocock/skills`, the issue-tracker workflows (`triage`, `to-spec`, `to-tickets`,
`wayfinder`) need a tracker configured by `setup-matt-pocock-skills` first, and `tdd` and
`code-review` would compete with the repo's own verify and review flow.

## Updating

`vendored.json` pins one commit per upstream repo. To take a newer upstream, change that source's
`ref` and re-run; to add or drop a skill, edit the `skills` array and re-run.

```
node skills/vendor/update.mjs          # re-fetch every skill, overwriting the local copy
node skills/vendor/update.mjs --check  # report drift against upstream, write nothing (exit 1 if any)
```

The script refuses a skill whose upstream `SKILL.md` declares a `name` different from its directory
here, because the directory name is what the sync hook mirrors and what an agent loads it by.

## How these reach an agent

`.claude/hooks/sync-skills.mjs` flattens `skills/` and `skills/vendor/` together into
`.claude/skills/` and `.agents/skills/` on session start. A first-party skill wins a name collision.
