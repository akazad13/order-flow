// No hidden faults (spec 009, AC-6). A message that failed every retry lands in a MassTransit
// "<queue>_error" queue; one that no consumer handled lands in "<queue>_skipped". Either can
// happen while every E2E scenario still passes, so CI checks the broker after the run.
//
// Prefer live counts from rabbitmqctl (the management API's counts lag by about 5 seconds):
//   docker compose exec -T rabbitmq rabbitmqctl list_queues name messages --formatter json | node scripts/check-error-queues.mjs -
// or ask the management API (RABBITMQ_API, RABBITMQ_USER, RABBITMQ_PASSWORD):
//   node scripts/check-error-queues.mjs
import { text } from 'node:stream/consumers';
import { fileURLToPath } from 'node:url';

/** Queues that hold faulted or skipped messages, from a list of { name, messages }. */
export function faultedQueues(queues) {
  return queues
    .filter((q) => /_(error|skipped)$/.test(q.name) && (q.messages ?? 0) > 0)
    .map((q) => ({ name: q.name, messages: q.messages }));
}

async function fromManagementApi() {
  const api = process.env.RABBITMQ_API ?? 'http://localhost:15672/api';
  const auth = Buffer.from(`${process.env.RABBITMQ_USER ?? 'guest'}:${process.env.RABBITMQ_PASSWORD ?? 'guest'}`).toString('base64');
  const response = await fetch(`${api}/queues`, { headers: { Authorization: `Basic ${auth}` } });
  if (!response.ok) throw new Error(`RabbitMQ management API answered ${response.status}`);
  return response.json();
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const queues = process.argv[2] === '-' ? JSON.parse(await text(process.stdin)) : await fromManagementApi();
  const faulted = faultedQueues(queues);
  for (const q of faulted) console.error(`${q.name}: ${q.messages} message(s)`);
  console.log(faulted.length === 0 ? `No faulted or skipped messages in ${queues.length} queues.` : `${faulted.length} queue(s) hold faulted or skipped messages.`);
  process.exit(faulted.length === 0 ? 0 : 1);
}
