---
name: telemetry-redaction
description: Use whenever you add, change or review anything that records, logs or sends user activity - a new recorded event, a field in an event payload, an error message, a console call, or a change to session-recorder.ts or redaction.ts. States what may never leave the browser in this clinical app and how to describe an element instead.
---

# Content never leaves the browser

This is a clinical app. Element text, field values and option labels are patient data. Nothing the
recorder ships may be able to reconstruct an MRN, a name or a date of birth.

| May be recorded | Never recorded |
|---|---|
| Element role and tag (`input`, `button`) | `textContent`, `innerText`, `innerHTML` |
| `data-track`, `aria-label`, `name`, `placeholder`, `id`, `type` | A field's value, or any prefix of it |
| `data-region` of the nearest ancestor | Option labels, table cell contents |
| A typed value's **length**, and whether it is filled | Character keystrokes |
| Non-character keys (`Enter`, `Escape`, `Tab`, arrows) | URLs or ids carrying clinical identifiers |

## Where the rule is enforced

`src/lib/redaction.ts` is the only place allowed to decide how something is described:

- `describeTarget(el)` - `region/tag:name`, built from author-given attributes. Text never qualifies.
- `describeField(el, value)` - `{ field, inputType, valueLength, filled }`. The value itself stays.

It takes an `ElementLike`, not an `Element`, so the rules are unit-tested without a DOM. That is
deliberate: this is the part that must never silently regress, and a test that needs jsdom is a test
that gets skipped.

The division of labour is strict:

```
session-recorder.ts   decides WHEN to record
redaction.ts          decides WHAT MAY BE SAID
telemetry.ts          only buffers and ships
```

A new event type does not get to describe its own target. Call `describeTarget` / `describeField`.

## Adding a recorded event

1. Add the case in `session-recorder.ts`, using a passive capture listener, and throttle anything
   high-frequency (scroll and resize are throttled to 250 ms).
2. Build props only from `describeTarget` / `describeField` plus primitives you can defend
   (durations, counts, status codes, correlation ids).
3. If the event needs a new kind of description, extend `redaction.ts` **and** add a test in
   `src/lib/redaction.test.ts` proving the content cannot be reconstructed.
4. `npm run test` - the redaction tests are the gate.

## Why keystrokes are excluded

A stream of character keystrokes reconstructs the field, which defeats reducing the value to a
length. Only the keys in `KEYS_WORTH_RECORDING` are recorded. Do not add a printable key to that set.

DOM mutation capture is deliberately out of scope: the timeline plus the rendered app is enough to
retrace a session, and a DOM-diff recorder would ship exactly the content this rule excludes.

## Review questions

- Could an attacker with the event stream and the app reconstruct a patient identifier?
- Does any new prop come from the DOM rather than from `redaction.ts`?
- Is there a `console.log` of an event, a value, or a response body? That is the same leak by another
  route.
