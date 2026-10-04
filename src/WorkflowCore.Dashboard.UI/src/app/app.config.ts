import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from '@angular/material/form-field';
import { MatIconRegistry } from '@angular/material/icon';
import { TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { ApiService, dashboardHeaderInterceptor } from './core/api.service';
import { LiveService } from './core/live.service';
import { DashboardTitleStrategy } from './core/title.strategy';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([dashboardHeaderInterceptor])),
    { provide: TitleStrategy, useClass: DashboardTitleStrategy },
    { provide: MAT_FORM_FIELD_DEFAULT_OPTIONS, useValue: { appearance: 'outline' } },
    provideAppInitializer(() => {
      // Icons come from the bundled "material-icons" outlined font, not Google Fonts.
      inject(MatIconRegistry).setDefaultFontSetClass('material-icons-outlined');
      inject(LiveService).start();
      return inject(ApiService).loadConfig();
    }),
  ],
};
