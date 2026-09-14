# Claude Code Configuration - Alcidion

Root-level configuration for multi-workspace project.

## Structure

```
.claude/
├── settings.json      # Workspace and MCP configuration
├── README.md          # This file
└── agents.json        # (Optional) Custom agent definitions
```

## Settings

`settings.json` configures:
- **Workspaces:** Maps frontend and backend with paths and descriptions
- **Skills:** Paths to skill directories (root + backend-scoped)
- **MCP:** Model Context Protocol servers (codebase-memory indexing)
- **Model:** Default Claude model (currently Opus 5)

## Multi-Workspace Setup

When you start Claude Code at the Alcidion root, it has context for both projects:

### Backend Focus
```
cd backend
# Claude now emphasizes backend skills and context
# Can still reference frontend code/architecture
```

### Frontend Focus
```
cd frontend
# Claude now emphasizes frontend context
# Can still reference backend API contracts
```

### Root-Level Work
```
cd alcidion  (root)
# Both workspaces available
# Use for coordination, architecture, deployment
```

## How Indexing Works

- `codebase-memory` MCP server indexes both frontend and backend
- Indexed incrementally — changes are detected and re-indexed
- Available to all agent tasks in this project

## Switching Workspaces

Claude Code respects the current directory:

```powershell
# Work on backend
cd alcidion/backend
# Skills, docs, tests defaulted to backend

# Switch to frontend
cd alcidion/frontend
# Skills, docs, tests defaulted to frontend

# Back to root
cd alcidion
# Both visible, choose explicitly
```

## Skills & Documentation

Root-level docs:
- `CONTEXT.md` — Project overview, both systems
- `docs/adr/` — Architecture decisions
- `skills/` — Project-wide guidance

Backend-scoped:
- `backend/.claude/skills/` — 20+ domain skills
- `backend/docs/` — Backend-specific docs
- `backend/CONTEXT.md` — Backend architecture

Frontend-scoped:
- `frontend/.claude/` — Frontend configuration
- `frontend/README.md` — Frontend setup and dev

## Starting Work

**First time?** Read these in order:
1. `./CONTEXT.md` — Overall project shape
2. `backend/.claude/README.md` — Backend structure
3. `frontend/README.md` — Frontend structure

**Have a task?** Tell Claude what you're building and where (backend/frontend/both). It will navigate.

## Troubleshooting

**Skills not showing?**
- Verify paths in `settings.json`
- Skills must have `SKILL.md` in their directory
- Index may need refresh — check `./agents/README.md`

**Workspace not switching?**
- Ensure you `cd` into the subdirectory
- Claude respects filesystem location

**Missing context?**
- Start from root: `cd alcidion`
- Ask Claude to review the CONTEXT files
