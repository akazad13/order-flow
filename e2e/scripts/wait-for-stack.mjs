// Waits until every service in docker compose answers, so E2E never starts against a half-up stack.
const targets = [
  'http://localhost:5101/health',
  'http://localhost:5102/health',
  'http://localhost:5103/health',
  'http://localhost:5104/health',
  'http://localhost:5180/',
  'http://localhost:8180/realms/orderflow/.well-known/openid-configuration', // Keycloak (spec 012)
];
const deadline = Date.now() + 120_000;

for (const url of targets) {
  for (;;) {
    try {
      const response = await fetch(url);
      if (response.ok) {
        console.log(`ready: ${url}`);
        break;
      }
    } catch {
      // not listening yet
    }
    if (Date.now() > deadline) {
      console.error(`timed out waiting for ${url}`);
      process.exit(1);
    }
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
}
