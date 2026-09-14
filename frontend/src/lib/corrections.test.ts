import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { changedFields } from './corrections.ts'
import type { Patient } from './types.ts'

const ada: Patient = {
  id: '11111111-1111-1111-1111-111111111111',
  mrn: 'MRN-001',
  givenName: 'Ada',
  familyName: 'Lovelace',
  dateOfBirth: '1815-12-10',
  registeredAt: '2026-09-01T09:00:00+00:00',
  version: 3,
}

describe('changedFields', () => {
  it('sends nothing when the form was opened and closed again', () => {
    // The form round-trips all four fields. Sending them back unchanged would claim to correct
    // things nobody looked at, and would lose a race against whoever is correcting them for real.
    assert.deepEqual(changedFields(ada, { ...ada }), {})
  })

  it('sends only the field that actually moved', () => {
    assert.deepEqual(changedFields(ada, { ...ada, familyName: 'King-Noel' }), { familyName: 'King-Noel' })
  })

  it('sends every field that moved, and no others', () => {
    assert.deepEqual(changedFields(ada, { ...ada, mrn: 'MRN-002', dateOfBirth: '1815-12-11' }), {
      mrn: 'MRN-002',
      dateOfBirth: '1815-12-11',
    })
  })

  it('treats a retyped MRN that normalises to the same value as unchanged', () => {
    // The aggregate trims and upper-cases, so " mrn-001 " is the MRN the patient already has.
    // Sending it would spend the version - and the next person's 412 - on a no-op.
    assert.deepEqual(changedFields(ada, { ...ada, mrn: '  mrn-001  ' }), {})
  })

  it('treats a name with surrounding whitespace as unchanged, because the aggregate trims it', () => {
    assert.deepEqual(changedFields(ada, { ...ada, givenName: ' Ada ' }), {})
  })

  it('ignores fields the correction does not carry at all', () => {
    assert.deepEqual(changedFields(ada, { familyName: 'King-Noel' }), { familyName: 'King-Noel' })
  })

  it('keeps a value the user actually typed, whitespace and all, once it differs', () => {
    // Trimming here would silently change what was asked for. The schema tolerates the padding and
    // the aggregate trims it, so the request stays what the user wrote.
    assert.deepEqual(changedFields(ada, { ...ada, givenName: ' Augusta ' }), { givenName: ' Augusta ' })
  })
})
