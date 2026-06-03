import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '../api.service';

export interface NotificationSummary {
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

@Injectable({ providedIn: 'root' })
export class NotificationsFacade {
  private readonly api = inject(ApiService);
  private loadVersion = 0;

  readonly notifications = signal<NotificationSummary[]>([]);
  readonly unreadCount = signal(0);
  readonly loading = signal(false);
  readonly actionBusy = signal<Record<string, boolean>>({});
  readonly unreadOnly = signal(false);

  clear(): void {
    this.loadVersion++;
    this.notifications.set([]);
    this.unreadCount.set(0);
    this.loading.set(false);
    this.actionBusy.set({});
  }

  async load(showErrors = true): Promise<string | null> {
    const loadVersion = ++this.loadVersion;

    if (!this.api.auth) {
      this.clear();
      return null;
    }

    this.loading.set(true);

    try {
      const params = new URLSearchParams({
        pageIndex: '0',
        pageSize: '20',
        unreadOnly: String(this.unreadOnly()),
      });
      const payload = await this.api.request<any>('notification', `/api/notifications/?${params.toString()}`, { auth: true });
      if (loadVersion !== this.loadVersion) return null;

      const items = payload.items ?? payload.Items ?? [];
      const notifications = items.map((item: any) => this.normalizeNotification(item));

      this.notifications.set(notifications);
      this.unreadCount.set(Number(payload.unreadCount ?? payload.UnreadCount ?? this.unreadCount()));
      return null;
    } catch (error) {
      if (loadVersion !== this.loadVersion) return null;

      this.notifications.set([]);
      return showErrors ? this.api.messageFromError(error) : null;
    } finally {
      if (loadVersion === this.loadVersion) {
        this.loading.set(false);
      }
    }
  }

  async loadUnreadCount(): Promise<void> {
    if (!this.api.auth) {
      this.unreadCount.set(0);
      return;
    }

    try {
      const payload = await this.api.request<any>('notification', '/api/notifications/unread-count', { auth: true });
      this.unreadCount.set(Number(payload.unreadCount ?? payload.UnreadCount ?? 0));
    } catch {
      this.unreadCount.set(0);
    }
  }

  async markRead(notification: NotificationSummary): Promise<string | null> {
    if (notification.isRead || this.actionBusy()[notification.id]) return null;

    this.actionBusy.update((current) => ({ ...current, [notification.id]: true }));

    try {
      const response = await this.api.request<any>('notification', `/api/notifications/${notification.id}/read`, {
        method: 'PUT',
        auth: true,
      });
      const updatedNotification = this.normalizeNotification(response);
      this.notifications.update((current) =>
        current.map((item) => (item.id === updatedNotification.id ? updatedNotification : item)),
      );
      await this.loadUnreadCount();
      return null;
    } catch (error) {
      return this.api.messageFromError(error);
    } finally {
      this.actionBusy.update((current) => ({ ...current, [notification.id]: false }));
    }
  }

  async markAllRead(): Promise<string | null> {
    if (this.actionBusy()['read-all']) return null;

    this.actionBusy.update((current) => ({ ...current, 'read-all': true }));

    try {
      await this.api.request<any>('notification', '/api/notifications/read-all', {
        method: 'PUT',
        auth: true,
      });
      this.notifications.update((current) =>
        current.map((item) => ({ ...item, isRead: true, readAt: item.readAt ?? new Date().toISOString() })),
      );
      this.unreadCount.set(0);
      return null;
    } catch (error) {
      return this.api.messageFromError(error);
    } finally {
      this.actionBusy.update((current) => ({ ...current, 'read-all': false }));
    }
  }

  setUnreadOnly(value: boolean): void {
    this.unreadOnly.set(value);
  }

  isBusy(notification: NotificationSummary): boolean {
    return Boolean(this.actionBusy()[notification.id]);
  }

  isMarkingAllRead(): boolean {
    return Boolean(this.actionBusy()['read-all']);
  }

  private normalizeNotification(notification: any): NotificationSummary {
    return {
      id: String(notification.id ?? notification.Id),
      customerId: String(notification.customerId ?? notification.CustomerId ?? ''),
      type: notification.type ?? notification.Type ?? 'General',
      title: notification.title ?? notification.Title ?? 'Notification',
      message: notification.message ?? notification.Message ?? '',
      dataJson: notification.dataJson ?? notification.DataJson,
      isRead: Boolean(notification.isRead ?? notification.IsRead),
      createdAt: notification.createdAt ?? notification.CreatedAt,
      readAt: notification.readAt ?? notification.ReadAt,
    };
  }
}
