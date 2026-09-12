---
name: bounded-contexts-and-events
description: Use when adding a feature that spans Patients and Admissions, adding a domain event or handler, creating a new bounded context, or wiring services into DI. Explains the reference rule, the read-model pattern, and the lifetimes that keep the in-process bus swappable for a broker.
---

# Bounded contexts talk through events, not references

`Alcidion.Admissions` never references `Alcidion.Patients`. It references
`Alcidion.Patients.Contracts`, which holds only the events Patients publishes. Admissions learns
about patients by handling `PatientRegistered` and keeping its own read model
(`IKnownPatients`), so the module can be split into a separate service by replacing
`InMemoryEventBus` with a broker and changing nothing else.

```
Alcidion.Shared               IDomainEvent, IEventBus (Mediator), IEventHandler<T> (Observer), Result<T>, IClock
Alcidion.Patients.Contracts   the only thing another context may reference
Alcidion.Patients             Patient aggregate, IPatientRepository, PatientService
Alcidion.Admissions           Admission aggregate, IKnownPatients read model, AdmissionService
Alcidion.Api                  controllers, JWT auth, correlation middleware, OpenTelemetry, [Audited]
```

## Rules

- A context may reference `Alcidion.Shared` and another context's `*.Contracts`. Nothing else.
- If you need data from another context, handle its event and keep what you need locally. Do not add
  a project reference, and do not call across with an interface that hides one.
- Events are published **after** the write succeeds, by the application service, never by the
  controller and never by the aggregate.
- An event is a fact in the past tense: `PatientRegistered`, `PatientDischarged`. It carries ids and
  the values a subscriber cannot derive, nothing more.
- A handler must tolerate being called again. The bus is in-process today; a broker delivers at least
  once.

## Adding a cross-context feature

1. Add the event record to the publishing context's `*.Contracts` project.
2. Publish it from the application service, after the successful write, with `IClock.UtcNow`.
3. Implement `IEventHandler<TEvent>` in the consuming context and register it in that context's
   `ServiceCollectionExtensions`.
4. Update the consuming context's read model, not the publisher's storage.
5. Cover it in `CrossDomainEventFlowTests`, which builds the real DI wiring rather than mocks.

## Wiring and lifetimes

Each context owns an `Add<Context>Domain()` extension; `Program.cs` composes them. Repositories are
singletons **only** because they stand in for a database. Application services and `IEventBus` are
scoped, so a handler resolves from the request scope and the correlation-id logging scope still
applies to it.

`TestServices.BuildRealWiring` builds the container with `ValidateOnBuild` and `ValidateScopes`, so a
captive dependency (a singleton capturing a scoped service) fails a test instead of surprising
production. If you add a registration, that test is where it gets proved.
