/**
 * Stop hook: if src/ or scripts/ changed during this session, lint, tests and build run before the
 * turn can end. The build is what enforces the gzip budget, so "it compiles" and "it fits" are the
 * same check here.
 *
 * About six seconds in total, which is cheap enough to spend on every turn that touched code.
 *
 * Escapes, because a red suite is a legitimate state mid-change:
 *   - touch .claude/verify-skip        (a marker file, delete it to re-enable)
 *   - ALCIDION_SKIP_VERIFY=1           (one session)
 * stop_hook_active is honoured, so a blocked turn is never blocked twice in a row.
 *
 * What this cannot check is whether the screen is right. UI work is still not done until it has been
 * opened in the browser.
 */
import { execFileSync, execSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = dirname(dirname(dirname(fileURLToPath(import.meta.url))))

function block(reason) {
  process.stdout.write(JSON.stringify({ decision: 'block', reason }))
  process.exit(0)
}

let payload = {}
try {
  payload = JSON.parse(readFileSync(0, 'utf8'))
} catch {
  /* no payload: fall through and verify anyway */
}

if (payload.stop_hook_active) process.exit(0)
if (process.env.ALCIDION_SKIP_VERIFY === '1') process.exit(0)
if (existsSync(join(ROOT, '.claude', 'verify-skip'))) process.exit(0)

let changed = []
try {
  changed = execFileSync('git', ['status', '--porcelain'], { cwd: ROOT, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] })
    .split('\n')
    .map((l) => l.slice(3).trim())
    .filter((p) => /^(src|scripts)\//.test(p) || /^(package\.json|index\.html|vite\.config\.ts)$/.test(p))
} catch {
  process.exit(0) // not a git work tree, or git unavailable
}

if (changed.length === 0) process.exit(0)

// tsc -b, vite build, then the gzip budget check - the budget is enforced by the build.
const steps = ['lint', 'test', 'build']

for (const label of steps) {
  try {
    // execSync, not execFileSync: npm is a .cmd on Windows and Node refuses to spawn one
    // without a shell, which fails as EINVAL with no output at all.
    execSync(`npm run ${label}`, {
      cwd: ROOT,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
      timeout: 300_000,
    })
  } catch (err) {
    const output = `${err.stdout ?? ''}${err.stderr ?? ''}`.trim()
    const tail = output.split('\n').filter(Boolean).slice(-25).join('\n')
    block(
      `npm run ${label} is failing, and ${changed.length} file(s) under src/ or scripts/ changed this session. ` +
        `Fix it before finishing, or say explicitly that you are leaving it failing and why.\n\n${tail}`,
    )
  }
}

process.exit(0)
