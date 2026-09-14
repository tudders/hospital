# Alcidion - Hospital Occupancy & Patient Management

Multi-workspace project spanning frontend and backend systems.

## Architecture

### Backend (`./backend`)
- **Framework:** ASP.NET Core 9.0 Web API
- **Database:** SQL Server with EF Core migrations
- **Key Domains:** Admissions, Patients, Hospital Occupancy (read model)
- **Entry Point:** `backend/src/Alcidion.Api/Program.cs`
- **Port:** 5025 (Development)

### Frontend (`./frontend`)
- **Framework:** React (check frontend README for details)
- **Features:** Patient forms, hospital occupancy dashboard, patient modals
- **API Integration:** Communicates with backend API on port 5025

## Working with the Project

### Starting Claude Code
When you invoke Claude Code at the alcidion root, it has context for both frontend and backend.

**For backend work:**
```
cd alcidion/backend
```
Available skills in `backend/.claude/skills/`:
- `dotnet-webapi` — ASP.NET Core endpoints, HTTP semantics, OpenAPI
- `optimizing-ef-core-queries` — EF Core performance tuning
- `bounded-contexts-and-events` — Domain-driven design patterns
- And 20+ domain-specific skills

**For frontend work:**
```
cd alcidion/frontend
```

## Running the Systems

### Backend
```powershell
cd backend/src/Alcidion.Api
dotnet run
# Listens on http://localhost:5025
```

### Frontend
```
cd frontend
npm start
# Check frontend/README.md for details
```

## Key Files & Decisions

### ADRs
- `docs/adr/0003-occupancy-is-its-own-read-model.md` — Why occupancy is a read model, not a query

### Tests
- Backend: `backend/tests/` (xUnit, integration tests with SQL Server)
- Frontend: `frontend/` (check frontend test setup)

## Current Status
Branch: `realtimehosptital` — EF Core migration pass in progress
- Checkpoint 6: EF Core migrations
- Previous: Realtime updates, patient forms, API validation refactoring

## Workspace Skills
Both `./skills/` and `backend/.claude/skills/` are indexed and available project-wide.
