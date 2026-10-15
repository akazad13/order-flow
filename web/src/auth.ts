/**
 * The login (specs 012, 015, 017; spec 023 since): the gateway logs the customer in with Keycloak and
 * keeps the tokens on the server. The page holds no token at all, only a session cookie its scripts
 * cannot read; it asks the gateway who is logged in, and sends `X-CSRF: 1` on every call it makes.
 */
export type Customer = { id: string; name: string; isStaff: boolean; isAdmin: boolean };

/** The header the gateway requires on calls from the page (spec 023, AC-5). */
export const CSRF_HEADER = { 'X-CSRF': '1' } as const;

const listeners = new Set<(customer: Customer | null) => void>();

/** The logged-in customer, or null: the gateway answers 401 without a session. */
export async function currentCustomer(): Promise<Customer | null> {
  const response = await fetch('/bff/user', { headers: CSRF_HEADER });
  if (response.status === 401) return null;
  if (!response.ok) throw new Error(`Could not read the login (${response.status}).`);
  return (await response.json()) as Customer;
}

/** Calls the listener when the login ends on its own (the gateway could not renew it). Returns an unsubscribe function. */
export function onLoginChanged(listener: (customer: Customer | null) => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** The gateway answered 401 to a call: the session is over (spec 023, AC-4); the page asks to log in. */
export function loginEnded(): void {
  for (const listener of listeners) listener(null);
}

/** Keycloak's login page, through the gateway; back to this page afterwards. */
export function logIn(): void {
  window.location.assign(`/bff/login?returnUrl=${encodeURIComponent(window.location.pathname)}`);
}

/** Ends the session at the gateway, then at Keycloak. */
export async function logOut(): Promise<void> {
  const response = await fetch('/bff/logout', { method: 'POST', headers: CSRF_HEADER });
  const { logoutUrl } = response.ok ? ((await response.json()) as { logoutUrl?: string }) : {};
  window.location.assign(logoutUrl ?? '/');
}
