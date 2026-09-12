---
name: design-system
description: Use when adding or restyling UI - a new component, a form, a table, a status colour, a button variant - or when tempted to add a CSS file or a styling library. Covers the token set in src/index.css, the primitives that exist, and the data-track / data-region attributes the recorder depends on.
---

# Tokens and a handful of primitives

One stylesheet, `src/index.css`: tokens on `:root`, then primitives. No CSS-in-JS, no utility
framework, no per-component stylesheet. The CSS budget is 8 kB gzip and the app uses about 1.1 kB;
that headroom is for tokens and primitives, not for a framework.

## Tokens

```
colour     --bg --surface --border --text --muted --primary --primary-hover --danger --ok --warn
shape      --radius
spacing    --space-1 (4px) --space-2 (8) --space-3 (12) --space-4 (16) --space-6 (24) --space-8 (32)
type       --font --mono, 15px base, 1.45 line height
```

Rules:

- Never write a raw hex colour or a raw pixel gap in a component. If a value is missing, add a token.
- Spacing comes from the scale. A one-off `margin: 7px` is a bug in the scale or in the layout.
- Status colour is semantic: `--danger` for a failure, `--ok` for success, `--warn` for attention.
  Never colour alone - pair it with text, because a washed-out clinical screen is a real constraint.

## Primitives

`card`, `btn`, `table`, `badge`, `alert`. Compose these before inventing a class. A new primitive
earns its place by being used in at least two places; until then it is local markup.

## Attributes the recorder depends on

Styling and observability meet here. `src/lib/redaction.ts` may describe an element only by its role
and its author-given name, so the names you put in the markup *are* the session timeline:

- `data-track="register-patient"` on anything clickable or submittable - a stable, behavioural name,
  not a label ("register-patient", never "blue-button").
- `data-region="patients"` on the section wrapper, so events read `patients/button:register-patient`.
- `aria-label` or `name` on inputs, which serves both assistive tech and the recorder.

A control with no `data-track`, `aria-label` or `name` records as `button:unknown`, which is a hole in
the timeline. Adding a control means naming it.

## Accessibility floor

Labels tied to inputs, focus visible (do not remove outlines), colour contrast that survives a bright
ward, and errors announced in text. These are the cheap half of accessibility and they are not
optional.

## Check before finishing

```
npm run dev        # look at it, at a narrow width too
npm run build      # CSS budget is enforced here
```

Never report UI work done without opening it in the browser.
