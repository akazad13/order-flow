# Plan 021: Only the shop and Keycloak are reachable in production

Spec: [spec.md](spec.md)

## Approach

In `deploy/prod/docker-compose.prod.yml`, the services, PostgreSQL and RabbitMQ publish no port (`ports: !reset []`); only the web server (5443) and Keycloak (8443) do (R-1). With nothing to poll from outside, each service gets a compose health check (an HTTP request to `/health` over bash's `/dev/tcp`; the image has no curl), and the profile starts with `up --wait`. `wait-for-stack.mjs --prod` waits only for the two doors.

The smoke test checks the doors (a TCP connection to each port: the two answer, the seven others refuse), and moves its "nothing test-only" checks: setting stock through the web server's `/inventory-api`, and the payment test switch from inside the network, with a request from the Order container (Q2).

## Services affected

| Area | Change |
| --- | --- |
| Production profile | Ports closed; health checks for the four services |
| Smoke test | Port check; test-only checks through the web server and from inside |
| `wait-for-stack.mjs --prod`, CI, README | `up --wait`; only the two doors polled |

## Contract changes

None.

## Test plan (criterion to layer)

| Criterion | Smoke | Scenario / test |
| --- | --- | --- |
| AC-1 Two doors | x | `smoke.spec.ts` "Only the shop and Keycloak are reachable from outside" |
| AC-2 The shop still works through its door | x | `smoke.spec.ts` "A customer logs in through the real page over HTTPS and an order is confirmed" |
| AC-3 The checks move inside | x | `smoke.spec.ts` "Nothing test-only is switched on" |
| AC-4 Ready means healthy | x | `docker compose up --wait` in the `prod-profile` job of `.github/workflows/ci.yml` (fails on an unhealthy service) |
| R-1 Reachable from inside only | x | `smoke.spec.ts` (the seven closed ports; the payment check from inside) |

## Constitution check

- [x] Spec first; [x] no contract changes; [x] every AC automated; [x] no sleeps; [x] no banned packages

## Risks and decisions

- **Red first.** Against the spec 019 profile, the port check failed at once: Order answered on 5101.
- **Found: the health checks first failed everywhere.** `CMD-SHELL` runs the check with `/bin/sh`, which in these images is dash, without bash's `/dev/tcp`; `up --wait` reported Payment unhealthy (the smoke test itself passed, which is why `--wait` is in CI). The check now runs `bash -c` explicitly; all four healthy.
- **From scratch, as CI does it:** `down -v`, `up --build --wait` (exit 0), smoke test 7 of 7.
