import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { flush, pendingEvents, sessionId, track } from './telemetry.ts'

describe('event timeline', () => {
  it('stamps every event with the session, an increasing sequence and an offset', () => {
    track('ui.click', { target: 'button:register' })
    track('ui.submit', { target: 'form:patients' })

    const events = pendingEvents().slice(-2)
    assert.equal(events.length, 2)
    assert.ok(events.every((e) => e.sessionId === sessionId))
    assert.equal(events[1].seq, events[0].seq + 1)
    assert.ok(events[1].t >= events[0].t)
    assert.ok(Number.isFinite(Date.parse(events[0].at)))
  })

  it('keeps the props it was given', () => {
    track('api.request', { path: '/api/patients', status: 201 })

    assert.deepEqual(pendingEvents().at(-1)?.props, { path: '/api/patients', status: 201 })
  })

  it('does nothing when no endpoint is configured, rather than throwing', () => {
    assert.doesNotThrow(() => flush())
  })
})

describe('props stay inside what the API accepts', () => {
  // The API validates the whole batch and rejects all of it over one bad value, and flush()
  // swallows the 400 so the app never learns. Everything here is about never sending that batch.

  it('clips a string value to the length the API allows', () => {
    track('app.error', { message: 'x'.repeat(2000) })

    const message = pendingEvents().at(-1)?.props?.message
    assert.equal(typeof message, 'string')
    assert.equal((message as string).length, 1024)
    assert.ok((message as string).endsWith('…'))
  })

  it('leaves a value at the limit alone, and does not copy props that need no clipping', () => {
    const props = { message: 'x'.repeat(1024), source: 'app.ts' }
    track('app.error', props)

    assert.equal(pendingEvents().at(-1)?.props, props)
  })

  it('queues only flat scalars, whatever the caller passed', () => {
    // The type forbids an array or an object; this is the runtime half of the same rule, and it is
    // the shape that cost every login batch a 400 - auth.login sent roles as string[].
    track('auth.login', { user: 'nurse', roles: ['clinician', 'admin'].join(' ') })

    const props = pendingEvents().at(-1)?.props ?? {}
    assert.equal(props.roles, 'clinician admin')
    for (const value of Object.values(props)) {
      assert.ok(
        value === null || ['string', 'number', 'boolean'].includes(typeof value),
        `props carried a ${typeof value}, which the batch schema refuses`,
      )
      assert.ok(typeof value !== 'string' || value.length <= 1024)
    }
  })
})

// Last in the file on purpose: it fills the shared queue to its cap, and a full queue drops
// everything tracked after it.
describe('buffer ceiling', () => {
  it('caps the buffer so a backend outage cannot grow it without bound', () => {
    for (let i = 0; i < 1000; i++) track('ui.scroll', { y: i })

    assert.ok(pendingEvents().length <= 500)
  })
})
