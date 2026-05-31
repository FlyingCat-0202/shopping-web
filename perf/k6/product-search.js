import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  scenarios: {
    product_search: {
      executor: 'ramping-vus',
      stages: [
        { duration: '30s', target: 20 },
        { duration: '1m', target: 50 },
        { duration: '30s', target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<500'],
  },
};

const baseUrl = __ENV.API_BASE_URL ?? 'http://localhost:5000';
const keywords = ['jacket', 'shirt', 'bag', 'shoe', 'watch'];

export default function () {
  const keyword = keywords[Math.floor(Math.random() * keywords.length)];
  const response = http.get(`${baseUrl}/api/products/search?Keyword=${keyword}&Page=1&PageSize=12`);

  check(response, {
    'search returns 200': (res) => res.status === 200,
    'search payload is json': (res) => String(res.headers['Content-Type']).includes('application/json'),
  });

  sleep(1);
}
