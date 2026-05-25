import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface ApiConfig {
  identity: string;
  product: string;
  cart: string;
  order: string;
  payment: string;
  notification: string;
}

export interface AuthState {
  token: string;
  refreshToken: string;
  userId: string;
  fullName: string;
  email: string;
  role: string;
}

type ServiceName = keyof ApiConfig;

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  auth?: boolean;
  retry?: boolean;
}

const defaultApi: ApiConfig = {
  identity: 'http://localhost:5000',
  product: 'http://localhost:5000',
  cart: 'http://localhost:5000',
  order: 'http://localhost:5000',
  payment: 'http://localhost:5000',
  notification: 'http://localhost:5000',
};

const storageKeys = {
  auth: 'northern-mountains.angular.auth',
  api: 'northern-mountains.angular.api',
};

@Injectable({ providedIn: 'root' })
export class ApiService {
  private apiConfig = this.readApiConfig();
  private authState = this.readAuth();

  constructor(private readonly http: HttpClient) {}

  get config(): ApiConfig {
    return { ...this.apiConfig };
  }

  get auth(): AuthState | null {
    return this.authState ? { ...this.authState } : null;
  }




  saveAuth(auth: AuthState | null): AuthState | null {
    this.authState = auth;

    if (auth) {
      localStorage.setItem(storageKeys.auth, JSON.stringify(auth));
    } else {
      localStorage.removeItem(storageKeys.auth);
    }

    return this.auth;
  }

  async request<T>(service: ServiceName, path: string, options: RequestOptions = {}): Promise<T> {
    const method = options.method ?? 'GET';
    const responseType = 'json' as const;
    let headers = new HttpHeaders({ Accept: 'application/json' });

    if (options.body !== undefined) {
      headers = headers.set('Content-Type', 'application/json');
    }

    if (options.auth && this.authState?.token) {
      headers = headers.set('Authorization', `Bearer ${this.authState.token}`);
    }

    if (method !== 'GET') {
      headers = headers.set('x-requestid', this.requestId());
    }

    try {
      return await firstValueFrom(
        this.http.request<T>(method, `${this.apiConfig[service]}${path}`, {
          body: options.body,
          headers,
          responseType,
        }),
      );
    } catch (error) {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        options.auth &&
        options.retry !== false &&
        this.authState?.refreshToken
      ) {
        await this.refreshAuth();
        return this.request<T>(service, path, { ...options, retry: false });
      }

      throw error;
    }
  }

  messageFromError(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return error instanceof Error ? error.message : 'Request failed.';
    }

    const payload = error.error;
    if (!payload) return error.statusText || 'Request failed.';
    if (typeof payload === 'string') return payload;
    if (payload.message) return payload.message;
    if (payload.title) return payload.title;
    if (Array.isArray(payload.errors)) return payload.errors.join(' ');

    if (payload.errors && typeof payload.errors === 'object') {
      return Object.values(payload.errors).flat().join(' ');
    }

    return error.statusText || 'Request failed.';
  }

  private async refreshAuth(): Promise<void> {
    if (!this.authState?.refreshToken) {
      this.saveAuth(null);
      return;
    }

    try {
      const response = await firstValueFrom(
        this.http.post<AuthState>(
          `${this.apiConfig.identity}/api/auth/refresh`,
          { refreshToken: this.authState.refreshToken },
          {
            headers: new HttpHeaders({
              Accept: 'application/json',
              'Content-Type': 'application/json',
              'x-requestid': this.requestId(),
            }),
          },
        ),
      );

      this.saveAuth(this.normalizeAuth(response));
    } catch {
      this.saveAuth(null);
      throw new Error('Session expired. Please login again.');
    }
  }

  normalizeAuth(response: any): AuthState {
    const token = response.token ?? response.Token;

    return {
      token,
      refreshToken: response.refreshToken ?? response.RefreshToken,
      userId: response.userId ?? response.UserId ?? this.userIdFromToken(token) ?? '',
      fullName: response.fullName ?? response.FullName ?? '',
      email: response.email ?? response.Email ?? '',
      role: response.role ?? response.Role ?? 'Customer',
    };
  }

  private readApiConfig(): ApiConfig {
    return { ...defaultApi };
  }

  private readAuth(): AuthState | null {
    try {
      const auth = JSON.parse(localStorage.getItem(storageKeys.auth) || 'null');
      if (!auth) return null;

      return {
        ...auth,
        userId: auth.userId ?? auth.UserId ?? this.userIdFromToken(auth.token ?? auth.Token) ?? '',
      };
    } catch {
      return null;
    }
  }

  private trimUrl(value: string): string {
    return value.trim().replace(/\/+$/, '');
  }

  private requestId(): string {
    if (crypto.randomUUID) return crypto.randomUUID();

    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (char) => {
      const value = (Math.random() * 16) | 0;
      const result = char === 'x' ? value : (value & 0x3) | 0x8;
      return result.toString(16);
    });
  }

  private userIdFromToken(token?: string): string | null {
    if (!token) return null;

    try {
      const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
      return (
        payload.nameid ??
        payload.sub ??
        payload['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'] ??
        null
      );
    } catch {
      return null;
    }
  }
}
