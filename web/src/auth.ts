import { UserManager, WebStorageStateStore, type User } from 'oidc-client-ts';

/**
 * Customer login (spec 012, ADR 0008): Keycloak, authorization code flow with PKCE.
 * The customer is the token's subject; the API never takes a customer ID from the page.
 * Staff carry the realm role `staff` (spec 015). The access token renews itself with the refresh
 * token a minute before it expires (spec 015, Q4); if it cannot, the login ends.
 */
export type Customer = { id: string; name: string; isStaff: boolean };

const authority = import.meta.env.VITE_AUTH_AUTHORITY ?? 'http://localhost:8180/realms/orderflow';

let token: string | null = null;
const listeners = new Set<(customer: Customer | null) => void>();

function toCustomer(user: User | null): Customer | null {
  if (!user || user.expired) {
    token = null;
    return null;
  }
  token = user.access_token;
  const roles = user.profile.roles;
  return {
    id: user.profile.sub,
    name: user.profile.preferred_username ?? user.profile.sub,
    isStaff: Array.isArray(roles) && roles.includes('staff'),
  };
}

function changed(customer: Customer | null) {
  for (const listener of listeners) listener(customer);
}

/** The login is over (the token expired unrenewed, or the session ended at Keycloak): forget it. */
async function endLogin() {
  token = null;
  await manager().removeUser();
  changed(null);
}

// Created on first use: modules that only need accessToken() (e.g. the Pact test, in Node) never touch the browser.
let userManager: UserManager | undefined;
function manager(): UserManager {
  if (!userManager) {
    userManager = new UserManager({
      authority,
      client_id: 'orderflow-web',
      redirect_uri: `${window.location.origin}/`,
      post_logout_redirect_uri: `${window.location.origin}/`,
      response_type: 'code',
      scope: 'openid profile',
      userStore: new WebStorageStateStore({ store: window.sessionStorage }),
      automaticSilentRenew: true,
    });
    // A renewed token replaces the old one for every later call and (re)connection.
    userManager.events.addUserLoaded((user) => changed(toCustomer(user)));
    userManager.events.addSilentRenewError(() => void endLogin());
    userManager.events.addAccessTokenExpired(() => void endLogin());
  }
  return userManager;
}

/** The logged-in customer, finishing a login if the page is the return from Keycloak. */
export async function currentCustomer(): Promise<Customer | null> {
  const params = new URLSearchParams(window.location.search);
  if (params.has('code') && params.has('state')) {
    const user = await manager().signinRedirectCallback();
    window.history.replaceState({}, document.title, window.location.pathname);
    return toCustomer(user);
  }
  return toCustomer(await manager().getUser());
}

/** Calls the listener whenever the login changes on its own: renewed, or ended. Returns an unsubscribe function. */
export function onLoginChanged(listener: (customer: Customer | null) => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function logIn(): Promise<void> {
  return manager().signinRedirect();
}

export function logOut(): Promise<void> {
  token = null;
  return manager().signoutRedirect();
}

/** The access token for API calls and the hub; null when nobody is logged in. */
export function accessToken(): string | null {
  return token;
}
