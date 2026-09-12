---
description: Exercise the app in the browser and read back the recorded session timeline, checking for duplicates and leaks
allowed-tools: Bash, Read, Grep, Glob
---

Drive the app for real and inspect what it recorded. $ARGUMENTS names the flow to exercise; with no
argument, exercise login, then register a patient, then admit them.

1. Start the API (`dotnet run --project ../backend/src/Alcidion.Api --launch-profile http`) and the app
   (`npm run dev`). Both ports 5173 and 5174 are CORS-allowed.
2. Walk the flow in the browser, with StrictMode on. Use the demo users from the backend README
   (`nurse`/`nurse` is a clinician).
3. Read the recorded timeline: the batches posted to `POST /api/telemetry/events`, and the API console
   output for the matching correlation ids.

Then check, and report on each:

- **Order.** Sequence numbers are monotonic, `t` offsets increase, and the timeline reads as the flow
  you actually performed.
- **Duplicates.** `session.start` appears exactly once per tab, including across a StrictMode remount
  and a reload. No event is doubled by an effect mounting twice. A duplicate here is the bug class this
  app has already had twice.
- **Names.** Every click and submit resolves to `region/tag:name`, not `button:unknown`. An unnamed
  control needs `data-track`, and its section needs `data-region`.
- **Leaks.** No event carries element text, a field value, an option label or a printable keystroke. A
  typed value appears only as `valueLength` and `filled`. If anything else appears, stop and report it
  as the finding that matters.
- **The join.** Pick one `api.request` event, take its correlation id, and find it in the API console:
  the audit line, an ordinary domain log line, and the trace span. Report any link that is missing.

Report what the timeline shows, each check as held or broken, and for anything broken the file and line
responsible. Do not fix as you go unless asked - report first, so the timeline evidence stays readable.
