/**
 * Re-fetch the third-party skills listed in vendored.json from their upstream repos.
 *
 * Vendored skills are copied verbatim and never edited here: upstream is the author, this repo is
 * only a consumer pinned to a commit. To take a newer upstream, change that source's `ref` in
 * vendored.json and re-run; to add or drop a skill, edit the `skills` array and re-run.
 *
 * Usage:
 *   node skills/vendor/update.mjs            re-fetch every skill, overwriting the local copy
 *   node skills/vendor/update.mjs --check    fetch and report drift, write nothing (exit 1 if any)
 *
 * Each source is fetched once into a temp clone at its pinned commit, so the result is reproducible
 * as long as the ref is a sha. A skill's directory name must equal the `name` in its SKILL.md
 * frontmatter, because that is what the sync hook mirrors and what agents load it by.
 */
import { execFileSync } from 'node:child_process'
import { cpSync, existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const VENDOR = dirname(fileURLToPath(import.meta.url))
const ROOT = dirname(dirname(VENDOR)) // skills/vendor -> repo root
const CHECK = process.argv.includes('--check')

const manifest = JSON.parse(readFileSync(join(VENDOR, 'vendored.json'), 'utf8'))

function git(args, cwd) {
  return execFileSync('git', args, { cwd, stdio: ['ignore', 'pipe', 'pipe'] }).toString()
}

/** Shallow-clone one source at its pinned ref. GitHub serves a fetch of a bare sha. */
function checkout(key, source) {
  const dir = mkdtempSync(join(tmpdir(), `vendor-${key}-`))
  git(['init', '--quiet', dir])
  git(['remote', 'add', 'origin', source.repo], dir)
  git(['fetch', '--quiet', '--depth', '1', 'origin', source.ref], dir)
  git(['checkout', '--quiet', 'FETCH_HEAD'], dir)
  return dir
}

/** The `name:` line of a SKILL.md, which must match the directory it lives in. */
function frontmatterName(skillDir) {
  const text = readFileSync(join(skillDir, 'SKILL.md'), 'utf8')
  return text.match(/^name:\s*["']?([^"'\n]+?)["']?\s*$/m)?.[1] ?? null
}

/** Every file under dir, as paths relative to it, sorted - enough to compare two trees. */
function files(dir) {
  const out = []
  const walk = (d) => {
    for (const entry of readdirSync(d, { withFileTypes: true })) {
      const full = join(d, entry.name)
      if (entry.isDirectory()) walk(full)
      else out.push(relative(dir, full).split(sep).join('/'))
    }
  }
  walk(dir)
  return out.sort()
}

function differs(a, b) {
  if (!existsSync(b)) return true
  const [fa, fb] = [files(a), files(b)]
  if (fa.join('\n') !== fb.join('\n')) return true
  return fa.some((f) => !readFileSync(join(a, f)).equals(readFileSync(join(b, f))))
}

const clones = new Map()
const drifted = []
let copied = 0

try {
  for (const skill of manifest.skills) {
    const source = manifest.sources[skill.source]
    if (!source) throw new Error(`${skill.name}: no source named "${skill.source}" in vendored.json`)

    if (!clones.has(skill.source)) {
      process.stdout.write(`fetching ${source.repo} @ ${source.ref.slice(0, 10)}\n`)
      clones.set(skill.source, checkout(skill.source, source))
    }

    const from = join(clones.get(skill.source), skill.path)
    if (!existsSync(from) || !statSync(from).isDirectory()) {
      throw new Error(`${skill.name}: ${skill.path} is not a directory at ${source.ref}`)
    }
    if (!existsSync(join(from, 'SKILL.md'))) throw new Error(`${skill.name}: ${skill.path} has no SKILL.md`)

    const declared = frontmatterName(from)
    if (declared !== skill.name) {
      throw new Error(`${skill.name}: upstream SKILL.md declares name "${declared}"; rename it in vendored.json`)
    }

    const to = join(VENDOR, skill.name)
    if (CHECK) {
      if (differs(from, to)) drifted.push(skill.name)
      continue
    }
    rmSync(to, { recursive: true, force: true })
    cpSync(from, to, { recursive: true })
    copied += 1
  }
} finally {
  for (const dir of clones.values()) rmSync(dir, { recursive: true, force: true })
}

if (CHECK) {
  if (drifted.length) {
    console.log(`drifted from upstream (${drifted.length}): ${drifted.join(', ')}`)
    console.log('Run: node skills/vendor/update.mjs')
    process.exit(1)
  }
  console.log(`${manifest.skills.length} vendored skills match upstream.`)
} else {
  console.log(`vendored ${copied} skills into ${relative(ROOT, VENDOR).split(sep).join('/')}/`)
}
