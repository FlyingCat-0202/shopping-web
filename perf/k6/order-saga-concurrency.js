import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  scenarios: {
    checkout_burst: {
      executor: 'constant-arrival-rate',
      rate: Number(__ENV.ORDER_RATE ?? 10),
      timeUnit: '1s',
      duration: __ENV.DURATION ?? '2m',
      preAllocatedVUs: 20,
      maxVUs: 100,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.02'],
    http_req_duration: ['p(95)<800'],
  },
};

const baseUrl = __ENV.API_BASE_URL ?? 'http://localhost:5000';
const token = __ENV.AUTH_TOKEN;
const productId = __ENV.PRODUCT_ID;

export default function () {
  if (!token || !productId) {
    throw new Error('AUTH_TOKEN and PRODUCT_ID are required.');
  }

  const headers = {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
    'x-requestid': `${__VU}-${__ITER}-${Date.now()}`,
  };

  const payload = JSON.stringify({
    receiverName: 'Load Test',
    phoneNumber: '0912345678',
    shippingAddress: '123 Load Test Street',
    paymentMethod: __ENV.PAYMENT_METHOD ?? 'COD',
    items: [{ productId, quantity: 1 }],
  });

  const response = http.post(`${baseUrl}/api/order/`, payload, { headers });
  check(response, {
    'checkout accepted': (res) => res.status === 200 || res.status === 202,
  });

  sleep(1);
}
