# Alcidion Skills

Domain-specific guidance and patterns for Alcidion development.

## Organization

### Root Skills (`./skills/`)
Project-wide guidance applicable to both frontend and backend:
- Architecture and design patterns
- Deployment and infrastructure
- Testing and CI/CD strategies
- Documentation standards

### Backend Skills (`./backend/.claude/skills/`)
ASP.NET Core, EF Core, and domain-driven design guidance:
- API endpoint patterns
- Database query optimization
- Domain modeling and bounded contexts
- Event-driven architecture

### Frontend Skills (`./frontend/.claude/skills/`)
React and UI patterns (if present):
- Component design
- State management
- Form handling
- Testing React components

## Using Skills

Skills are automatically discovered and indexed. When working in a specific workspace, scoped skills are prioritized:

```
cd backend
# Uses backend/.claude/skills first, then root ./skills/
```

## Adding New Skills

1. **For backend guidance:** Add to `backend/.claude/skills/<skill-name>/`
2. **For project-wide guidance:** Add to `./skills/<skill-name>/`

Structure:
```
<skill-name>/
├── SKILL.md          # Metadata and guidance
├── examples/         # Code examples
└── references/       # Supporting docs
```

See `backend/.claude/skills/dotnet-webapi/SKILL.md` for a complete example.

## Shared Skills

These are used across both frontend and backend:

- `dotnet-webapi` (backend) — Can inform REST API design philosophy
- `test-gap-analysis` (backend) — Principles apply to frontend tests too
- Architecture skills — Used for both systems

## Documenting Domain Knowledge

Critical project knowledge lives in:
- `./CONTEXT.md` — Project overview and architecture
- `./docs/adr/` — Architecture Decision Records
- `backend/docs/` — Backend-specific documentation
- `frontend/` — Frontend structure and setup

Skills supplement these docs with _how-to_ guidance, not architecture docs.
