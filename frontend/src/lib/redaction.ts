/**
 * How a recorded UI event describes what the user touched.
 *
 * This is a clinical app, so element text, field values and option labels are patient data. An
 * element is described by its role, its author-given name and its region; a typed value is reduced
 * to a length. Nothing produced here can reconstruct an MRN, a name or a date of birth.
 *
 * Kept free of imports and side effects so it can be exercised directly, without a DOM.
 */

/** The subset of Element this module needs, so the describe logic is testable without a DOM. */
export type ElementLike = {
  tagName: string
  id?: string
  getAttribute(name: string): string | null
  closest?(selector: string): ElementLike | null
  parentElement?: ElementLike | null
  children?: ArrayLike<ElementLike>
}

/**
 * A short, stable description of an element: its tag, then the first identifying attribute that is
 * safe to keep. Author-supplied hooks win, because they survive restyling; text content never
 * qualifies, since in this app it is patient data.
 */
export function describeTarget(el: ElementLike | null | undefined): string {
  if (!el) return 'unknown'
  const tag = el.tagName.toLowerCase()
  const name =
    el.getAttribute('data-track') ??
    el.getAttribute('aria-label') ??
    el.getAttribute('name') ??
    el.getAttribute('placeholder') ??
    (el.id ? `#${el.id}` : null) ??
    el.getAttribute('type')
  const region = el.closest?.('[data-region]')?.getAttribute('data-region')
  const label = [tag, name].filter(Boolean).join(':')
  return region ? `${region}/${label}` : label
}

/** Field identity only. The value itself never leaves the browser; its length is enough for replay. */
export function describeField(
  el: ElementLike,
  rawValue: unknown,
): { field: string; inputType: string; valueLength: number; filled: boolean } {
  const value = typeof rawValue === 'string' ? rawValue : ''
  return {
    field: describeTarget(el),
    inputType: el.getAttribute('type') ?? el.tagName.toLowerCase(),
    valueLength: value.length,
    filled: value.length > 0,
  }
}
