import { NgClass, NgFor, NgIf } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthState } from '../api.service';

export interface AccountOrderViewModel {
  id: string;
  customerId?: string;
  orderDate?: string;
  totalAmount: number;
  paymentMethod: string;
  status: string;
  paymentStatus: string;
  canCancel: boolean;
  canReturn: boolean;
}

export interface AccountPaymentViewModel {
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

export interface AccountCategoryViewModel {
  id: number;
  name: string;
  description: string;
}

export interface LoginModel {
  emailOrPhone: string;
  password: string;
}

export interface RegisterModel {
  fullName: string;
  email: string;
  phoneNumber: string;
  password: string;
}

export interface PaymentFiltersModel {
  status: string;
  orderId: string;
  customerId: string;
}

export interface CategoryCreateModel {
  name: string;
  description: string;
}

export interface ProductCreateModel {
  name: string;
  price: number;
  stockQuantity: number;
  categoryId: number;
  imageUrl: string;
  description: string;
}

@Component({
  selector: 'app-account-drawer',
  imports: [FormsModule, NgClass, NgFor, NgIf],
  templateUrl: './account-drawer.html',
  styleUrl: './account-drawer.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountDrawerComponent {
  @Input() isOpen = false;
  @Input() auth: AuthState | null = null;
  @Input() authTab: 'login' | 'register' = 'login';
  @Input() isAdmin = false;
  @Input() orders: AccountOrderViewModel[] = [];
  @Input() adminPayments: AccountPaymentViewModel[] = [];
  @Input() adminPaymentsLoading = false;
  @Input() categories: AccountCategoryViewModel[] = [];
  @Input() loginModel: LoginModel = { emailOrPhone: '', password: '' };
  @Input() registerModel: RegisterModel = { fullName: '', email: '', phoneNumber: '', password: '' };
  @Input() paymentFilters: PaymentFiltersModel = { status: 'All', orderId: '', customerId: '' };
  @Input() categoryModel: CategoryCreateModel = { name: '', description: '' };
  @Input() productModel: ProductCreateModel = {
    name: '',
    price: 0,
    stockQuantity: 0,
    categoryId: 0,
    imageUrl: '',
    description: '',
  };

  @Input() shortId: (value?: string) => string = (value) => value ?? '';
  @Input() formatPrice: (value: number) => string = (value) => String(value);
  @Input() formatDate: (value?: string) => string = (value) => value ?? '';
  @Input() paymentFor: (order: AccountOrderViewModel) => AccountPaymentViewModel | undefined = () => undefined;
  @Input() canConfirmPayment: (order: AccountOrderViewModel) => boolean = () => false;
  @Input() isConfirmingPayment: (order: AccountOrderViewModel) => boolean = () => false;
  @Input() payButtonText: (order: AccountOrderViewModel) => string = () => 'Pay now';
  @Input() isOrderActionBusy: (order: AccountOrderViewModel, action: string) => boolean = () => false;
  @Input() isOrderDetailBusy: (order: AccountOrderViewModel) => boolean = () => false;
  @Input() isMockingPayment: (payment: AccountPaymentViewModel, success: boolean) => boolean = () => false;
  @Input() canMockPayment: (payment: AccountPaymentViewModel) => boolean = () => false;
  @Input() paymentStatusClass: (status: string) => string = () => '';

  @Output() close = new EventEmitter<void>();
  @Output() authTabChange = new EventEmitter<'login' | 'register'>();
  @Output() login = new EventEmitter<void>();
  @Output() register = new EventEmitter<void>();
  @Output() reloadOrders = new EventEmitter<void>();
  @Output() logout = new EventEmitter<void>();
  @Output() openOrderDetail = new EventEmitter<AccountOrderViewModel>();
  @Output() confirmPayment = new EventEmitter<AccountOrderViewModel>();
  @Output() orderAction = new EventEmitter<{ order: AccountOrderViewModel; action: string }>();
  @Output() reloadAdminPayments = new EventEmitter<void>();
  @Output() mockPayment = new EventEmitter<{ payment: AccountPaymentViewModel; success: boolean }>();
  @Output() createCategory = new EventEmitter<void>();
  @Output() createProduct = new EventEmitter<void>();

  trackByOrder(_: number, order: AccountOrderViewModel): string {
    return order.id;
  }

  trackByPayment(_: number, payment: AccountPaymentViewModel): string {
    return payment.id;
  }

  trackByCategory(_: number, category: AccountCategoryViewModel): number {
    return category.id;
  }
}
