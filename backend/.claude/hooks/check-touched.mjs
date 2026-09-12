/**
 * PostToolUse hook: grep the file that was just written for the defect classes this codebase has
 * already paid for, and say so while the change is still in hand.
 *
 * Advisory by design. These are heuristics, and a heuristic that blocks an edit costs more than it
 * saves; each finding names the rule so it can be dismissed on sight when it is wrong. Runs in
 * milliseconds, so it can fire on every .cs write - dotnet format takes nine seconds and belongs in
 * /verify instead.
 */
import { readFileSync } from 'node:fs'

const CHECKS = [
  {
    re: /\bDateTime\.(Now|UtcNow)\b/,
    where: /^src[\\/]/,
    note: 'DateTime.Now/UtcNow in src: take time from IClock so the behaviour is testable.',
  },
  {
    re: /\basync\s+void\b/,
    note: 'async void: exceptions escape to the synchronization context and cannot be awaited or asserted. Return Task.',
  },
  {
    re: /\.GetAwaiter\(\)\.GetResult\(\)|\.Result\b|\.Wait\(\)/,
    note: 'Blocking on a Task: await it. Sync-over-async deadlocks under load and hides cancellation.',
  },
  {
    re: /\bTask\.Run\s*\(/,
    where: /^tests[\\/]/,
    note: 'Task.Run in a test: a body that blocks on a Barrier starves the thread pool and the race never happens. Use real Threads, as the existing concurrency tests do.',
  },
  {
    re: /\bThread\.Sleep\s*\(/,
    where: /^tests[\\/]/,
    note: 'Thread.Sleep in a test: sleeping is not synchronisation. Use a Barrier or an awaited signal.',
  },
  {
    re: /\bAddAsync\s*\(/,
    where: /^src[\\/]/,
    note: 'AddAsync: uniqueness is enforced by the insert. Use the TryAdd-shaped method so a race cannot produce two rows (see skills/invariants-at-the-write).',
  },
  {
    re: /catch\s*\(\s*Exception\s*\w*\s*\)\s*\{\s*\}/,
    note: 'Empty catch of Exception: an expected failure is a Result<T>; an unexpected one belongs in the logs.',
  },
  {
    re: /return\s+StatusCode\s*\(/,
    where: /Controllers[\\/]/,
    note: 'Bare StatusCode() in a controller: map the domain Error through ApiController.FromError so the response stays problem details.',
  },
  {
    re: /using\s+Alcidion\.Patients\s*;/,
    where: /^src[\\/]Alcidion\.Admissions[\\/]/,
    note: 'Admissions is referencing Patients directly. It may reference Alcidion.Patients.Contracts only; data from Patients arrives as an event and lives in the local read model (see skills/bounded-contexts-and-events).',
  },
]

const FILE_NOTES = [
  {
    re: /Program\.cs$/,
    note: 'Program.cs: middleware order is load-bearing (exception handler, correlation id, CORS, auth, routing, filters). If the order changed, say why, and keep CorrelationTests green.',
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
if (!absolute.endsWith('.cs')) process.exit(0)

const root = (process.env.CLAUDE_PROJECT_DIR ?? payload.cwd ?? '').replace(/[\\/]+$/, '')
const relative = root && absolute.startsWith(root) ? absolute.slice(root.length + 1) : absolute

let source
try {
  source = readFileSync(absolute, 'utf8')
} catch {
  process.exit(0)
}

const findings = []
for (const { re, where, note } of CHECKS) {
  if (where && !where.test(relative)) continue
  const line = source.split('\n').findIndex((l) => re.test(l) && !l.trimStart().startsWith('//'))
  if (line >= 0) findings.push(`${relative}:${line + 1} ${note}`)
}
for (const { re, note } of FILE_NOTES) if (re.test(relative)) findings.push(note)

if (findings.length > 0) {
  context(`Review the edit just made:\n${findings.map((f) => `  - ${f}`).join('\n')}`)
}
process.exit(0)
