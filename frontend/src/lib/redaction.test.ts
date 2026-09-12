import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { describeField, describeTarget, type ElementLike } from './redaction.ts'

function el(tagName: string, attrs: Record<string, string> = {}, region?: string): ElementLike {
  return {
    tagName,
    id: attrs.id,
    getAttribute: (name) => attrs[name] ?? null,
    closest: (selector) =>
      selector === '[data-region]' && region ? el('section', { 'data-region': region }) : null,
  }
}

describe('describeTarget', () => {
  it('prefers the author-supplied hook over anything incidental', () => {
    assert.equal(describeTarget(el('button', { 'data-track': 'register', 'aria-label': 'Register patient' })), 'button:register')
  })

  it('falls back through aria-label, name, placeholder, id, then type', () => {
    assert.equal(describeTarget(el('input', { 'aria-label': 'MRN' })), 'input:MRN')
    assert.equal(describeTarget(el('input', { name: 'mrn' })), 'input:mrn')
    assert.equal(describeTarget(el('input', { placeholder: 'MRN' })), 'input:MRN')
    assert.equal(describeTarget(el('input', { id: 'mrn' })), 'input:#mrn')
    assert.equal(describeTarget(el('input', { type: 'date' })), 'input:date')
  })

  it('qualifies the element with its region so repeated controls stay distinguishable', () => {
    assert.equal(describeTarget(el('button', { 'data-track': 'submit' }, 'admissions')), 'admissions/button:submit')
  })

  it('never reaches for element text, which in this app is patient data', () => {
    const withText = { ...el('button'), textContent: 'Ada Lovelace' } as ElementLike
    assert.equal(describeTarget(withText), 'button')
  })

  it('survives a missing target', () => {
    assert.equal(describeTarget(null), 'unknown')
  })
})

describe('describeField', () => {
  it('records the field and the length, never the value', () => {
    const input = el('input', { name: 'mrn', type: 'text' }, 'patients')
    const described = describeField(input, 'MRN-123456')

    assert.deepEqual(described, {
      field: 'patients/input:mrn',
      inputType: 'text',
      valueLength: 10,
      filled: true,
    })
    assert.ok(!JSON.stringify(described).includes('MRN-123456'))
  })

  it('treats a non-string value as empty rather than serialising it', () => {
    const described = describeField(el('select', { name: 'ward' }), { secret: 'value' })

    assert.equal(described.valueLength, 0)
    assert.equal(described.filled, false)
    assert.ok(!JSON.stringify(described).includes('secret'))
  })
})
