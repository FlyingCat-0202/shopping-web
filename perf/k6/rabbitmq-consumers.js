import http from 'k6/http';
import { check, sleep } from 'k6';
import encoding from 'k6/encoding';

export const options = {
  scenarios: {
    rabbitmq_queue_depth: {
      executor: 'constant-vus',
      vus: 5,
      duration: __ENV.DURATION ?? '2m',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<300'],
  },
};

const managementUrl = __ENV.RABBITMQ_MANAGEMENT_URL ?? 'http://localhost:15672';
const username = __ENV.RABBITMQ_USER ?? 'guest';
const password = __ENV.RABBITMQ_PASSWORD ?? 'guest';
const queue = __ENV.RABBITMQ_QUEUE ?? 'order-submitted';
const vhost = encodeURIComponent(__ENV.RABBITMQ_VHOST ?? '/');

export default function () {
  const credentials = encoding.b64encode(`${username}:${password}`);
  const response = http.get(`${managementUrl}/api/queues/${vhost}/${queue}`, {
    headers: { Authorization: `Basic ${credentials}` },
  });

  check(response, {
    'queue metrics available': (res) => res.status === 200,
    'consumer backlog below limit': (res) => {
      const payload = res.json();
      return Number(payload.messages_ready ?? 0) < Number(__ENV.MAX_READY ?? 1000);
    },
  });

  sleep(1);
}
