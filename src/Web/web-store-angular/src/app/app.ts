import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiConfig, ApiService, AuthState } from './api.service';

interface Product {
  id: string;
  name: string;
  price: number;
  stockQuantity: number;
  description: string;
  imageUrl: string;
  categoryId: number;
  categoryName: string;
}

interface Category {
  id: number;
  name: string;
  description: string;
}

interface CartItem {
  productId: string;
  quantity: number;
}

interface CartLine extends CartItem {
  product: Product;
}

interface OrderSummary {
  id: string;
  customerId?: string;
  orderDate?: string;
  totalAmount: number;
  status: string;
  paymentStatus: string;
  canCancel: boolean;
  canReturn: boolean;
}

interface OrderDetail extends OrderSummary {
  receiverName: string;
  phoneNumber: string;
  shippingAddress: string;
  paymentMethod: string;
  items: OrderDetailItem[];
  timeline: OrderTimelineItem[];
}

interface OrderDetailItem {
  productId: string;
  productName: string;
  productImageUrl?: string;
  quantity: number;
  unitPrice: number;
}

interface OrderTimelineItem {
  id: string;
  status: string;
  title: string;
  description: string;
  source: string;
  occurredAt?: string;
}


interface PaymentSummary {
  id: string;
  orderId: string;
  customerId?: string;
  amount: number;
  paymentMethod: string;
  status: string;
  providerTransactionId?: string;
  failureReason?: string;
  createdAt?: string;
  completedAt?: string;
}

interface PaymentCheckoutResponse {
  provider: string;
  providerKey: string;
  paymentId: string;
  checkoutUrl: string;
  expiresAt: string;
}

interface PaymentProviderOption {
  name: string;
  providerKey: string;
}

interface NotificationSummary {
  id: string;
  customerId: string;
  type: string;
  title: string;
  message: string;
  dataJson?: string;
  isRead: boolean;
  createdAt?: string;
  readAt?: string;
}

