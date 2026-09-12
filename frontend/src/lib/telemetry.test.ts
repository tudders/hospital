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

  it('caps the buffer so a backend outage cannot grow it without bound', () => {
    for (let i = 0; i < 1000; i++) track('ui.scroll', { y: i })

    assert.ok(pendingEvents().length <= 500)
  })

  it('does nothing when no endpoint is configured, rather than throwing', () => {
    assert.doesNotThrow(() => flush())
  })
})
