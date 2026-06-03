import { CommonModule, CurrencyPipe, NgFor, NgIf } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

export interface CatalogProductViewModel {
  id: string;
  name: string;
  price: number;
  stockQuantity: number;
  description: string;
  imageUrl: string;
  categoryId: number;
  categoryName: string;
}

export interface CatalogChipViewModel {
  value: string;
  label: string;
}

@Component({
  selector: 'app-catalog-view',
  imports: [CommonModule, CurrencyPipe, FormsModule, NgFor, NgIf],
  templateUrl: './catalog-view.html',
  styleUrl: './catalog-view.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CatalogViewComponent {
  @Input() categoryChips: CatalogChipViewModel[] = [];
  @Input() stockChips: CatalogChipViewModel[] = [];
  @Input() sortOptions: CatalogChipViewModel[] = [];
  @Input() selectedCategory = 'All';
  @Input() selectedStock = 'All';
  @Input() selectedSort = 'featured';
  @Input() selectedSortLabel = 'Response order';
  @Input() sortMenuOpen = false;
  @Input() productTotal = 0;
  @Input() productRangeStart = 0;
  @Input() productRangeEnd = 0;
  @Input() productPage = 1;
  @Input() productTotalPages = 1;
  @Input() paginationItems: Array<number | string> = [];
  @Input() searchActive = false;
  @Input() searchAppliedKeyword = '';
  @Input() searchKeyword = '';
  @Input() searchLoading = false;
  @Input() connectionState: 'loading' | 'live' | 'offline' = 'loading';
  @Input() products: CatalogProductViewModel[] = [];
  @Input() isAuthenticated = false;
  @Input() isAdmin = false;
  @Input() cartQuantity: (productId: string) => number = () => 0;
  @Input() addButtonText: (product: CatalogProductViewModel) => string = () => 'Add';
  @Input() isDeletingProduct: (product: CatalogProductViewModel) => boolean = () => false;

  @Output() categoryChange = new EventEmitter<string>();
  @Output() stockChange = new EventEmitter<string>();
  @Output() searchKeywordChange = new EventEmitter<string>();
  @Output() searchSubmit = new EventEmitter<void>();
  @Output() searchClear = new EventEmitter<void>();
  @Output() sortMenuToggle = new EventEmitter<void>();
  @Output() sortMenuClose = new EventEmitter<void>();
  @Output() sortChange = new EventEmitter<string>();
  @Output() productOpen = new EventEmitter<CatalogProductViewModel>();
  @Output() productAdd = new EventEmitter<CatalogProductViewModel>();
  @Output() productEdit = new EventEmitter<CatalogProductViewModel>();
  @Output() productDelete = new EventEmitter<CatalogProductViewModel>();
  @Output() pageChange = new EventEmitter<number>();

  onProductCardKeydown(event: KeyboardEvent, product: CatalogProductViewModel): void {
    if (event.key !== 'Enter' && event.key !== ' ') return;

    event.preventDefault();
    this.productOpen.emit(product);
  }

  isPaginationPage(item: number | string): item is number {
    return typeof item === 'number';
  }

  trackByValue(_: number, chip: CatalogChipViewModel): string {
    return chip.value;
  }

  trackByProduct(_: number, product: CatalogProductViewModel): string {
    return product.id;
  }

  trackByPaginationItem(_: number, item: number | string): string {
    return String(item);
  }
}
