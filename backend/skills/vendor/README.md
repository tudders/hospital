# Vendored skills (backend)

Skills written by someone else, copied here verbatim at a pinned commit. They are tracked so a
checkout is self-contained and a bump is reviewable as a diff, but this repo is only a consumer:
upstream owns the content.

**Never edit a directory in here.** The next `update.mjs` run overwrites it, and a local edit is
invisible to anyone reading the upstream repo. If a vendored skill is wrong for this codebase, say so
in a first-party skill in `skills/` - that is the one agents should believe when the two disagree.

## What is here and why

| Skill | From | Why it earns its place |
|---|---|---|
| `dotnet-webapi` | dotnet | Controller, DI, configuration and hosting conventions for an ASP.NET Core Web API |
| `configuring-opentelemetry-dotnet` | dotnet | We already export OTel traces from `Program.cs`; this is the reference for changing that wiring |
| `assertion-quality` | dotnet | Whether an assertion actually pins the behaviour it claims to |
| `test-anti-patterns` | dotnet | Named failure modes to check a new test against |
| `test-smell-detection` | dotnet | Catalogue for reviewing an existing suite |
| `test-gap-analysis` | dotnet | Mutation-style reasoning about what the 39 tests do not cover |
| `coverage-analysis` | dotnet | Coverlet is already referenced; this turns its output into a judgement |
| `find-untested-sources` | dotnet | Finds production files with no test touching them |
| `detect-static-dependencies` | dotnet | Statics are what break the `IClock` rule; this finds them |
| `testability-obstacle` | dotnet | What to do when a class resists being tested, short of weakening the test |
| `analyzing-dotnet-performance` | dotnet | Allocation, async and LINQ patterns, for when an endpoint is slow |
| `codebase-design` | pocock | Deep-module vocabulary for arguing about where a seam goes |
| `diagnosing-bugs` | pocock | Root cause before fixes, which is the house rule anyway |
| `domain-modeling` | pocock | Terminology and ADRs - this repo is two bounded contexts, so the words matter |
| `research` | pocock | Gathering primary-source facts into a Markdown file instead of guessing |
| `resolving-merge-conflicts` | pocock | Conflict resolution that reasons about both intents |
| `grilling` | pocock | Stress-testing a plan before it becomes code |
| `handoff` | pocock | Compacting a session for the next agent |
| `writing-for-agents` | pocock | We author skills and AGENTS.md here; this is how to write them |

Deliberately not taken from `dotnet/skills`: Blazor, MAUI, MSBuild, template-engine, NuGet, test
migration, and the EF Core skills. This is an xUnit, no-EF, single-project-per-context Web API, and
100 skills of mostly-irrelevant triggers would drown the five that describe this codebase.

From `mattpocock/skills`, the issue-tracker workflows (`triage`, `to-spec`, `to-tickets`,
`wayfinder`) need a tracker configured by `setup-matt-pocock-skills` first, and `tdd` and
`code-review` would compete with `skills/tdd-and-verification` and the repo's own review flow.

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
