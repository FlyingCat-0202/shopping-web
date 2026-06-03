import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

@Component({
  selector: 'app-store-header',
  templateUrl: './store-header.html',
  styleUrl: './store-header.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StoreHeaderComponent {
  @Input() unreadNotificationCount = 0;
  @Input() cartCount = 0;

  @Output() openNotifications = new EventEmitter<void>();
  @Output() openAccount = new EventEmitter<void>();
  @Output() openCart = new EventEmitter<void>();
}
