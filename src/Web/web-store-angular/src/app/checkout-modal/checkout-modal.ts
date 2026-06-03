import { NgFor, NgIf } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

export interface CheckoutFormModel {
  receiverName: string;
  phoneNumber: string;
  shippingAddress: string;
  paymentMethod: string;
}

export interface PaymentMethodOptionViewModel {
  value: string;
  label: string;
}

@Component({
  selector: 'app-checkout-modal',
  imports: [FormsModule, NgFor, NgIf],
  templateUrl: './checkout-modal.html',
  styleUrl: './checkout-modal.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CheckoutModalComponent {
  @Input() isOpen = false;
  @Input() busy = false;
  @Input({ required: true }) model!: CheckoutFormModel;
  @Input() paymentMethods: PaymentMethodOptionViewModel[] = [];

  @Output() close = new EventEmitter<void>();
  @Output() submitCheckout = new EventEmitter<void>();

  trackByValue(_: number, item: PaymentMethodOptionViewModel): string {
    return item.value;
  }
}
