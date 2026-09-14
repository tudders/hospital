# Alcidion Agents

Multi-workspace coordination and specialized task agents for Alcidion.

## Architecture

The Alcidion project spans:
- **Backend** — ASP.NET Core 9.0 API with domain-driven design
- **Frontend** — React UI for hospital occupancy and patient management

Agents here coordinate across these boundaries and handle specialized tasks.

## Available Agents

### General Purpose
- **Backend Agent** — Start here for backend/API work
  ```
  cd backend && /agent backend-expert
  ```
- **Frontend Agent** — Start here for UI/React work
  ```
  cd frontend && /agent frontend-expert
  ```

### Cross-Workspace
- **Feature Integration** — When work spans both frontend and backend
- **Migration Lead** — For data migrations, schema changes, breaking API changes

## Using Agents

### From Root (Alcidion)
Agents inherit both workspace contexts:
```
claude /agent <agent-name> <task>
```

### From Subdirectory
Agent defaults to that workspace:
```
cd backend
claude /agent dotnet-expert "add endpoint for patient discharge"
```

## Agent Configuration

Agents are configured in `.claude/agents.json` and `.claude/settings.json`.

Each agent has:
- **Context files** — what docs/code it reads on startup
- **Workspace** — backend, frontend, or both
- **Skills** — which skill directories it can access
- **Constraints** — what it can/cannot modify

## Creating a New Agent

1. Define in `.claude/agents.json`:
   ```json
   {
     "name": "your-agent",
     "description": "What it does",
     "workspace": "backend|frontend|both",
     "skills": ["skill-name"],
     "read_only": ["path/to/docs"]
   }
   ```

2. Document usage in this README

## Best Practices

- **Narrow workspace:** Use agents scoped to one project when possible
- **Explicit scope:** Tell the agent what you want, not just "improve"
- **Handoff:** When work switches to the other workspace, start a new agent session
