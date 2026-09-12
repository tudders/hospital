/**
 * Performance budget for the shipped bundle.
 *
 * Bundle size is a user-facing number on hospital hardware and hospital networks, so it is checked
 * on every build rather than noticed later. Budgets are gzip bytes, since that is what crosses the
 * wire. Raise one deliberately, with the reason, rather than to make a build pass.
 */
import { gzipSync } from 'node:zlib'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'

const BUDGETS_KB = {
  js: 90, // React 19 runtime plus this app
  css: 8,
  html: 2,
}

const DIST = new URL('../dist/', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')

function walk(dir) {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    return statSync(full).isDirectory() ? walk(full) : [full]
  })
}

const totals = { js: 0, css: 0, html: 0 }
const rows = []

for (const file of walk(DIST)) {
  const ext = file.split('.').pop()
  if (!(ext in totals)) continue
  const gzip = gzipSync(readFileSync(file)).length
  totals[ext] += gzip
  rows.push({ file: file.slice(DIST.length), gzip })
}

const kb = (bytes) => (bytes / 1024).toFixed(2)
const failures = []

for (const [ext, budgetKb] of Object.entries(BUDGETS_KB)) {
  const used = totals[ext] / 1024
  const state = used > budgetKb ? 'OVER' : 'ok'
  if (used > budgetKb) failures.push(`${ext}: ${used.toFixed(2)} kB gzip exceeds the ${budgetKb} kB budget`)
  console.log(`  ${ext.padEnd(4)} ${used.toFixed(2).padStart(7)} kB gzip / ${String(budgetKb).padStart(3)} kB budget  ${state}`)
}

for (const row of rows.sort((a, b) => b.gzip - a.gzip)) {
  console.log(`       ${kb(row.gzip).padStart(7)} kB  ${row.file}`)
}

if (failures.length > 0) {
  console.error('\nBundle budget exceeded:\n' + failures.map((f) => `  - ${f}`).join('\n'))
  process.exit(1)
}
console.log('\nBundle within budget.')
