/**
 * Stop hook: if C# changed during this session, the tests run before the turn can end.
 *
 * "Never mark work complete without verification" is only a rule if something checks. dotnet test is
 * about five seconds warm, which is cheap enough to spend on every turn that touched code, and the
 * failure output comes back as the reason the turn may not end yet.
 *
 * Escapes, because a red test is a legitimate state mid-TDD:
 *   - touch .claude/verify-skip        (a marker file, delete it to re-enable)
 *   - ALCIDION_SKIP_VERIFY=1           (one session)
 * stop_hook_active is honoured, so a blocked turn is never blocked twice in a row.
 */
import { execFileSync } from 'node:child_process'
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

function git(args) {
  return execFileSync('git', args, { cwd: ROOT, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] })
}

let changed = []
try {
  changed = git(['status', '--porcelain'])
    .split('\n')
    .map((l) => l.slice(3).trim())
    .filter((p) => p.endsWith('.cs') || p.endsWith('.csproj'))
} catch {
  process.exit(0) // not a git work tree, or git unavailable
}

if (changed.length === 0) process.exit(0)

try {
  execFileSync('dotnet', ['test', '--nologo', '-v', 'q'], {
    cwd: ROOT,
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    timeout: 300_000,
  })
} catch (err) {
  const output = `${err.stdout ?? ''}${err.stderr ?? ''}`.trim()
  const tail = output.split('\n').filter(Boolean).slice(-25).join('\n')
  block(
    `dotnet test is not green, and ${changed.length} C# file(s) changed this session. Fix the failures ` +
      `before finishing, or say explicitly that you are leaving them red and why.\n\n${tail}`,
  )
}

process.exit(0)
