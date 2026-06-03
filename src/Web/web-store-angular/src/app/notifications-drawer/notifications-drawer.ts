import { NgClass, NgFor, NgIf } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

export interface NotificationViewModel {
  id: string;
  customerId: string;
  type: string;
  title: string;
  message: string;
  dataJson?: string;
  isRead: boolean;
  createdAt?: string;
  readAt?: string;
}

@Component({
  selector: 'app-notifications-drawer',
  imports: [NgClass, NgFor, NgIf],
  templateUrl: './notifications-drawer.html',
  styleUrl: './notifications-drawer.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationsDrawerComponent {
  @Input() isOpen = false;
  @Input() unreadCount = 0;
  @Input() unreadOnly = false;
  @Input() loading = false;
  @Input() markingAllRead = false;
  @Input() notifications: NotificationViewModel[] = [];
  @Input() isNotificationBusy: (notification: NotificationViewModel) => boolean = () => false;
  @Input() formatDate: (value?: string) => string = (value) => (value ? new Date(value).toLocaleString() : '');

  @Output() close = new EventEmitter<void>();
  @Output() unreadOnlyChange = new EventEmitter<boolean>();
  @Output() markAllRead = new EventEmitter<void>();
  @Output() markRead = new EventEmitter<NotificationViewModel>();

  trackByNotification(_: number, notification: NotificationViewModel): string {
    return notification.id;
  }
}
