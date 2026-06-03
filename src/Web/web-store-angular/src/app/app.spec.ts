import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { ApiConfig, ApiService, AuthState } from './api.service';
import { StorePage } from './store/store-page';

class ApiServiceStub {
  private authState: AuthState | null = null;
  requestHandler?: (service: keyof ApiConfig, path: string) => Promise<unknown>;

  readonly config: ApiConfig = {
    identity: 'http://localhost:5000',
    product: 'http://localhost:5000',
    cart: 'http://localhost:5000',
    order: 'http://localhost:5000',
    payment: 'http://localhost:5000',
    notification: 'http://localhost:5000',
  };

  get auth(): AuthState | null {
    return this.authState;
  }

  saveAuth(auth: AuthState | null): AuthState | null {
    this.authState = auth;
    return auth;
  }

  normalizeAuth(response: any): AuthState {
    return {
      token: response.token,
      refreshToken: response.refreshToken,
      userId: response.userId,
      fullName: response.fullName,
      email: response.email,
      role: response.role,
    };
  }

  messageFromError(error: unknown): string {
    return error instanceof Error ? error.message : 'Request failed.';
  }

  async request<T>(service: keyof ApiConfig, path: string): Promise<T> {
    if (this.requestHandler) {
      return (await this.requestHandler(service, path)) as T;
    }

    if (path.startsWith('/api/products/?')) {
      return {
        products: [
          {
            id: 'product-1',
            name: 'Trail Jacket',
            price: 89.99,
            stockQuantity: 4,
            description: 'Water resistant shell',
            imageUrl: '',
            categoryId: 1,
            categoryName: 'Outerwear',
          },
        ],
        categories: [{ id: 1, name: 'Outerwear', description: 'Jackets and shells' }],
        totalItems: 1,
        currentPage: 1,
      } as T;
    }

    if (path === '/api/payment/providers') {
      return [{ name: 'MeiMei', providerKey: 'meimei' }] as T;
    }

    return {} as T;
  }
}

describe('StorePage', () => {
  let fixture: ComponentFixture<StorePage>;
  let api: ApiServiceStub;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StorePage],
      providers: [
        { provide: ApiService, useClass: ApiServiceStub },
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({})) } },
        { provide: Router, useValue: { navigate: jasmine.createSpy('navigate') } },
      ],
    }).compileComponents();

    api = TestBed.inject(ApiService) as unknown as ApiServiceStub;
    fixture = TestBed.createComponent(StorePage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('renders catalog search controls and product cards', () => {
    const nativeElement: HTMLElement = fixture.nativeElement;

    expect(nativeElement.querySelector('h1')?.textContent).toContain('Sech-Team Store');
    expect(nativeElement.querySelector('input[name="searchKeyword"]')).not.toBeNull();
    expect(nativeElement.querySelector('button[type="submit"]')?.textContent).toContain('Search');
    expect(nativeElement.textContent).toContain('Trail Jacket');
  });

  it('opens checkout only when an authenticated cart has items', async () => {
    const component = fixture.componentInstance;
    component.auth.set({
      token: 'token',
      refreshToken: 'refresh-token',
      userId: 'customer-1',
      fullName: 'Test Customer',
      email: 'customer@example.test',
      role: 'Customer',
    });
    component.cart.set([{ productId: 'product-1', quantity: 1 }]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('aside[aria-label="Checkout"]')).toBeNull();

    component.openCheckout();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.checkoutOpen()).toBeTrue();
    expect(fixture.nativeElement.querySelector('aside[aria-label="Checkout"]')).not.toBeNull();
  });

  it('ignores stale catalog responses that complete after a newer request', async () => {
    const component = fixture.componentInstance;
    const slowCatalog = deferred<unknown>();
    const fastCatalog = deferred<unknown>();
    let catalogCalls = 0;

    api.requestHandler = async (_service, path) => {
      if (path.startsWith('/api/products/?')) {
        catalogCalls++;
        return catalogCalls === 1 ? slowCatalog.promise : fastCatalog.promise;
      }

      return {};
    };

    const firstLoad = component.loadCatalog(1);
    const secondLoad = component.loadCatalog(2);

    fastCatalog.resolve(catalogPayload('Fresh Jacket', 2));
    await secondLoad;

    slowCatalog.resolve(catalogPayload('Stale Jacket', 1));
    await firstLoad;

    expect(component.products()[0].name).toBe('Fresh Jacket');
    expect(component.catalogPage()).toBe(2);
  });
});

function catalogPayload(name: string, currentPage: number) {
  return {
    products: [
      {
        id: `product-${currentPage}`,
        name,
        price: 89.99,
        stockQuantity: 4,
        description: 'Water resistant shell',
        imageUrl: '',
        categoryId: 1,
        categoryName: 'Outerwear',
      },
    ],
    categories: [],
    totalItems: 2,
    currentPage,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}
