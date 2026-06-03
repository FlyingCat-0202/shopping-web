import { expect, Page, test } from '@playwright/test';

const product = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Trail Jacket',
  price: 89.99,
  stockQuantity: 8,
  description: 'Water resistant shell',
  imageUrl: '',
  categoryId: 1,
  categoryName: 'Outerwear',
};

test.beforeEach(async ({ page }) => {
  let orderState = { status: 'Processing', paymentMethod: 'COD' };

  await page.route('http://localhost:5000/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();

    const json = (payload: unknown, status = 200) =>
      route.fulfill({
        status,
        contentType: 'application/json',
        body: JSON.stringify(payload),
      });

    if (path === '/api/products/' && method === 'GET') {
      return json({
        products: [product],
        categories: [{ id: 1, name: 'Outerwear', description: 'Jackets and shells' }],
        totalItems: 1,
        currentPage: 1,
      });
    }

    if (path === '/api/products/search' && method === 'GET') {
      return json({ items: [product], totalItems: 1, currentPage: 1 });
    }

    if (path === `/api/products/${product.id}` && method === 'GET') {
      return json(product);
    }

    if (path === '/api/auth/login' && method === 'POST') {
      const body = request.postDataJSON();
      const isAdmin = String(body.emailOrPhone).toLowerCase().includes('admin');
      return json({
        token: isAdmin ? 'admin-token' : 'customer-token',
        refreshToken: isAdmin ? 'admin-refresh' : 'customer-refresh',
        userId: isAdmin ? 'admin-1' : 'customer-1',
        fullName: isAdmin ? 'Admin User' : 'Test Customer',
        email: body.emailOrPhone,
        role: isAdmin ? 'Admin' : 'Customer',
      });
    }

    if (path === '/api/payment/providers') {
      return json([{ name: 'MeiMei', providerKey: 'meimei' }]);
    }

    if (path === '/api/cart/' && method === 'GET') {
      const quantity = await cartQuantity(page);
      return json({
        items: quantity > 0 ? [{ productId: product.id, quantity }] : [],
        totalQuantity: quantity,
      });
    }

    if (path === '/api/cart/items' && method === 'POST') {
      await setCartQuantity(page, 1);
      return json({ message: 'Added to cart.' });
    }

    if (path === '/api/order/' && method === 'POST') {
      const body = request.postDataJSON();
      await setCartQuantity(page, body.paymentMethod === 'COD' ? 0 : 1);
      orderState = {
        status: body.paymentMethod === 'COD' ? 'Processing' : 'PaymentPending',
        paymentMethod: body.paymentMethod,
      };
      return json({ orderId: 'order-1', message: 'Order is being processed.' });
    }

    if ((path === '/api/order/' || path === '/api/order/admin') && method === 'GET') {
      return json({ items: [orderSummary(orderState)] });
    }

    if (/^\/api\/order\/[^/]+\/ship\/?$/.test(path) && method === 'PUT') {
      orderState = { status: 'Shipped', paymentMethod: 'COD' };
      return json({ message: 'Order shipped.' });
    }

    if (/^\/api\/order\/[^/]+\/deliver\/?$/.test(path) && method === 'PUT') {
      orderState = { status: 'Delivered', paymentMethod: 'COD' };
      return json({ message: 'Order delivered.' });
    }

    if (path === '/api/payment/orders/query' && method === 'POST') {
      return json([paymentSummary()]);
    }

    if (path === '/api/payment/order/order-1') {
      return json(paymentSummary());
    }

    if (path === '/api/payment/payment-1/providers/meimei/checkout' && method === 'POST') {
      return json({
        provider: 'MeiMei',
        providerKey: 'meimei',
        paymentId: 'payment-1',
        checkoutUrl: 'https://wallet.test/checkout/payment-1',
        expiresAt: new Date(Date.now() + 600_000).toISOString(),
      });
    }

    if (path === '/api/payment/admin/') {
      return json({ items: [] });
    }

    if (path === '/api/notifications/unread-count') {
      return json({ unreadCount: 0 });
    }

    if (path === '/api/notifications/') {
      return json({
        items: [],
        unreadCount: 0,
      });
    }

    return json({});
  });
});

test('renders the storefront shell', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveTitle(/Sech-Team Store/);
  await expect(page.getByRole('heading', { name: 'Sech-Team Store' })).toBeVisible();
  await expect(page.getByRole('searchbox')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Search' })).toBeVisible();
  await expect(page.getByText('Trail Jacket')).toBeVisible();
});

