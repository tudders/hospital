---
status: accepted
---

# Request validation is schema-first, with JSON Schema as the source of truth

The API is about to grow from five endpoints to roughly thirty, and the OpenAPI document it
publishes today carries no constraints at all â€” so the TypeScript frontend re-implements every
length, pattern and required-ness rule by hand, and drifts. We are making JSON Schema the source of
truth for the wire contract: request types are generated from schema documents with
`Corvus.Text.Json`, validation runs at the edge against the schema, and the same documents generate
the frontend's types. The setup cost is fixed and amortises over thirty endpoints; the cost it
removes â€” hand-maintaining the same rules in two languages â€” is per-endpoint and compounds.

## Considered options

- **DataAnnotations** (`[Required]`, `[StringLength]`, plus a small custom attribute set). Implemented
  first and reverted. It validates and it surfaces in the OpenAPI schema, but the rules live in C#
  attributes, so the published document is a projection of the server's types rather than a contract
  either side can be generated from. It also has a sharp edge: MVC throws
  `InvalidOperationException` when validation metadata sits on a record's *properties* via
  `[property:]` while the fields are primary-constructor parameters.
- **FluentValidation.** Better ergonomics than attributes for conditional rules, but the rules become
  invisible to OpenAPI entirely â€” strictly worse for the thing we are trying to fix.
- **C#-first with an OpenAPI schema transformer** to project constraints into the published document.
  This was the recommendation from the session that produced the analysis: it keeps the rules where
  the code is and still publishes them. It was rejected because the transformer has to be taught
  every rule shape by hand, and because what is being chosen here is the precedent for thirty
  endpoints, not the handling of the current five.

## Consequences

- Generated types are `readonly partial struct` over `JsonElement`. They stay at the controller edge;
  `RegisterPatientCommand` and the other application commands remain plain records the domain owns.
  `JsonElement`-backed structs must not leak into application services.
- Edge validation covers shape, length and format only. Aggregates keep their own guards â€” they are
  the enforcement point for callers that never come through HTTP, so this is not duplication and
  must not be "cleaned up" (see `.agents/skills/invariants-at-the-write/SKILL.md`).
- Rules that depend on the clock â€” "date of birth is not in the future" â€” cannot go in a static
  schema without baking in a build-time date. They stay in the aggregate, which compares against the
  injected clock.
- The generated types and the schema documents live in their own project,
  `Alcidion.Api.Contracts`. It carries `LangVersion=preview`, which the Corvus generator's output
  needs on the .NET 9 SDK and which hand-written API code must not silently acquire; it is also the
  single project the frontend's codegen points at.
- A guardrail test — `RequestBodyContractTests` — reflects over every controller action and asserts
  that each body type on a body-carrying verb is schema-generated, so endpoint 31 cannot ship
  unvalidated silently. Its list of exceptions is now empty: `AdmitPatientRequest` and
  `LoginRequest` were the last two bound C#-first, and every body the API accepts is generated
  from a schema document. The list stays in place so the next exception has to be a deliberate
  act with a name on it. `ContractDocumentTests` asserts the same property from the document
  side: a published request body carrying no `title` is one reflected off a C# type rather than
  read from a schema, and fails the build.
- Shape is all the edge asserts, and for `POST /api/auth/login` that is the whole of it. Wrong
  credentials stay a 401 naming no field — a 400 would tell a caller which half of the pair to
  keep — and the password carries no published pattern, because a rule there would describe the
  credential format to everyone who can read the document.