interface Chip {
  value: string;
  label: string;
}

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App implements OnInit {
  private readonly api = inject(ApiService);
  private readonly priceFormatter = new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
  });

  readonly fallbackImages = [
    'https://images.unsplash.com/photo-1523275335684-37898b6baf30?auto=format&fit=crop&w=900&q=85',
    'https://images.unsplash.com/photo-1521572163474-6864f9cf17ab?auto=format&fit=crop&w=900&q=85',
    'https://images.unsplash.com/photo-1553062407-98eeb64c6a62?auto=format&fit=crop&w=900&q=85',
    'https://images.unsplash.com/photo-1515886657613-9f3515b0c78f?auto=format&fit=crop&w=900&q=85',
    'https://images.unsplash.com/photo-1542291026-7eec264c27ff?auto=format&fit=crop&w=900&q=85',
    'https://images.unsplash.com/photo-1511556820780-d912e42b4980?auto=format&fit=crop&w=900&q=85',
  ];

  readonly auth = signal<AuthState | null>(this.api.auth);
  readonly apiConfig = signal<ApiConfig>(this.api.config);
  readonly products = signal<Product[]>([]);
  readonly productCache = signal<Record<string, Product>>({});
  readonly categories = signal<Category[]>([]);
  readonly catalogTotal = signal(0);
  readonly cart = signal<CartItem[]>([]);
  readonly orders = signal<OrderSummary[]>([]);
  readonly selectedOrder = signal<OrderDetail | null>(null);
  readonly payments = signal<Record<string, PaymentSummary>>({});
  readonly adminPayments = signal<PaymentSummary[]>([]);
  readonly paymentProviders = signal<PaymentProviderOption[]>(this.defaultPaymentProviders());
  readonly notifications = signal<NotificationSummary[]>([]);
  readonly unreadNotificationCount = signal(0);
  readonly notificationsLoading = signal(false);
  readonly notificationActionBusy = signal<Record<string, boolean>>({});
  readonly notificationUnreadOnly = signal(false);
  readonly checkoutBusy = signal(false);
  readonly confirmingPayments = signal<Record<string, boolean>>({});
  readonly orderActionBusy = signal<Record<string, boolean>>({});
  readonly orderDetailBusy = signal<Record<string, boolean>>({});
  readonly paymentMockBusy = signal<Record<string, boolean>>({});
  readonly adminPaymentsLoading = signal(false);
  readonly searchKeyword = signal('');
  readonly searchAppliedKeyword = signal('');
  readonly searchResults = signal<Product[]>([]);
  readonly searchLoading = signal(false);
  readonly searchTotal = signal(0);
  readonly searchPage = signal(1);
  readonly catalogPage = signal(1);
  readonly productEditorOpen = signal(false);
  readonly productSaveBusy = signal(false);
  readonly productDeleteBusy = signal<Record<string, boolean>>({});
  readonly selectedCategory = signal('All');
  readonly selectedStock = signal('All');
  readonly selectedSort = signal('featured');
  readonly sortMenuOpen = signal(false);
  readonly activeDrawer = signal<'account' | 'cart' | 'notifications' | null>(null);
  readonly checkoutOpen = signal(false);
  readonly authTab = signal<'login' | 'register'>('login');
  readonly connectionState = signal<'loading' | 'live' | 'offline'>('loading');
  readonly toast = signal('');

  readonly stockChips: Chip[] = [
    { value: 'All', label: 'All' },
    { value: 'InStock', label: 'In stock' },
    { value: 'OutOfStock', label: 'Out of stock' },
  ];

  readonly sortOptions: Chip[] = [
    { value: 'featured', label: 'Response order' },
    { value: 'price-asc', label: 'Price low to high' },
    { value: 'price-desc', label: 'Price high to low' },
    { value: 'name', label: 'Name' },
  ];

  readonly categoryChips = computed<Chip[]>(() => [
    { value: 'All', label: 'All' },
    ...this.categories().map((category) => ({
      value: String(category.id),
      label: category.name,
    })),
  ]);

  readonly paymentMethodOptions = computed<Chip[]>(() => [
    { value: 'COD', label: 'COD' },
    ...this.paymentProviders().map((provider) => ({
      value: provider.name,
      label: `${provider.name} Wallet`,
    })),
  ]);

  readonly searchActive = computed(() => this.searchAppliedKeyword().length > 0);
  readonly selectedSortLabel = computed(
    () => this.sortOptions.find((option) => option.value === this.selectedSort())?.label ?? 'Response order',
  );

  readonly filteredProducts = computed(() => {
    if (this.searchActive()) return this.searchResults();

    return this.products();
  });

  readonly visibleProducts = computed(() => {
    const products = this.filteredProducts();

    return products;
  });

  readonly productTotal = computed(() => (this.searchActive() ? this.searchTotal() : this.catalogTotal()));
  readonly productPage = computed(() => (this.searchActive() ? this.searchPage() : this.catalogPage()));
  readonly productPageSize = computed(() => 12);
  readonly productTotalPages = computed(() => Math.max(1, Math.ceil(this.productTotal() / this.productPageSize())));
  readonly productRangeStart = computed(() =>
    this.productTotal() === 0 ? 0 : (this.productPage() - 1) * this.productPageSize() + 1,
  );
  readonly productRangeEnd = computed(() =>
    Math.min(this.productRangeStart() + this.visibleProducts().length - 1, this.productTotal()),
  );
  readonly paginationItems = computed<Array<number | string>>(() => {
    const totalPages = this.productTotalPages();
    const currentPage = this.productPage();

    if (totalPages <= 7) {
      return Array.from({ length: totalPages }, (_, index) => index + 1);
    }

    const items: Array<number | string> = [1];
    const start = Math.max(2, currentPage - 1);
    const end = Math.min(totalPages - 1, currentPage + 1);

    if (start > 2) items.push('ellipsis-left');

    for (let page = start; page <= end; page++) {
      items.push(page);
    }

    if (end < totalPages - 1) items.push('ellipsis-right');

    items.push(totalPages);
    return items;
  });

  readonly isAdmin = computed(() => this.auth()?.role.toLowerCase() === 'admin');
  readonly currentUserId = computed(() => this.auth()?.userId.toLowerCase() ?? '');

  readonly cartLines = computed<CartLine[]>(() =>
    this.cart().map((item) => ({
      ...item,
      product: this.findProduct(item.productId) ?? this.placeholderProduct(item.productId, item.quantity),
    })),
  );
  readonly cartQuantityByProduct = computed(
    () => new Map(this.cart().map((item) => [item.productId, item.quantity] as const)),
  );

  readonly cartCount = computed(() => this.cart().reduce((sum, item) => sum + item.quantity, 0));
  readonly cartTotal = computed(() =>
    this.cartLines().reduce((sum, line) => sum + line.product.price * line.quantity, 0),
  );


  loginModel = { emailOrPhone: 'admin@shopping.local', password: 'Admin123' };
  registerModel = { fullName: '', email: '', phoneNumber: '', password: '' };
  categoryModel = { name: '', description: '' };
  productModel = {
    name: '',
    price: 0,
    stockQuantity: 0,
    categoryId: 0,
    imageUrl: '',
    description: '',
  };
  editProductModel = {
    id: '',
    name: '',
    price: 0,
    stockQuantity: 0,
    categoryId: 0,
    imageUrl: '',
    description: '',
    isActive: true,
  };
  checkoutModel = {
    receiverName: '',
    phoneNumber: '',
    shippingAddress: '',
    paymentMethod: 'COD',
  };
  paymentFilters = {
    status: 'All',
    orderId: '',
    customerId: '',
  };

  async ngOnInit(): Promise<void> {
    await this.loadCatalog();
    await this.loadPaymentProviders();
    await this.loadCart();
    await this.loadOrders();
    await this.loadUnreadNotificationCount();
  }

  async loadCatalog(page = this.catalogPage()): Promise<void> {
    this.connectionState.set('loading');

    try {
      const nextPage = Math.max(1, page);
      const params = new URLSearchParams({
        Page: String(nextPage),
        PageSize: String(this.productPageSize()),
        Sort: this.selectedSort(),
      });

      if (this.selectedCategory() !== 'All') {
        params.set('CategoryId', this.selectedCategory());
      }

      if (this.selectedStock() !== 'All') {
        params.set('Stock', this.selectedStock());
      }

      const payload = await this.api.request<any>('product', `/api/products/?${params.toString()}`);
      const products = payload.products ?? payload.Products ?? [];
      const categories = payload.categories ?? payload.Categories ?? [];
      const normalizedProducts = products.map((product: any, index: number) => this.normalizeProduct(product, index));

      this.products.set(normalizedProducts);
      this.cacheProducts(normalizedProducts);
      this.categories.set(categories.map((category: any) => this.normalizeCategory(category)));
      this.catalogTotal.set(Number(payload.totalItems ?? payload.TotalItems ?? normalizedProducts.length));
      this.catalogPage.set(Number(payload.currentPage ?? payload.CurrentPage ?? nextPage));
      this.connectionState.set('live');

      if (!this.productModel.categoryId && this.categories().length > 0) {
        this.productModel.categoryId = this.categories()[0].id;
      }

      if (this.searchActive()) {
        await this.searchProducts(this.searchPage(), false);
      }
    } catch (error) {
      this.products.set([]);
      this.catalogTotal.set(0);
      this.categories.set([]);
      this.connectionState.set('offline');
      this.showToast(this.api.messageFromError(error));
    }
  }

  async loadCart(): Promise<void> {
    if (!this.auth()) {
      this.cart.set([]);
      return;
    }

    try {
      const payload = await this.api.request<any>('cart', '/api/cart/', { auth: true });
      const items = payload.items ?? payload.Items ?? [];
      const cartItems: CartItem[] = items
        .map((item: any) => this.normalizeCartItem(item))
        .filter((item: CartItem) => item.quantity > 0);

      this.cart.set(cartItems);
      await this.ensureProductsLoaded(cartItems.map((item) => item.productId));
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    }
  }

  async loadOrders(): Promise<void> {
    if (!this.auth()) {
      this.orders.set([]);
      this.payments.set({});
      this.adminPayments.set([]);
      this.notifications.set([]);
      this.unreadNotificationCount.set(0);
      return;
    }

    const path = this.isAdmin()
      ? '/api/order/admin?pageIndex=0&pageSize=10'
      : '/api/order/?pageIndex=0&pageSize=10';

    try {
      const payload = await this.api.request<any>('order', path, { auth: true });
      const orders = payload.items ?? payload.Items ?? [];
      const normalizedOrders = orders.map((order: any) => this.normalizeOrder(order));
      this.orders.set(normalizedOrders);
      await this.loadPaymentsForOrders(normalizedOrders);
      await this.loadUnreadNotificationCount();
      if (this.isAdmin()) await this.loadAdminPayments();
    } catch (error) {
      this.orders.set([]);
      this.payments.set({});
      this.adminPayments.set([]);
      this.showToast(this.api.messageFromError(error));
    }
  }

  async login(): Promise<void> {
    try {
      const response = await this.api.request<any>('identity', '/api/auth/login', {
        method: 'POST',
        body: this.loginModel,
      });

      this.auth.set(this.api.saveAuth(this.api.normalizeAuth(response)));
      await Promise.all([this.loadPaymentProviders(), this.loadCart(), this.loadOrders(), this.loadNotifications(false)]);
      this.showToast(`Logged in as ${this.auth()?.role}.`);
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    }
  }

  async register(): Promise<void> {
    try {
      const response = await this.api.request<any>('identity', '/api/auth/register', {
        method: 'POST',
        body: this.registerModel,
      });

      this.auth.set(this.api.saveAuth(this.api.normalizeAuth(response)));
      await Promise.all([this.loadPaymentProviders(), this.loadCart(), this.loadOrders(), this.loadNotifications(false)]);
      this.showToast('Account created.');
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    }
  }

  logout(): void {
    this.auth.set(this.api.saveAuth(null));
    this.cart.set([]);
    this.orders.set([]);
    this.payments.set({});
    this.adminPayments.set([]);
    this.notifications.set([]);
    this.unreadNotificationCount.set(0);
    this.paymentProviders.set(this.defaultPaymentProviders());
    this.showToast('Logged out.');
  }



  async loadNotifications(showErrors = true): Promise<void> {
    if (!this.auth()) {
      this.notifications.set([]);
      this.unreadNotificationCount.set(0);
      return;
    }

    this.notificationsLoading.set(true);

    try {
      const params = new URLSearchParams({
        pageIndex: '0',
        pageSize: '20',
        unreadOnly: String(this.notificationUnreadOnly()),
      });
      const payload = await this.api.request<any>('notification', `/api/notifications/?${params.toString()}`, { auth: true });
      const items = payload.items ?? payload.Items ?? [];
      const notifications = items.map((item: any) => this.normalizeNotification(item));

      this.notifications.set(notifications);
      await this.loadUnreadNotificationCount();
    } catch (error) {
      this.notifications.set([]);
      if (showErrors) this.showToast(this.api.messageFromError(error));
    } finally {
      this.notificationsLoading.set(false);
    }
  }

  async loadUnreadNotificationCount(): Promise<void> {
    if (!this.auth()) {
      this.unreadNotificationCount.set(0);
      return;
    }

    try {
      const payload = await this.api.request<any>('notification', '/api/notifications/unread-count', { auth: true });
      this.unreadNotificationCount.set(Number(payload.unreadCount ?? payload.UnreadCount ?? 0));
    } catch {
      this.unreadNotificationCount.set(0);
    }
  }

  async markNotificationRead(notification: NotificationSummary): Promise<void> {
    if (notification.isRead || this.notificationActionBusy()[notification.id]) return;

    this.notificationActionBusy.update((current) => ({ ...current, [notification.id]: true }));

    try {
      const response = await this.api.request<any>('notification', `/api/notifications/${notification.id}/read`, {
        method: 'PUT',
        auth: true,
      });
      const updatedNotification = this.normalizeNotification(response);
      this.notifications.update((current) =>
        current.map((item) => (item.id === updatedNotification.id ? updatedNotification : item)),
      );
      await this.loadUnreadNotificationCount();
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    } finally {
      this.notificationActionBusy.update((current) => ({ ...current, [notification.id]: false }));
    }
  }

  async markAllNotificationsRead(): Promise<void> {
    if (this.notificationActionBusy()['read-all']) return;

    this.notificationActionBusy.update((current) => ({ ...current, 'read-all': true }));

    try {
      await this.api.request<any>('notification', '/api/notifications/read-all', {
        method: 'PUT',
        auth: true,
      });
      this.notifications.update((current) =>
        current.map((item) => ({ ...item, isRead: true, readAt: item.readAt ?? new Date().toISOString() })),
      );
      this.unreadNotificationCount.set(0);
      this.showToast('Notifications marked as read.');
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    } finally {
      this.notificationActionBusy.update((current) => ({ ...current, 'read-all': false }));
    }
  }

  setNotificationUnreadOnly(value: boolean): void {
    this.notificationUnreadOnly.set(value);
    void this.loadNotifications();
  }
  async loadPaymentProviders(): Promise<void> {
    if (!this.auth()) {
      this.paymentProviders.set(this.defaultPaymentProviders());
      return;
    }

    try {
      const providers = await this.api.request<any[]>('payment', '/api/payment/providers', { auth: true });
      const normalizedProviders = providers
        .map((provider) => this.normalizePaymentProvider(provider))
        .filter((provider) => provider.name && provider.providerKey);

      this.paymentProviders.set(normalizedProviders.length > 0 ? normalizedProviders : this.defaultPaymentProviders());
    } catch {
      this.paymentProviders.set(this.defaultPaymentProviders());
    }
  }

  async searchProducts(page = 1, showEmptyToast = true): Promise<void> {
    const keyword = this.searchKeyword().trim();
    const nextPage = Math.max(1, page);

    if (!keyword) {
      this.clearSearch();
      return;
    }

    if (this.searchLoading()) return;
    this.searchLoading.set(true);

    try {
      const params = new URLSearchParams({
        Keyword: keyword,
        Page: String(nextPage),
        PageSize: String(this.productPageSize()),
      });

      if (this.selectedCategory() !== 'All') {
        params.set('CategoryId', this.selectedCategory());
      }

      if (this.selectedStock() !== 'All') {
        params.set('Stock', this.selectedStock());
      }

      if (this.selectedSort() !== 'featured') {
        params.set('Sort', this.selectedSort());
      }

      const payload = await this.api.request<any>('product', `/api/products/search?${params.toString()}`);
      const items = payload.items ?? payload.Items ?? [];
      const products = items.map((product: any, index: number) => this.normalizeSearchProduct(product, index));

      this.searchAppliedKeyword.set(keyword);
      this.searchResults.set(products);
      this.cacheProducts(products);
      this.searchTotal.set(Number(payload.totalItems ?? payload.TotalItems ?? products.length));
      this.searchPage.set(Number(payload.currentPage ?? payload.CurrentPage ?? nextPage));

      if (products.length === 0 && showEmptyToast) {
        this.showToast('No products found in search.');
      }
    } catch (error) {
      const fallbackMatches = this.localSearch(keyword);
      const start = (nextPage - 1) * this.productPageSize();
      const fallbackProducts = fallbackMatches.slice(start, start + this.productPageSize());
      this.searchAppliedKeyword.set(keyword);
      this.searchResults.set(fallbackProducts);
      this.searchTotal.set(fallbackMatches.length);
      this.searchPage.set(nextPage);
      if (fallbackProducts.length === 0 && showEmptyToast) {
        this.showToast(`Search service unavailable. No local matches found. ${this.api.messageFromError(error)}`);
      }
    } finally {
      this.searchLoading.set(false);
    }
  }

  clearSearch(): void {
    this.clearSearchState();
    this.catalogPage.set(1);
    void this.loadCatalog(1);
  }

  async goToProductPage(page: number): Promise<void> {
    const nextPage = Math.min(Math.max(1, page), this.productTotalPages());
    if (nextPage === this.productPage()) return;

    if (this.searchActive()) {
      await this.searchProducts(nextPage, false);
      return;
    }

    await this.loadCatalog(nextPage);
  }

  async addToCart(product: Product): Promise<void> {
    if (!this.auth()) {
      this.openAccount();
      this.showToast('Login before adding products to your cart.');
      return;
    }

    if (!this.canAdd(product)) {
      this.showToast(`Only ${product.stockQuantity} item(s) left in stock.`);
      return;
    }

    try {
      await this.api.request('cart', '/api/cart/items', {
        method: 'POST',
        auth: true,
        body: { productId: product.id, quantity: 1 },
      });

      await this.loadCart();
      this.openCart();
      this.showToast('Added to cart.');
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    }
  }

  async updateCartItem(productId: string, quantity: number): Promise<void> {
    try {
      if (quantity <= 0) {
        await this.removeCartItem(productId);
        return;
      }

      await this.api.request('cart', `/api/cart/items/${productId}`, {
        method: 'PUT',
        auth: true,
        body: { productId, quantity },
      });

      await this.loadCart();
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    }
  }

  async removeCartItem(productId: string): Promise<void> {
    try {
      await this.api.request('cart', `/api/cart/items/${productId}`, {
        method: 'DELETE',
        auth: true,
      });

      await this.loadCart();
      this.showToast('Removed from cart.');
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    }
  }

  async checkout(): Promise<void> {
    if (this.checkoutBusy()) return;

    if (!this.auth()) {
      this.openAccount();
      this.showToast('Login before checkout.');
      return;
    }

    if (this.cart().length === 0) {
      this.showToast('Your cart is empty.');
      return;
    }

    this.checkoutBusy.set(true);

    try {
      const response = await this.api.request<any>('order', '/api/order/', {
        method: 'POST',
        auth: true,
        body: {
          ...this.checkoutModel,
          items: this.cart().map((item) => ({
            productId: item.productId,
            quantity: item.quantity,
          })),
        },
      });

      const usesOnlinePayment = this.checkoutModel.paymentMethod !== 'COD';

      if (!usesOnlinePayment) {
        this.cart.set([]);
      }

      this.checkoutOpen.set(false);
      if (usesOnlinePayment) {
        this.openAccount();
      } else {
        this.activeDrawer.set(null);
      }

      await this.loadOrders();
      this.showToast(response.message ?? response.Message ?? 'Order is being processed.');

      window.setTimeout(() => {
        void this.loadOrders();
        void this.loadCart();
        void this.loadNotifications(false);
      }, 1500);
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    } finally {
      this.checkoutBusy.set(false);
    }
  }

  async createCategory(): Promise<void> {
    try {
      await this.api.request('product', '/api/products/categories', {
        method: 'POST',
        auth: true,
        body: {
          name: this.categoryModel.name.trim(),
          description: this.optionalString(this.categoryModel.description),
        },
      });

      this.categoryModel = { name: '', description: '' };
      await this.loadCatalog();
      this.showToast('Category created.');
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    }
  }

  async createProduct(): Promise<void> {
    try {
      await this.api.request('product', '/api/products/', {
        method: 'POST',
        auth: true,
        body: {
          name: this.productModel.name.trim(),
          price: Number(this.productModel.price),
          stockQuantity: Number(this.productModel.stockQuantity),
          categoryId: Number(this.productModel.categoryId),
          imageUrl: this.optionalString(this.productModel.imageUrl),
          description: this.optionalString(this.productModel.description),
          isActive: true,
        },
      });

      this.productModel = {
        name: '',
        price: 0,
        stockQuantity: 0,
        categoryId: this.categories()[0]?.id ?? 0,
        imageUrl: '',
        description: '',
      };
      await this.loadCatalog();
      this.showToast('Product created.');

      window.setTimeout(() => {
        void this.loadCatalog();
      }, 1500);
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    }
  }

  startEditProduct(product: Product): void {
    this.editProductModel = {
      id: product.id,
      name: product.name,
      price: product.price,
      stockQuantity: product.stockQuantity,
      categoryId: product.categoryId,
      imageUrl: product.imageUrl,
      description: product.description,
      isActive: true,
    };
    this.productEditorOpen.set(true);
  }

  closeProductEditor(): void {
    if (this.productSaveBusy()) return;
    this.productEditorOpen.set(false);
  }

  async updateProduct(): Promise<void> {
    if (this.productSaveBusy()) return;
    this.productSaveBusy.set(true);

    try {
      await this.api.request('product', `/api/products/${this.editProductModel.id}`, {
        method: 'PUT',
        auth: true,
        body: {
          id: this.editProductModel.id,
          name: this.editProductModel.name.trim(),
          price: Number(this.editProductModel.price),
          stockQuantity: Number(this.editProductModel.stockQuantity),
          isActive: this.editProductModel.isActive,
          description: this.optionalString(this.editProductModel.description),
          imgUrl: this.optionalString(this.editProductModel.imageUrl),
          categoryId: Number(this.editProductModel.categoryId),
        },
      });

      this.productEditorOpen.set(false);
      await this.loadCatalog();
      this.showToast('Product update queued.');

      window.setTimeout(() => {
        void this.loadCatalog();
      }, 1500);
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    } finally {
      this.productSaveBusy.set(false);
    }
  }

  async deleteProduct(product: Product): Promise<void> {
    if (this.productDeleteBusy()[product.id]) return;

    const confirmed = window.confirm(`Delete ${product.name}?`);
    if (!confirmed) return;

    this.productDeleteBusy.update((current) => ({ ...current, [product.id]: true }));

    try {
      await this.api.request('product', `/api/products/${product.id}`, {
        method: 'DELETE',
        auth: true,
      });

      this.products.update((current) => current.filter((item) => item.id !== product.id));
      this.searchResults.update((current) => current.filter((item) => item.id !== product.id));
      this.productCache.update((current) => {
        const next = { ...current };
        delete next[product.id];
        return next;
      });
      this.catalogTotal.update((total) => Math.max(0, total - 1));
      this.showToast('Product delete queued.');

      window.setTimeout(() => {
        void this.loadCatalog();
      }, 1500);
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    } finally {
      this.productDeleteBusy.update((current) => ({ ...current, [product.id]: false }));
    }
  }

  async orderAction(order: OrderSummary, action: string): Promise<void> {
    const actionKey = this.orderActionKey(order, action);
    if (this.orderActionBusy()[actionKey]) return;

    const path = {
      cancel: 'cancel',
      ship: 'ship',
      deliver: 'deliver',
      returnRequest: 'return-request',
      returnApprove: 'return-approve',
      returnReject: 'return-reject',
    }[action];

    if (!path) return;

    this.orderActionBusy.update((current) => ({ ...current, [actionKey]: true }));

    try {
      const response = await this.api.request<any>('order', `/api/order/${order.id}/${path}`, {
        method: 'PUT',
        auth: true,
      });

      await this.loadOrders();
      await this.loadNotifications(false);
      this.showToast(response.message ?? response.Message ?? 'Order updated.');
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    } finally {
      this.orderActionBusy.update((current) => ({ ...current, [actionKey]: false }));
    }
  }

  async openOrderDetail(order: OrderSummary): Promise<void> {
    if (this.orderDetailBusy()[order.id]) return;

    this.orderDetailBusy.update((current) => ({ ...current, [order.id]: true }));

    try {
      const detailPath = this.isAdmin()
        ? `/api/order/admin/${order.id}`
        : `/api/order/${order.id}`;
      const response = await this.api.request<any>('order', detailPath, { auth: true });
      this.selectedOrder.set(this.normalizeOrderDetail(response));
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    } finally {
      this.orderDetailBusy.update((current) => ({ ...current, [order.id]: false }));
    }
  }

  closeOrderDetail(): void {
    this.selectedOrder.set(null);
  }
  async confirmPayment(order: OrderSummary): Promise<void> {
    if (this.confirmingPayments()[order.id]) return;

    this.confirmingPayments.update((current) => ({ ...current, [order.id]: true }));

    try {
      const payment = await this.ensurePaymentForOrder(order);

      if (!payment) {
        this.showToast('Payment is not ready yet. Reload orders in a moment.');
        return;
      }

      const provider = this.paymentProviderKey(payment.paymentMethod);

      if (!provider) {
        this.showToast(`${payment.paymentMethod} does not have a checkout provider yet.`);
        return;
      }

      const checkout = await this.api.request<PaymentCheckoutResponse>('payment', `/api/payment/${payment.id}/providers/${provider}/checkout`, {
        method: 'POST',
        auth: true,
      });

      window.open(checkout.checkoutUrl, '_blank', 'noopener,noreferrer');

      this.showToast(`${checkout.provider} checkout opened. Complete the payment in the new tab.`);

      window.setTimeout(() => {
        void this.loadOrders();
        void this.loadCart();
        void this.loadNotifications(false);
      }, 1500);

      window.setTimeout(() => {
        void this.loadOrders();
        void this.loadCart();
        void this.loadNotifications(false);
      }, 5000);
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    } finally {
      this.confirmingPayments.update((current) => ({ ...current, [order.id]: false }));
    }
  }

  async loadAdminPayments(): Promise<void> {
    if (!this.isAdmin()) return;

    this.adminPaymentsLoading.set(true);

    try {
      const params = new URLSearchParams({ pageIndex: '0', pageSize: '20' });
      if (this.paymentFilters.status !== 'All') params.set('status', this.paymentFilters.status);
      if (this.paymentFilters.orderId.trim()) params.set('orderId', this.paymentFilters.orderId.trim());
      if (this.paymentFilters.customerId.trim()) params.set('customerId', this.paymentFilters.customerId.trim());

      const payload = await this.api.request<any>('payment', `/api/payment/admin/?${params.toString()}`, { auth: true });
      const items = payload.items ?? payload.Items ?? [];
      this.adminPayments.set(items.map((payment: any) => this.normalizePayment(payment)));
    } catch (error) {
      this.adminPayments.set([]);
      this.showToast(this.api.messageFromError(error));
    } finally {
      this.adminPaymentsLoading.set(false);
    }
  }

  async mockPayment(payment: PaymentSummary, success: boolean): Promise<void> {
    const actionKey = `${payment.id}:${success ? 'success' : 'fail'}`;
    if (this.paymentMockBusy()[actionKey]) return;

    this.paymentMockBusy.update((current) => ({ ...current, [actionKey]: true }));

    try {
      const response = await this.api.request<any>('payment', `/api/payment/admin/${payment.id}/mock-webhook`, {
        method: 'POST',
        auth: true,
        body: {
          success,
          providerTransactionId: success ? `admin-mock-${payment.id}` : null,
          reason: success ? null : 'Admin mock payment failure.',
        },
      });

      const updatedPayment = this.normalizePayment(response);
      this.adminPayments.update((current) =>
        current.map((item) => (item.id === updatedPayment.id ? updatedPayment : item)),
      );
      this.payments.update((current) => ({
        ...current,
        [updatedPayment.orderId]: updatedPayment,
      }));

      await this.loadOrders();
      this.showToast(success ? 'Mock payment succeeded.' : 'Mock payment failed.');

      window.setTimeout(() => {
        void this.loadOrders();
        void this.loadCart();
        void this.loadNotifications(false);
        void this.loadAdminPayments();
      }, 1500);
    } catch (error) {
      this.showToast(this.api.messageFromError(error));
    } finally {
      this.paymentMockBusy.update((current) => ({ ...current, [actionKey]: false }));
    }
  }

  setCategory(value: string): void {
    this.selectedCategory.set(value);

    if (this.searchActive()) {
      this.searchPage.set(1);
      void this.searchProducts(1, false);
      return;
    }

    this.resetProductPage();
  }

  setStock(value: string): void {
    this.selectedStock.set(value);

    if (this.searchActive()) {
      this.searchPage.set(1);
      void this.searchProducts(1, false);
      return;
    }

    this.resetProductPage();
  }

  setSort(value: string): void {
    this.selectedSort.set(value);
    this.sortMenuOpen.set(false);

    if (this.searchActive()) {
      this.searchPage.set(1);
      void this.searchProducts(1, false);
      return;
    }

    this.resetProductPage();
  }

  toggleSortMenu(): void {
    this.sortMenuOpen.update((isOpen) => !isOpen);
  }

  closeSortMenu(): void {
    this.sortMenuOpen.set(false);
  }

  openAccount(): void {
    this.activeDrawer.set('account');
    if (this.auth()) void this.loadOrders();
  }

  openCart(): void {
    this.activeDrawer.set('cart');
    void this.loadCart();
  }

  openNotifications(): void {
    if (!this.auth()) {
      this.openAccount();
      this.showToast('Login to view notifications.');
      return;
    }

    this.activeDrawer.set('notifications');
    void this.loadNotifications();
  }

  closeDrawer(): void {
    this.activeDrawer.set(null);
  }

  openCheckout(): void {
    if (!this.auth()) {
      this.openAccount();
      this.showToast('Login before checkout.');
      return;
    }

    if (this.cart().length === 0) {
      this.showToast('Your cart is empty.');
      return;
    }

    this.checkoutModel.receiverName = this.auth()?.fullName ?? '';
    this.checkoutOpen.set(true);
  }

  closeCheckout(): void {
    this.checkoutOpen.set(false);
  }

  cartQuantity(productId: string): number {
    return this.cartQuantityByProduct().get(productId) ?? 0;
  }

  canAdd(product: Product): boolean {
    return Boolean(this.auth()) && product.stockQuantity > 0 && this.cartQuantity(product.id) < product.stockQuantity;
  }

  addButtonText(product: Product): string {
    if (!this.auth()) return 'Login';
    if (product.stockQuantity <= 0) return 'Sold out';
    if (this.cartQuantity(product.id) >= product.stockQuantity) return 'Max';
    return 'Add';
  }

  paymentFor(order: OrderSummary): PaymentSummary | undefined {
    return this.payments()[order.id];
  }

  canConfirmPayment(order: OrderSummary): boolean {
    if (order.status !== 'PaymentPending') return false;
    if (!this.isAdmin()) return true;

    return Boolean(order.customerId && order.customerId.toLowerCase() === this.currentUserId());
  }

  isConfirmingPayment(order: OrderSummary): boolean {
    return Boolean(this.confirmingPayments()[order.id]);
  }

  payButtonText(order: OrderSummary): string {
    const paymentMethod = this.paymentFor(order)?.paymentMethod;
    return paymentMethod ? `Pay with ${paymentMethod}` : 'Pay now';
  }

  isOrderActionBusy(order: OrderSummary, action: string): boolean {
    return Boolean(this.orderActionBusy()[this.orderActionKey(order, action)]);
  }

  isOrderDetailBusy(order: OrderSummary): boolean {
    return Boolean(this.orderDetailBusy()[order.id]);
  }

  isMockingPayment(payment: PaymentSummary, success: boolean): boolean {
    return Boolean(this.paymentMockBusy()[`${payment.id}:${success ? 'success' : 'fail'}`]);
  }

  isNotificationBusy(notification: NotificationSummary): boolean {
    return Boolean(this.notificationActionBusy()[notification.id]);
  }

  isMarkingAllNotificationsRead(): boolean {
    return Boolean(this.notificationActionBusy()['read-all']);
  }

  isDeletingProduct(product: Product): boolean {
    return Boolean(this.productDeleteBusy()[product.id]);
  }

  canMockPayment(payment: PaymentSummary): boolean {
    return payment.status === 'Pending';
  }

  paymentStatusClass(status: string): string {
    return `is-${status.toLowerCase()}`;
  }

  formatPrice(value: number): string {
    return this.priceFormatter.format(value);
  }

  formatDate(value?: string): string {
    return value ? new Date(value).toLocaleString() : '';
  }

  shortId(value?: string): string {
    return value && value.length > 8 ? value.slice(0, 8) : (value ?? '');
  }

  trackByValue(_: number, chip: Chip): string {
    return chip.value;
  }

  trackBySortOption(_: number, option: Chip): string {
    return option.value;
  }

  trackByProduct(_: number, product: Product): string {
    return product.id;
  }

  trackByPaginationItem(_: number, item: number | string): string {
    return String(item);
  }

  isPaginationPage(item: number | string): item is number {
    return typeof item === 'number';
  }

  trackByCartLine(_: number, line: CartLine): string {
    return line.productId;
  }

  trackByOrder(_: number, order: OrderSummary): string {
    return order.id;
  }

  trackByOrderDetailItem(_: number, item: OrderDetailItem): string {
    return item.productId;
  }

  trackByTimelineItem(_: number, item: OrderTimelineItem): string {
    return item.id;
  }

  trackByPayment(_: number, payment: PaymentSummary): string {
    return payment.id;
  }

  trackByNotification(_: number, notification: NotificationSummary): string {
    return notification.id;
  }

  private normalizeProduct(product: any, index: number): Product {
    return {
      id: String(product.id ?? product.Id),
      name: product.name ?? product.Name ?? 'Unnamed product',
      price: Number(product.price ?? product.Price ?? 0),
      stockQuantity: Number(product.stockQuantity ?? product.StockQuantity ?? 0),
      description: product.description ?? product.Description ?? '',
      imageUrl: product.imageUrl || product.ImageUrl || this.fallbackImages[index % this.fallbackImages.length],
      categoryId: Number(product.categoryId ?? product.CategoryId ?? 0),
      categoryName: product.categoryName ?? product.CategoryName ?? 'Uncategorized',
    };
  }

  private normalizeCategory(category: any): Category {
    return {
      id: Number(category.id ?? category.Id ?? 0),
      name: category.name ?? category.Name ?? 'Unnamed category',
      description: category.description ?? category.Description ?? '',
    };
  }

  private normalizeCartItem(item: any): CartItem {
    return {
      productId: String(item.productId ?? item.ProductId),
      quantity: Number(item.quantity ?? item.Quantity ?? 0),
    };
  }

  private normalizeOrder(order: any): OrderSummary {
    return {
      id: String(order.id ?? order.Id),
      customerId: order.customerId ?? order.CustomerId,
      orderDate: order.orderDate ?? order.OrderDate,
      totalAmount: Number(order.totalAmount ?? order.TotalAmount ?? 0),
      status: order.status ?? order.Status ?? 'Unknown',
      paymentStatus: order.paymentStatus ?? order.PaymentStatus ?? 'Unknown',
      canCancel: Boolean(order.canCancel ?? order.CanCancel),
      canReturn: Boolean(order.canReturn ?? order.CanReturn),
    };
  }