test('customer can open a product detail route and return to catalog', async ({ page }) => {
  await page.goto('/');

  await page.getByRole('link', { name: 'View Trail Jacket' }).click();
  await expect(page).toHaveURL(`/products/${product.id}`);
  await expect(page.getByLabel('Product detail')).toContainText('Trail Jacket');
  await expect(page.getByLabel('Product detail')).toContainText('Water resistant shell');

  await page.getByRole('button', { name: 'Back to products' }).click();
  await expect(page).toHaveURL('/');
  await expect(page.getByRole('searchbox')).toBeVisible();
});

test('customer can login, browse, add cart, and checkout COD', async ({ page }) => {
  await page.goto('/');
  await login(page, 'customer@example.test');

  await page.getByRole('button', { name: 'Add' }).click();
  await expect(page.getByLabel('Shopping cart')).toContainText('Trail Jacket');

  await page.getByRole('button', { name: 'Checkout', exact: true }).click();
  await page.getByLabel('Phone number').fill('0912345678');
  await page.getByLabel('Shipping address').fill('123 Test Street');
  await page.getByRole('button', { name: 'Place order' }).click();

  await expect(page.getByText('Order is being processed.')).toBeVisible();
  await page.getByRole('button', { name: 'Account', exact: true }).click();
  await expect(page.locator('aside[aria-label="Account"]')).toContainText('Processing');
});

test('online checkout exposes wallet payment action', async ({ page }) => {
  await page.goto('/');
  await login(page, 'customer@example.test');
  await page.getByRole('button', { name: 'Add' }).click();
  await expect(page.getByLabel('Shopping cart')).toContainText('Trail Jacket');
  await page.getByRole('button', { name: 'Checkout', exact: true }).click();
  await page.getByLabel('Phone number').fill('0912345678');
  await page.getByLabel('Shipping address').fill('123 Test Street');
  await page.getByLabel('Payment method').selectOption('MeiMei');
  await page.getByRole('button', { name: 'Place order' }).click();

  await expect(page.getByRole('button', { name: 'Pay with MeiMei' })).toBeVisible();
});

test('admin can ship and deliver an order', async ({ page }) => {
  await page.goto('/');
  await login(page, 'admin@shopping.local');
  await page.getByRole('button', { name: 'Account', exact: true }).click();
  const account = page.locator('aside[aria-label="Account"]');

  await account.getByRole('button', { name: 'Ship' }).click({ force: true });
  await expect(page.getByText('Shipped / Unpaid')).toBeVisible();

  await account.getByRole('button', { name: 'Deliver' }).click({ force: true });
  await expect(page.getByText('Delivered / Paid')).toBeVisible();
});

async function login(page: Page, email: string) {
  await page.getByRole('button', { name: 'Account', exact: true }).click();
  const account = page.locator('aside[aria-label="Account"]');
  await account.getByLabel('Email or phone').fill(email);
  await account.getByLabel('Password').fill('Password123');
  await account.locator('button[type="submit"]', { hasText: 'Login' }).click();
  await expect(page.getByText(/Logged in as/)).toBeVisible();
  await account.getByRole('button', { name: 'Close account' }).click();
}

async function setCartQuantity(page: Page, quantity: number) {
  await page.evaluate((value) => window.sessionStorage.setItem('e2e-cart-quantity', String(value)), quantity);
}

async function cartQuantity(page: Page) {
  return page.evaluate(() => Number(window.sessionStorage.getItem('e2e-cart-quantity') ?? 0));
}

function orderSummary(state: { status: string; paymentMethod: string }) {
  const status = state?.status ?? 'Processing';
  const paymentMethod = state?.paymentMethod ?? 'COD';
  const paymentStatus = status === 'Delivered' ? 'Paid' : paymentMethod === 'COD' ? 'Unpaid' : status === 'PaymentPending' ? 'Unpaid' : 'Paid';

  return {
    id: 'order-1',
    customerId: 'customer-1',
    orderDate: new Date().toISOString(),
    totalAmount: 89.99,
    status,
    paymentStatus,
    paymentMethod,
    canCancel: status === 'Processing' && paymentMethod === 'COD',
    canReturn: status === 'Delivered',
  };
}

function paymentSummary() {
  return {
    id: 'payment-1',
    orderId: 'order-1',
    customerId: 'customer-1',
    amount: 89.99,
    paymentMethod: 'MeiMei',
    status: 'Pending',
  };
}
