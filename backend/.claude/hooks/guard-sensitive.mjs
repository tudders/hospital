/**
 * PreToolUse hook: refuse the handful of actions that must be a human decision.
 *
 * Two classes, both from the repo's own rules: writing to a file that holds real credentials or a
 * deployment target, and a git operation that rewrites or discards history. The permission prompt is
 * not a backstop here - this repo is worked on in bypass-permissions mode, so this hook is the
 * backstop.
 *
 * A denial is advice to ask, not a claim that the action is wrong.
 */
import { readFileSync } from 'node:fs'

const SENSITIVE_PATHS = [
  { re: /(^|[\\/])\.env(\.|$)(?!example)/i, why: 'holds credentials; production values belong in environment variables (Jwt__Secret, Cors__Origins__0)' },
  { re: /appsettings\.Production\.json$/i, why: 'is the production configuration; change it through the deployment, not the repo' },
  { re: /\.(pubxml|publishsettings|pfx|snk)$/i, why: 'is a deployment or signing credential' },
  { re: /(^|[\\/])secrets\.json$/i, why: 'is a user-secrets store' },
]

const DANGEROUS_COMMANDS = [
  { re: /git\s+push[^\n]*\s(origin\s+)?(main|master)\b/, why: 'pushes to main' },
  { re: /git\s+push[^\n]*--force(-with-lease)?\b/, why: 'force-pushes' },
  { re: /git\s+push[^\n]*\s-f\b/, why: 'force-pushes' },
  { re: /git\s+reset\s+--hard/, why: 'discards working-tree changes' },
  { re: /git\s+clean\s+-[a-z]*f/, why: 'deletes untracked files' },
  { re: /git\s+branch\s+-D/, why: 'deletes a branch without a merge check' },
  { re: /git\s+checkout\s+--\s+\./, why: 'discards every working-tree change' },
  { re: /git\s+rebase[^\n]*(-i|--interactive)/, why: 'needs an interactive editor, which is unavailable here' },
]

function deny(reason) {
  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        permissionDecision: 'deny',
        permissionDecisionReason: reason,
      },
    }),
  )
  process.exit(0)
}

let payload
try {
  payload = JSON.parse(readFileSync(0, 'utf8'))
} catch {
  process.exit(0)
}

const tool = payload.tool_name ?? ''
const input = payload.tool_input ?? {}

if (tool === 'Bash') {
  const command = String(input.command ?? '')
  for (const { re, why } of DANGEROUS_COMMANDS) {
    if (re.test(command)) deny(`Blocked: this command ${why}. Ask before running it, and prefer a branch plus a reviewed PR.`)
  }
} else {
  const path = String(input.file_path ?? input.notebook_path ?? '')
  if (path) {
    for (const { re, why } of SENSITIVE_PATHS) {
      if (re.test(path)) deny(`Blocked: ${path} ${why}. Ask before editing it.`)
    }
  }
}

process.exit(0)
