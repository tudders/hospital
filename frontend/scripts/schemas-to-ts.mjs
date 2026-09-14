#!/usr/bin/env node
/**
 * Generates src/lib/contracts.ts from the JSON Schema documents the backend generates its request
 * types from, so both sides of the wire come from one source. See
 * docs/adr/0001-schema-first-request-validation.md.
 *
 *   node scripts/schemas-to-ts.mjs           rewrite the output
 *   node scripts/schemas-to-ts.mjs --check   fail if the committed output is stale (used by build)
 *
 * The output is committed rather than generated into the build so that a plain `git clone` type
 * checks, and so that a change to a schema shows up as a diff in the frontend's own review.
 */

import { readFileSync, readdirSync, writeFileSync } from 'node:fs'
import { join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = fileURLToPath(new URL('.', import.meta.url))
const SCHEMA_DIR = resolve(here, '../../backend/src/Alcidion.Api.Contracts/Schemas')
const OUTPUT = resolve(here, '../src/lib/contracts.ts')

/** Keywords that constrain a value without changing its TypeScript type. Emitted as a comment so
 *  the rule is visible where it is used, and so a stale comment shows up in the diff. */
const CONSTRAINTS = ['minLength', 'maxLength', 'minItems', 'maxItems', 'minProperties', 'maxProperties', 'minimum', 'maximum', 'pattern', 'format']

function main() {
  const files = readdirSync(SCHEMA_DIR).filter((f) => f.endsWith('.json')).sort()
  const generated = render(files.map((file) => ({ file, schema: JSON.parse(readFileSync(join(SCHEMA_DIR, file), 'utf8')) })))

  if (!process.argv.includes('--check')) {
    writeFileSync(OUTPUT, generated)
    console.log(`wrote ${relative(process.cwd(), OUTPUT)} from ${files.length} schema document(s)`)
    return
  }

  const committed = readFileSync(OUTPUT, 'utf8').replace(/\r\n/g, '\n')
  if (committed === generated) return

  console.error(
    `${relative(process.cwd(), OUTPUT)} is out of date with ${relative(process.cwd(), SCHEMA_DIR)}.\n` +
      'Run `npm run contracts` and commit the result.',
  )
  process.exit(1)
}

function render(documents) {
  const declarations = []
  for (const { file, schema } of documents) {
    declare(schema, typeName(schema, file), declarations, `Generated from ${file}.`)
  }

  return [
    '/* eslint-disable */',
    '// Generated from backend/src/Alcidion.Api.Contracts/Schemas/*.json - do not edit by hand.',
    '// Run `npm run contracts` after changing a schema; `npm run build` fails if this is stale.',
    '',
    declarations.join('\n\n'),
    '',
  ].join('\n')
}

/** Adds an exported type for `schema`, plus one for any titled sub-schema it contains. */
function declare(schema, name, out, provenance) {
  const body = expression(schema, name, out)
  out.push(`${docComment(schema, provenance, 0)}export type ${name} = ${body}`)
}

/**
 * The TypeScript for a schema. A sub-schema with a title of its own becomes a named type rather
 * than an inline shape, because a name is what the calling code wants to import.
 */
function expression(schema, name, out, depth = 0) {
  // An enum is the whole type. The union of the values it admits is tighter than the primitive
  // type they happen to share, and it is what turns a wrong value into a compile error here
  // rather than a 400 from the API.
  if (Array.isArray(schema.enum)) return schema.enum.map((value) => JSON.stringify(value)).join(' | ')

  // `type` may list several. "Any flat scalar" is a union here rather than the `unknown` an
  // unrecognised type would otherwise produce.
  if (Array.isArray(schema.type)) {
    const members = schema.type.map((type) => expression({ ...schema, type }, name, out, depth))
    return [...new Set(members)].join(' | ')
  }

  switch (schema.type) {
    case 'object':
      if (schema.properties) return shape(schema, name, out, depth)
      // No named properties but a schema for the values: a dictionary whose value type is still
      // worth keeping. Dropping it is what let an array reach a scalars-only `props` and cost a
      // whole telemetry batch a 400 that the client never reports.
      if (schema.additionalProperties && typeof schema.additionalProperties === 'object') {
        return `Record<string, ${expression(schema.additionalProperties, `${name}Value`, out, depth)}>`
      }
      return 'Record<string, unknown>'
    case 'array':
      return `${itemType(schema, name, out, depth)}[]`
    case 'string':
      return 'string'
    case 'integer':
    case 'number':
      return 'number'
    case 'boolean':
      return 'boolean'
    case 'null':
      return 'null'
    default:
      return 'unknown'
  }
}

function itemType(schema, name, out, depth) {
  const items = schema.items
  if (!items) return 'unknown'
  if (!items.title) return expression(items, name, out, depth)

  // A named element type: declared once at the top level and referenced here.
  const itemName = identifier(items.title)
  if (!out.some((d) => d.includes(`export type ${itemName} =`))) {
    declare(items, itemName, out, `Element of ${name}.`)
  }
  return itemName
}

function shape(schema, name, out, depth) {
  const required = new Set(schema.required ?? [])
  const pad = '  '.repeat(depth + 1)
  const members = Object.entries(schema.properties).map(([property, child]) => {
    const optional = required.has(property) ? '' : '?'
    const value = expression(child, `${name}${identifier(property)}`, out, depth + 1)
    return `${docComment(child, null, depth + 1)}${pad}${property}${optional}: ${value}`
  })

  // Deliberately no index signature for an open object. The schema leaves it open so the API
  // tolerates a field it does not yet know about; that says what the server accepts, not that this
  // side may send anything, and an index signature would cost the caller the typo checking this
  // file exists to give it. The openness is stated in the type's comment instead.
  return ['{', ...members, `${'  '.repeat(depth)}}`].join('\n')
}

/** The schema's description and its constraints, as a comment. Empty if it has neither. */
function docComment(schema, provenance, depth) {
  const pad = '  '.repeat(depth)
  const lines = []
  if (schema.description) lines.push(...wrap(schema.description, 96 - pad.length))

  const constraints = CONSTRAINTS.filter((k) => schema[k] !== undefined).map((k) => `${k}: ${schema[k]}`)
  if (constraints.length > 0) lines.push(constraints.join(', '))
  if (schema.type === 'object' && schema.properties && schema.additionalProperties !== false) {
    lines.push('Open: the API ignores properties not listed here rather than refusing them.')
  }
  if (provenance) lines.push(provenance)

  if (lines.length === 0) return ''
  return [`${pad}/**`, ...lines.map((l) => `${pad} * ${l}`), `${pad} */`, ''].join('\n')
}

function wrap(text, width) {
  const lines = []
  let line = ''
  for (const word of text.split(/\s+/)) {
    if (line.length > 0 && line.length + word.length + 1 > width) {
      lines.push(line)
      line = word
    } else {
      line = line.length > 0 ? `${line} ${word}` : word
    }
  }
  if (line.length > 0) lines.push(line)
  return lines
}

function typeName(schema, file) {
  return identifier(schema.title ?? file.replace(/\.json$/, ''))
}

/** PascalCase, and never something that is not a legal identifier. */
function identifier(text) {
  const parts = String(text).split(/[^A-Za-z0-9]+/).filter(Boolean)
  const name = parts.map((p) => p[0].toUpperCase() + p.slice(1)).join('')
  if (!/^[A-Za-z_]/.test(name)) throw new Error(`Cannot make a TypeScript name from '${text}'.`)
  return name
}

main()
