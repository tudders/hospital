# Backend Architecture - Alcidion

ASP.NET Core 10.0 Web API with domain-driven design, EF Core, and SQL Server.

## Solution Structure

```
backend/
├── Alcidion.sln
├── src/
│   ├── Alcidion.Api/                  # Web API entry point
│   │   ├── Program.cs                 # DI & middleware setup
│   │   ├── Controllers/               # HTTP endpoints
│   │   └── Configuration/
│   ├── Alcidion.Api.Contracts/        # DTO shapes for clients
│   ├── Alcidion.Patients/             # Patient bounded context
│   ├── Alcidion.Admissions/           # Admissions bounded context
│   ├── Alcidion.Hospital/             # Hospital occupancy read model
│   └── Alcidion.Domain/               # Shared domain logic
├── tests/
│   ├── Alcidion.Api.Tests/            # Integration tests
│   ├── Alcidion.Domain.Tests/
│   └── Alcidion.Sql.Tests/
└── docs/
    └── adr/                           # Architecture decision records
```

## Bounded Contexts

### Patients
**Domain:** Patient registration, demographics, contact information.  
**Aggregates:** `Patient` (root), `Contact`  
**Entry Point:** `src/Alcidion.Patients/`  
**Tests:** `tests/Alcidion.Domain.Tests/`

### Admissions
**Domain:** Hospital admissions, discharge, length of stay.  
**Aggregates:** `Admission` (root)  
**Entry Point:** `src/Alcidion.Admissions/`  
**Dependencies:** Patients context (via read model)  
**Tests:** `tests/Alcidion.Api.Tests/`

### Hospital (Read Model)
**Purpose:** Real-time occupancy aggregation for dashboards.  
**Source:** Domain events from Admissions  
**Query:** `IHospitalReader`  
**Materialization:** SQL Server table + in-memory cache  
**Entry Point:** `src/Alcidion.Hospital/`  
**See:** `docs/adr/0003-occupancy-is-its-own-read-model.md`

## Key Patterns

### Domain Events
- Published by aggregates on state changes
- Consumed by:
  - Read models (e.g., Hospital occupancy)
  - Integration notifications (future: message broker)
- Handler: `IDomainEventHandler<T>`

### Repository Pattern
- One repository per aggregate (e.g., `IPatientRepository`)
- Implements persistence + domain event publishing
- Currently: EF Core with SQL Server
- Future: Swappable for messaging (events as first-class)

### Read Models
- `Hospital` read model precalculates occupancy from Admissions
- Not a queryable domain aggregate
- Updated via domain event handlers
- See ADR 0003 for rationale

## EF Core Migration Strategy

**Current:** Checkpoint 6 - Migration pass 1  
- Moving from inline data access to EF Core `DbContext`
- New projects: `Alcidion.Hospital` (read model persistence)
- Migrations tracked in `backend/src/Alcidion.Admissions/Migrations/`

**Data Access Path:**
```
API Controller
  ↓
Application Service / Repository
  ↓
DbContext (EF Core)
  ↓
SQL Server
```

## Running the Backend

### Development
```powershell
cd backend/src/Alcidion.Api
dotnet run
# Listens on http://localhost:5025
# Launch settings: Properties/launchSettings.json
```

### Database
- Connection string: `appsettings.json` or environment
- SQL Server required (LocalDB, Express, or full)
- Migrations applied via: `dotnet ef database update`

### Tests
```powershell
cd backend
dotnet test
# Or by project: dotnet test tests/Alcidion.Api.Tests
```

## API Contract

Base URL: `http://localhost:5025`

### Occupancy Endpoints
- `GET /hospital/occupancy` — Current occupancy snapshot
- Consumed by frontend dashboard

### Patient Endpoints
- `POST /patients` — Register new patient
- `GET /patients/{id}` — Retrieve patient
- `PUT /patients/{id}` — Update patient

### Admission Endpoints
- `POST /admissions` — Admit patient
- `GET /admissions/{id}` — Retrieve admission
- `POST /admissions/{id}/discharge` — Discharge patient

See `Alcidion.Api.Contracts/` for response DTOs.

## Testing Strategy

### Unit Tests (Domain.Tests)
- Domain logic, aggregates, value objects
- No database, no HTTP
- Focus: business rules validation

### Integration Tests (Api.Tests)
- Repository + database behavior
- End-to-end flows
- Against in-memory or test SQL Server

### SQL-Specific Tests (Sql.Tests)
- Query performance, schema integrity
- Read model correctness

## Important Files

- `Program.cs` — DI setup, middleware, health checks
- `HospitalConnection.cs` — Database configuration and pooling
- `EfAdmissionRepository.cs` — EF Core persistence + event publishing
- `HospitalOccupancyController.cs` — GET /hospital/occupancy

## Dependencies

Key NuGet packages:
- `Microsoft.EntityFrameworkCore` 10.0.12
- `Microsoft.Data.SqlClient` 6.1.7
- `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12
- `OpenTelemetry.*` 1.18.0 — Distributed tracing & metrics

## Next Steps

- [ ] Complete EF Core migration for all repositories
- [ ] Swap in-memory Hospital read model for persistent EF table
- [ ] Add message broker for cross-service events (optional)
- [ ] Implement saga for multi-step workflows (if needed)

## Questions?

See skills in `backend/.claude/skills/` for guidance on:
- Adding new endpoints → `dotnet-webapi`
- Optimizing EF Core queries → `optimizing-ef-core-queries`
- Domain modeling → `domain-modeling`
- Bounded context refactoring → `bounded-contexts-and-events`
