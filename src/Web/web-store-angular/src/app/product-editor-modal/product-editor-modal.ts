import { NgFor, NgIf } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

export interface ProductEditorCategoryViewModel {
  id: number;
  name: string;
}

export interface ProductEditorModel {
  id: string;
  name: string;
  price: number;
  stockQuantity: number;
  categoryId: number;
  imageUrl: string;
  description: string;
  isActive: boolean;
}

@Component({
  selector: 'app-product-editor-modal',
  imports: [FormsModule, NgFor, NgIf],
  templateUrl: './product-editor-modal.html',
  styleUrl: './product-editor-modal.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductEditorModalComponent {
  @Input() isOpen = false;
  @Input() busy = false;
  @Input({ required: true }) model!: ProductEditorModel;
  @Input() categories: ProductEditorCategoryViewModel[] = [];

  @Output() close = new EventEmitter<void>();
  @Output() save = new EventEmitter<void>();

  trackByCategory(_: number, category: ProductEditorCategoryViewModel): number {
    return category.id;
  }
}
