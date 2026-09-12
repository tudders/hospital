/**
 * PostToolUse hook: lint the file that was just written and grep it for the leaks this app must never
 * ship. oxlint on one file is about a second, which is cheap enough to run on every write.
 *
 * Advisory: findings come back as context, not as a blocked edit. The redaction findings are the ones
 * to take seriously - everything they describe is patient data in this app.
 */
import { execFileSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = dirname(dirname(dirname(fileURLToPath(import.meta.url))))

const CHECKS = [
  {
    re: /\.(textContent|innerText|innerHTML)\b/,
    where: /^src[\\/]/,
    note: 'Reading element text in src/: in this app element text is patient data. Describe the element through redaction.ts (describeTarget) instead.',
  },
  {
    re: /track\([^)]*\.value\b/,
    where: /^src[\\/]/,
    note: 'A field value is being recorded. Reduce it to a length with describeField; the value itself never leaves the browser.',
  },
  {
    re: /\bconsole\.(log|info|debug|dir)\s*\(/,
    where: /^src[\\/]/,
    note: 'console logging in src/: it reaches the browser console with whatever it was given, which is the same leak as recording it. Remove it before finishing.',
  },
  {
    re: /\bfetch\s*\(/,
    where: /^src[\\/](?!lib[\\/]api\.ts)/,
    note: 'Bare fetch outside lib/api.ts: no bearer token, no X-Correlation-Id, no problem-details parsing, no api.request event. Go through api().',
  },
  {
    re: /useEffect\s*\(\s*async\b/,
    where: /^src[\\/]/,
    note: 'async useEffect callback: React cannot use the returned promise as a cleanup. Define the async function inside and call it with void.',
  },
  {
    re: /#[0-9a-fA-F]{3,8}\b/,
    where: /^src[\\/].*\.tsx?$/,
    note: 'Raw colour in a component: use a token from src/index.css (--primary, --danger, --muted ...), or add a token if one is missing.',
  },
  {
    re: /KEYS_WORTH_RECORDING\s*=\s*new Set\(\[[^\]]*'[a-z0-9]'/i,
    where: /session-recorder\.ts$/,
    note: 'A printable key was added to KEYS_WORTH_RECORDING. A character keystroke stream reconstructs the field, which is exactly what reducing a value to its length prevents.',
  },
]

function context(text) {
  process.stdout.write(
    JSON.stringify({ hookSpecificOutput: { hookEventName: 'PostToolUse', additionalContext: text } }),
  )
}

let payload
try {
  payload = JSON.parse(readFileSync(0, 'utf8'))
} catch {
  process.exit(0)
}

const absolute = String(payload.tool_input?.file_path ?? '')
if (!/\.(ts|tsx|mjs|js|jsx)$/.test(absolute)) process.exit(0)

const root = (process.env.CLAUDE_PROJECT_DIR ?? payload.cwd ?? ROOT).replace(/[\\/]+$/, '')
const relative = absolute.startsWith(root) ? absolute.slice(root.length + 1) : absolute

let source
try {
  source = readFileSync(absolute, 'utf8')
} catch {
  process.exit(0)
}

const findings = []
const lines = source.split('\n')
for (const { re, where, note } of CHECKS) {
  if (where && !where.test(relative)) continue
  const index = lines.findIndex((l) => re.test(l) && !l.trimStart().startsWith('//') && !l.trimStart().startsWith('*'))
  if (index >= 0) findings.push(`${relative}:${index + 1} ${note}`)
}

// oxlint reports warnings with exit 0 and errors with a non-zero exit, so capture both paths.
// Run its JS entry through node rather than npx: npx is a .cmd on Windows, Node refuses to spawn
// one without a shell, and a shell would mean interpolating a file path into a command string.
const OXLINT = join(root, 'node_modules', 'oxlint', 'bin', 'oxlint')
let lint = ''
if (existsSync(OXLINT)) {
  try {
    lint = execFileSync(process.execPath, [OXLINT, relative], {
      cwd: root,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
      timeout: 60_000,
    })
  } catch (err) {
    lint = `${err.stdout ?? ''}${err.stderr ?? ''}`
  }
}
lint = lint
  .split('\n')
  .filter((l) => l.trim() && !/^Found 0 warnings and 0 errors/.test(l.trim()) && !/^Finished in/.test(l.trim()))
  .join('\n')
  .trim()
if (lint) findings.push(`oxlint:\n${lint.split('\n').slice(0, 20).join('\n')}`)

if (findings.length > 0) {
  context(`Review the edit just made:\n${findings.map((f) => `  - ${f}`).join('\n')}`)
}
process.exit(0)
