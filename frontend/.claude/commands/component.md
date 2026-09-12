---
description: Add or change a component with the lifecycle, naming, token and recording rules already applied
allowed-tools: Bash, Read, Write, Edit, Grep, Glob
---

Build what $ARGUMENTS describes as a component in this app's style. If the description is too vague to
name the component and its states, ask one question and stop; otherwise proceed without checking in.

First read `skills/effects-and-strictmode/SKILL.md`, `skills/design-system/SKILL.md` and
`skills/telemetry-redaction/SKILL.md`, and look at `src/components/Patients.tsx` for the shape this
codebase uses.

Then build it with these already applied:

- **State.** Local `useState` for form and page state. Nothing derived in render that could be
  computed. No state that only mirrors a prop.
- **Effects.** Only for the outside world, each with a cleanup, each idempotent under a StrictMode
  remount. `useCallback` only where the function is an effect dependency. No `async` effect callback.
- **Data.** Through `api()` from `src/lib/api.ts`, never a bare `fetch`: it carries the token and a
  correlation id and records the request.
- **Errors.** Surface through `ErrorAlert`, including the correlation id from `ApiError.problem`. Never
  swallow a failure, and never put a field value in the message.
- **Naming.** `data-track` on every clickable and submittable control, `data-region` on the section
  wrapper, `aria-label` or `name` on every input. These names are the session timeline.
- **Style.** Tokens and primitives from `src/index.css` (`card`, `btn`, `table`, `badge`, `alert`). No
  raw colours, no raw pixel gaps, no new CSS file.
- **Accessibility.** Labels tied to inputs, focus left visible, errors in text and not colour alone.
- **Empty, loading and error states.** All three exist, because a clinical screen with none of them is
  a screen that lies.

Finish with `npm run lint`, `npm run test`, `npm run build`, then `npm run dev` and look at the
component in the browser - at a narrow width too. Report what you built, the events it will record,
and what you saw on screen. Not done until it has been seen.
