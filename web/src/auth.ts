import { UserManager, WebStorageStateStore, type User } from 'oidc-client-ts';

/**
 * Customer login (spec 012, ADR 0008): Keycloak, authorization code flow with PKCE.
 * The customer is the token's subject; the API never takes a customer ID from the page.
 */
export type Customer = { id: string; name: string };

const authority = import.meta.env.VITE_AUTH_AUTHORITY ?? 'http://localhost:8180/realms/orderflow';

// Created on first use: modules that only need accessToken() (e.g. the Pact test, in Node) never touch the browser.
let userManager: UserManager | undefined;
function manager(): UserManager {
  userManager ??= new UserManager({
    authority,
    client_id: 'orderflow-web',
    redirect_uri: `${window.location.origin}/`,
    post_logout_redirect_uri: `${window.location.origin}/`,
    response_type: 'code',
    scope: 'openid profile',
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  });
  return userManager;
}

let token: string | null = null;

function toCustomer(user: User | null): Customer | null {
  if (!user || user.expired) {
    token = null;
    return null;
  }
  token = user.access_token;
  return { id: user.profile.sub, name: user.profile.preferred_username ?? user.profile.sub };
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
