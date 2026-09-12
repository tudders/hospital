/**
 * PreToolUse hook: refuse the handful of actions that must be a human decision, and ask about the
 * one that changes what ships to the browser.
 *
 * deny - writing to a credentials file, or a git command that rewrites or discards history.
 * ask  - installing a runtime dependency, or raising a bundle budget: both are decisions with a
 *        user-visible cost, and "ask" turns into a prompt even in bypass-permissions mode.
 */
import { readFileSync } from 'node:fs'

const SENSITIVE_PATHS = [
  { re: /(^|[\\/])\.env(\.|$)(?!example)/i, why: 'holds credentials and machine-specific values; document variables in .env.example instead' },
  { re: /(^|[\\/])\.npmrc$/i, why: 'can hold a registry token' },
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
  { re: /\brm\s+-rf?\s+[^\n]*node_modules[^\n]*\s+\S/, why: 'deletes more than node_modules' },
]

/** Installing a package that is not dev-only lands in the bundle, which has a budget. */
const INSTALL = /\bnpm\s+(i|install|add)\b(?![^\n]*(-D\b|--save-dev\b|--global\b|-g\b))[^\n]*\s[a-z@][^\s]*/

function decide(decision, reason) {
  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        permissionDecision: decision,
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
    if (re.test(command)) decide('deny', `Blocked: this command ${why}. Ask before running it, and prefer a branch plus a reviewed PR.`)
  }
  if (INSTALL.test(command) && !/npm\s+(i|install)\s*$/.test(command.trim())) {
    decide(
      'ask',
      'This adds a runtime dependency, which ships to the browser against a 90 kB gzip JS budget with about 20 kB of headroom. ' +
        'Confirm it is wanted, or use --save-dev if it is dev-only.',
    )
  }
} else {
  const path = String(input.file_path ?? input.notebook_path ?? '')
  if (path) {
    for (const { re, why } of SENSITIVE_PATHS) {
      if (re.test(path)) decide('deny', `Blocked: ${path} ${why}. Ask before editing it.`)
    }
    if (/check-bundle-size\.mjs$/.test(path)) {
      const content = String(input.content ?? input.new_string ?? '')
      if (/BUDGETS_KB/.test(content) || /\b(js|css|html):\s*\d+/.test(content)) {
        decide(
          'ask',
          'This looks like a change to the bundle budget. A budget is raised deliberately, with the reason, never to make a build pass. Confirm the new number and why it is worth it.',
        )
      }
    }
  }
}

process.exit(0)
