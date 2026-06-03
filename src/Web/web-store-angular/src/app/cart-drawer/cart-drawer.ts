import { CurrencyPipe, NgFor, NgIf } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

export interface CartDrawerProductViewModel {
  id: string;
  name: string;
  price: number;
  stockQuantity: number;
  imageUrl: string;
}

export interface CartDrawerLineViewModel {
  productId: string;
  quantity: number;
  product: CartDrawerProductViewModel;
}

export interface CartQuantityChange {
  productId: string;
  quantity: number;
}

@Component({
  selector: 'app-cart-drawer',
  imports: [CurrencyPipe, NgFor, NgIf],
  templateUrl: './cart-drawer.html',
  styleUrl: './cart-drawer.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CartDrawerComponent {
  @Input() isOpen = false;
  @Input() isAuthenticated = false;
  @Input() cartLines: CartDrawerLineViewModel[] = [];
  @Input() cartCount = 0;
  @Input() cartTotal = 0;

  @Output() close = new EventEmitter<void>();
  @Output() checkout = new EventEmitter<void>();
  @Output() quantityChange = new EventEmitter<CartQuantityChange>();
  @Output() remove = new EventEmitter<string>();

  trackByCartLine(_: number, line: CartDrawerLineViewModel): string {
    return line.productId;
  }
}
