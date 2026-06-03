import { provideHttpClient } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { StorePage } from './store/store-page';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(),
    provideRouter(
      [
        { path: '', component: StorePage },
        { path: 'products/:productId', component: StorePage },
        { path: '**', redirectTo: '' },
      ],
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled' }),
    ),
  ]
};
