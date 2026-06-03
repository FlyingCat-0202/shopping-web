import { CurrencyPipe, NgFor, NgIf } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

export interface ProductDetailViewModel {
  id: string;
  name: string;
  price: number;
  stockQuantity: number;
  description: string;
  imageUrl: string;
  categoryId: number;
  categoryName: string;
}

@Component({
  selector: 'app-product-detail',
  imports: [CurrencyPipe, NgFor, NgIf],
  templateUrl: './product-detail.html',
  styleUrl: './product-detail.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductDetailComponent {
  @Input({ required: true }) product!: ProductDetailViewModel;
  @Input() relatedProducts: ProductDetailViewModel[] = [];
  @Input() cartQuantity: (productId: string) => number = () => 0;
  @Input() addButtonText: (product: ProductDetailViewModel) => string = () => 'Add';
  @Input() isAdmin = false;

  @Output() back = new EventEmitter<void>();
  @Output() add = new EventEmitter<ProductDetailViewModel>();
  @Output() openCart = new EventEmitter<void>();
  @Output() edit = new EventEmitter<ProductDetailViewModel>();
  @Output() openProduct = new EventEmitter<ProductDetailViewModel>();

  trackByProduct(_: number, product: ProductDetailViewModel): string {
    return product.id;
  }
}
