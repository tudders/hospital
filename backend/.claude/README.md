# Backend Claude Configuration

Backend-scoped configuration and skills.

## Structure

```
backend/.claude/
├── README.md                  # This file
├── skills/                    # Backend-specific skills
│   ├── dotnet-webapi/        # API endpoints & HTTP semantics
│   ├── optimizing-ef-core-queries/
│   ├── bounded-contexts-and-events/
│   ├── domain-modeling/
│   ├── test-gap-analysis/
│   └── 20+ other skills
└── (settings.json inherited from root)
```

## Available Skills

### API & Web
- `dotnet-webapi` — ASP.NET Core endpoints, HTTP semantics, OpenAPI
- `configuring-opentelemetry-dotnet` — Tracing & metrics setup
- `correlation-and-audit` — Request correlation, audit logs

### Data Access
- `optimizing-ef-core-queries` — EF Core performance, N+1 prevention
- `detect-static-dependencies` — Find hard-to-test static calls

### Domain Design
- `domain-modeling` — Build domain vocabulary, aggregates, values
- `bounded-contexts-and-events` — Domain events, read models, context boundaries
- `invariants-at-the-write` — Unique constraints, state transitions

### Testing
- `test-gap-analysis` — Coverage gaps, survival tests
- `test-anti-patterns` — Common test failures
- `assertion-quality` — Strengthen assertions
- `tdd-and-verification` — Test-driven development

### Debugging & Optimization
- `diagnosing-bugs` — Hard-bug diagnosis loop
- `analyzing-dotnet-performance` — 50+ performance anti-patterns
- `coverage-analysis` — Coverage metrics and interpretation

### Workflow
- `resolving-merge-conflicts` — Git conflict resolution
- `writing-for-agents` — Writing code that agents can understand
- `grilling` — Stress-test your decisions

## Using Skills

Skills are auto-discovered. Invoke one at the right moment:

When adding an API endpoint → use /dotnet-webapi skill
When optimizing a slow query → use /optimizing-ef-core-queries skill
When writing tests → use /test-gap-analysis or /assertion-quality skill

## Key Documentation

Start here:
1. CONTEXT.md — Backend architecture and structure
2. docs/adr/0003-occupancy-is-its-own-read-model.md — Why occupancy is a read model

Then for specific tasks:
- New API endpoint? → Read skills/dotnet-webapi/SKILL.md
- Slow database query? → Read skills/optimizing-ef-core-queries/SKILL.md
- New bounded context? → Read skills/bounded-contexts-and-events/SKILL.md
- Test coverage gap? → Read skills/test-gap-analysis/SKILL.md

## Important Files

**Entry & Configuration:**
- src/Alcidion.Api/Program.cs — DI container, middleware
- src/Alcidion.Api/appsettings.json — Connection strings, logging
- Alcidion.sln — Solution file

**By Context:**
- **Patients:** src/Alcidion.Patients/
- **Admissions:** src/Alcidion.Admissions/
- **Hospital (Read Model):** src/Alcidion.Hospital/

**Tests:**
- tests/Alcidion.Api.Tests/ — Integration tests
- tests/Alcidion.Domain.Tests/ — Domain unit tests
- tests/Alcidion.Sql.Tests/ — SQL-specific tests

## Working from Root vs. Backend Dir

From root (alcidion/):
- Both frontend and backend context available
- Skills include both ./skills and backend/.claude/skills

From backend (alcidion/backend/):
- Backend-focused context
- Skills default to backend/.claude/skills
- Faster, more focused

## Troubleshooting

**Skill not showing?**
- Verify it's in backend/.claude/skills/<skill>/SKILL.md
- Check that SKILL.md has valid frontmatter

**Missing domain context?**
- Read CONTEXT.md (this directory)
- Ask Claude to review docs/adr/ for past decisions

**Test questions?**
- See tests/ subdirectories for examples
- Use /test-gap-analysis skill for coverage guidance
