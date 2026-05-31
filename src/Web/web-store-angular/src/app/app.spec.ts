import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { App } from './app';
import { ApiConfig, ApiService, AuthState } from './api.service';

class ApiServiceStub {
  private authState: AuthState | null = null;

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

  async request<T>(_service: keyof ApiConfig, path: string): Promise<T> {
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

describe('App', () => {
  let fixture: ComponentFixture<App>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [{ provide: ApiService, useClass: ApiServiceStub }],
    }).compileComponents();

    fixture = TestBed.createComponent(App);
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

    fixture.debugElement.query(By.css('button.checkout-button')).nativeElement.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.checkoutOpen()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Checkout');
  });
});
