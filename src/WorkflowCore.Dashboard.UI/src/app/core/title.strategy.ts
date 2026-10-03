import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { ApiService } from './api.service';

/** "Instances · Workflow Core" */
@Injectable({ providedIn: 'root' })
export class DashboardTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly api = inject(ApiService);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const page = this.buildTitle(snapshot);
    const app = this.api.config().title;
    this.title.setTitle(page ? `${page} · ${app}` : app);
  }
}
