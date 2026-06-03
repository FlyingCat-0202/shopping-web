import { CurrencyPipe, NgFor, NgIf } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

export interface OrderDetailItemViewModel {
  productId: string;
  productName: string;
  productImageUrl?: string;
  quantity: number;
  unitPrice: number;
}

export interface OrderTimelineItemViewModel {
  id: string;
  title: string;
  description: string;
  source: string;
  occurredAt?: string;
}

export interface OrderDetailViewModel {
  id: string;
  customerId?: string;
  orderDate?: string;
  totalAmount: number;
  paymentMethod: string;
  status: string;
  paymentStatus: string;
  receiverName: string;
  phoneNumber: string;
  shippingAddress: string;
  items: OrderDetailItemViewModel[];
  timeline: OrderTimelineItemViewModel[];
}

@Component({
  selector: 'app-order-detail-modal',
  imports: [CurrencyPipe, NgFor, NgIf],
  templateUrl: './order-detail-modal.html',
  styleUrl: './order-detail-modal.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderDetailModalComponent {
  @Input() order: OrderDetailViewModel | null = null;
  @Input() fallbackImage = '';
  @Input() shortId: (value?: string) => string = (value) => (value && value.length > 8 ? value.slice(0, 8) : (value ?? ''));
  @Input() formatDate: (value?: string) => string = (value) => (value ? new Date(value).toLocaleString() : '');

  @Output() close = new EventEmitter<void>();

  trackByOrderDetailItem(_: number, item: OrderDetailItemViewModel): string {
    return item.productId;
  }

  trackByTimelineItem(_: number, item: OrderTimelineItemViewModel): string {
    return item.id;
  }
}
