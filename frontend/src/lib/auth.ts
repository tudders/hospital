const TOKEN_KEY = 'alcidion.token'

let token: string | null = (() => {
  try {
    return sessionStorage.getItem(TOKEN_KEY)
  } catch {
    return null
  }
})()

export function getToken() {
  return token
}

export function setToken(next: string | null) {
  token = next
  try {
    if (next) sessionStorage.setItem(TOKEN_KEY, next)
    else sessionStorage.removeItem(TOKEN_KEY)
  } catch {
    // storage unavailable; token lives in memory only
  }
}

export type Me = { name: string; roles: string[] }

export function canWrite(me: Me | null) {
  return !!me && (me.roles.includes('clinician') || me.roles.includes('admin'))
}
