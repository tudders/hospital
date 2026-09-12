---
name: effects-and-strictmode
description: Use when writing or reviewing a React component - adding useEffect, useState, useCallback, a subscription, a fetch on mount, or anything that emits an event. Covers the render/commit/effect lifecycle, idempotent effects with cleanup, and the two duplicate-event bugs StrictMode caught in this app.
---

# Render is pure; effects touch the world

React's lifecycle: render (pure, may run more than once), commit, effects, cleanup. Anything reaching
outside - a listener, a timer, a fetch, a recorded event - belongs in an effect with a cleanup, and
must tolerate running twice.

StrictMode is on in development and stays on. It mounts, unmounts and remounts every component
specifically to expose effects that are not idempotent.

## What StrictMode caught here

Both bugs were visible in the recorded timeline as duplicate events:

1. **`session.start` per effect mount.** A session starts once per tab, not once per mount. The marker
   now lives in `sessionStorage` beside the session id (`alcidion.recording.started`), so it survives
   a remount and a reload.
2. **`page.view` per effect run**, duplicating what `session.start` already recorded. It was deleted;
   real navigations are recorded as `ui.navigate`.

The rule both produce: **an effect that emits an event must key that event to the thing it describes,
not to the component's mounting.** If the thing is the session, the marker belongs with the session.

## Patterns in use

```tsx
// Start recording once; the cleanup is the recorder's own stop function.
useEffect(() => startSessionRecording(), [])

// refresh is a dependency of an effect, so it is stable.
const refresh = useCallback(async () => { ... }, [/* real deps */])
useEffect(() => { void refresh() }, [refresh])
```

- `useState` for local form and page state. Never derive state in render that could be computed from
  props or state directly - compute it.
- `useCallback` only where a function is an effect dependency or is passed to a memoized child.
  Elsewhere it is noise.
- Every effect returns a cleanup if it subscribed, timed, or started anything.
- No `async` effect callbacks: define the async function inside, call it, and ignore its promise
  explicitly (`void run()`).

## Checklist before declaring a component done

- Run it twice: does anything double - an event, a listener, a request, a toast?
- Does the cleanup actually undo what the effect did?
- Does the effect's dependency array list what it reads, with no function that is recreated each
  render?
- Is there state that only mirrors a prop? Remove it.
- Did a new effect start recording, fetching or listening without a cleanup? Fix before moving on.

`npm run dev` with StrictMode on is the test. A duplicate in the recorded timeline is the symptom to
watch for, and `../docs/lifecycle-and-hooks.md` section 2 has the longer version of this story.