private normalizeOrderDetail(order: any): OrderDetail {
    const base = this.normalizeOrder(order);
    const items = order.items ?? order.Items ?? [];
    const timeline = order.timeline ?? order.Timeline ?? [];

    return {
      ...base,
      receiverName: order.receiverName ?? order.ReceiverName ?? '',
      phoneNumber: order.phoneNumber ?? order.PhoneNumber ?? '',
      shippingAddress: order.shippingAddress ?? order.ShippingAddress ?? '',
      paymentMethod: order.paymentMethod ?? order.PaymentMethod ?? '',
      items: items.map((item: any) => this.normalizeOrderDetailItem(item)),
      timeline: timeline.map((item: any) => this.normalizeTimelineItem(item)),
    };
  }

  private normalizeOrderDetailItem(item: any): OrderDetailItem {
    return {
      productId: String(item.productId ?? item.ProductId),
      productName: item.productName ?? item.ProductName ?? `Product ${this.shortId(String(item.productId ?? item.ProductId))}`,
      productImageUrl: item.productImageUrl ?? item.ProductImageUrl,
      quantity: Number(item.quantity ?? item.Quantity ?? 0),
      unitPrice: Number(item.unitPrice ?? item.UnitPrice ?? 0),
    };
  }

  private normalizeTimelineItem(item: any): OrderTimelineItem {
    return {
      id: String(item.id ?? item.Id),
      status: item.status ?? item.Status ?? 'Unknown',
      title: item.title ?? item.Title ?? 'Order update',
      description: item.description ?? item.Description ?? '',
      source: item.source ?? item.Source ?? 'Order',
      occurredAt: item.occurredAt ?? item.OccurredAt,
    };
  }

  private normalizePayment(payment: any): PaymentSummary {
    return {
      id: String(payment.id ?? payment.Id),
      orderId: String(payment.orderId ?? payment.OrderId),
      customerId: payment.customerId ?? payment.CustomerId,
      amount: Number(payment.amount ?? payment.Amount ?? 0),
      paymentMethod: payment.paymentMethod ?? payment.PaymentMethod ?? '',
      status: payment.status ?? payment.Status ?? 'Unknown',
      providerTransactionId: payment.providerTransactionId ?? payment.ProviderTransactionId,
      failureReason: payment.failureReason ?? payment.FailureReason,
      createdAt: payment.createdAt ?? payment.CreatedAt,
      completedAt: payment.completedAt ?? payment.CompletedAt,
    };
  }

  private normalizePaymentProvider(provider: any): PaymentProviderOption {
    return {
      name: provider.name ?? provider.Name ?? '',
      providerKey: provider.providerKey ?? provider.ProviderKey ?? '',
    };
  }

  private normalizeNotification(notification: any): NotificationSummary {
    return {
      id: String(notification.id ?? notification.Id),
      customerId: String(notification.customerId ?? notification.CustomerId ?? ''),
      type: notification.type ?? notification.Type ?? 'General',
      title: notification.title ?? notification.Title ?? 'Notification',
      message: notification.message ?? notification.Message ?? '',
      dataJson: notification.dataJson ?? notification.DataJson,
      isRead: Boolean(notification.isRead ?? notification.IsRead),
      createdAt: notification.createdAt ?? notification.CreatedAt,
      readAt: notification.readAt ?? notification.ReadAt,
    };
  }

  private normalizeSearchProduct(product: any, index: number): Product {
    const id = String(product.id ?? product.Id);
    const existingProduct = this.findProduct(id);
    const categoryName = product.categoryName ?? product.CategoryName ?? existingProduct?.categoryName ?? 'Uncategorized';
    const rawCategoryId = Number(product.categoryId ?? product.CategoryId ?? existingProduct?.categoryId ?? 0);

    return {
      id,
      name: product.name ?? product.Name ?? existingProduct?.name ?? 'Unnamed product',
      price: Number(product.price ?? product.Price ?? existingProduct?.price ?? 0),
      stockQuantity: Number(product.stockQuantity ?? product.StockQuantity ?? existingProduct?.stockQuantity ?? 0),
      description: product.description ?? product.Description ?? existingProduct?.description ?? '',
      imageUrl:
        product.imageUrl ||
        product.ImageUrl ||
        existingProduct?.imageUrl ||
        this.fallbackImages[index % this.fallbackImages.length],
      categoryId: rawCategoryId > 0 ? rawCategoryId : this.categoryIdFromName(categoryName) ?? 0,
      categoryName,
    };
  }

  private paymentProviderKey(paymentMethod: string): string | null {
    const normalized = paymentMethod.toLowerCase();
    const provider = this.paymentProviders().find(
      (item) => item.name.toLowerCase() === normalized || item.providerKey.toLowerCase() === normalized,
    );

    return provider?.providerKey ?? null;
  }

  private async loadPaymentsForOrders(orders: OrderSummary[]): Promise<void> {
    const ordersToLoad = this.isAdmin()
      ? orders.filter((order) => order.customerId?.toLowerCase() === this.currentUserId())
      : orders;

    if (ordersToLoad.length === 0) {
      this.payments.set({});
      return;
    }

    const payments = await Promise.all(ordersToLoad.map((order) => this.fetchPaymentForOrder(order.id)));
    const byOrderId = payments
      .filter((payment): payment is PaymentSummary => Boolean(payment))
      .reduce<Record<string, PaymentSummary>>((current, payment) => {
        current[payment.orderId] = payment;
        return current;
      }, {});

    this.payments.set(byOrderId);
  }

  private async ensurePaymentForOrder(order: OrderSummary): Promise<PaymentSummary | null> {
    const cachedPayment = this.paymentFor(order);
    if (cachedPayment) return cachedPayment;

    const payment = await this.fetchPaymentForOrder(order.id);

    if (payment) {
      this.payments.update((current) => ({
        ...current,
        [payment.orderId]: payment,
      }));
    }

    return payment;
  }

  private async fetchPaymentForOrder(orderId: string): Promise<PaymentSummary | null> {
    try {
      const response = await this.api.request<any>('payment', `/api/payment/order/${orderId}`, { auth: true });
      return this.normalizePayment(response);
    } catch {
      return null;
    }
  }

  private async ensureProductsLoaded(productIds: string[]): Promise<void> {
    const missingIds = Array.from(new Set(productIds))
      .filter((productId) => productId && !this.findProduct(productId));

    if (missingIds.length === 0) return;

    const products = await Promise.all(missingIds.map((productId) => this.fetchProduct(productId)));
    this.cacheProducts(products.filter((product): product is Product => Boolean(product)));
  }

  private async fetchProduct(productId: string): Promise<Product | null> {
    try {
      const response = await this.api.request<any>('product', `/api/products/${productId}`);
      return this.normalizeProduct(response, 0);
    } catch {
      return null;
    }
  }

  private cacheProducts(products: Product[]): void {
    if (products.length === 0) return;

    this.productCache.update((current) => {
      const next = { ...current };
      for (const product of products) {
        next[product.id] = product;
      }

      return next;
    });
  }

  private findProduct(productId: string): Product | undefined {
    return (
      this.products().find((product) => product.id === productId) ??
      this.searchResults().find((product) => product.id === productId) ??
      this.productCache()[productId]
    );
  }

  private placeholderProduct(productId: string, quantity: number): Product {
    return {
      id: productId,
      name: `Product ${this.shortId(productId)}`,
      price: 0,
      stockQuantity: quantity,
      description: '',
      imageUrl: this.fallbackImages[0],
      categoryId: 0,
      categoryName: 'Unknown',
    };
  }

  private localSearch(keyword: string): Product[] {
    const normalizedKeyword = keyword.toLowerCase();
    const selectedCategory = this.selectedCategory();
    const selectedStock = this.selectedStock();
    const selectedSort = this.selectedSort();

    const matches = Object.values(this.productCache()).filter((product) => {
      const categoryMatch = selectedCategory === 'All' || String(product.categoryId) === selectedCategory;
      const stockMatch =
        selectedStock === 'All' ||
        (selectedStock === 'InStock' && product.stockQuantity > 0) ||
        (selectedStock === 'OutOfStock' && product.stockQuantity <= 0);
      const keywordMatch = [product.name, product.categoryName, product.description]
        .filter(Boolean)
        .some((value) => value.toLowerCase().includes(normalizedKeyword));

      return categoryMatch && stockMatch && keywordMatch;
    });

    if (selectedSort === 'price-asc') return [...matches].sort((a, b) => a.price - b.price);
    if (selectedSort === 'price-desc') return [...matches].sort((a, b) => b.price - a.price);
    if (selectedSort === 'name') return [...matches].sort((a, b) => a.name.localeCompare(b.name));

    return matches;
  }

  private categoryIdFromName(categoryName: string): number | null {
    const category = this.categories().find(
      (item) => item.name.toLowerCase() === categoryName.toLowerCase(),
    );

    return category?.id ?? null;
  }

  private defaultPaymentProviders(): PaymentProviderOption[] {
    return [
      { name: 'MeiMei', providerKey: 'meimei' },
      { name: 'MeilyMeily', providerKey: 'meilymeily' },
    ];
  }

  private optionalString(value: string): string | null {
    const text = value.trim();
    return text.length === 0 ? null : text;
  }

  private orderActionKey(order: OrderSummary, action: string): string {
    return `${order.id}:${action}`;
  }

  private resetProductPage(): void {
    this.catalogPage.set(1);
    void this.loadCatalog(1);
  }

  private clearSearchState(): void {
    this.searchKeyword.set('');
    this.searchAppliedKeyword.set('');
    this.searchResults.set([]);
    this.searchTotal.set(0);
    this.searchPage.set(1);
  }

  private showToast(message: string): void {
    this.toast.set(message);
    window.setTimeout(() => this.toast.set(''), 3200);
  }
}
