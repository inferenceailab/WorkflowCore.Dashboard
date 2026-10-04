import { BreakpointObserver } from '@angular/cdk/layout';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatDivider } from '@angular/material/divider';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatTooltip } from '@angular/material/tooltip';
import { ActivatedRouteSnapshot, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { ApiService, dashboardUrl } from './core/api.service';
import { LiveService } from './core/live.service';
import { PublishEventDialog } from './dialogs/publish-event-dialog';
import { StartWorkflowDialog } from './dialogs/start-workflow-dialog';

type Theme = 'light' | 'dark';
const THEME_KEY = 'wfc-dashboard-theme';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatSidenavModule,
    MatIcon,
    MatIconButton,
    MatButton,
    MatTooltip,
    MatMenuModule,
    MatDivider,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly api = inject(ApiService);
  protected readonly live = inject(LiveService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  protected readonly config = this.api.config;
  protected readonly compact = toSignal(
    inject(BreakpointObserver).observe('(max-width: 900px)').pipe(map((r) => r.matches)),
    { initialValue: false },
  );
  // Only used on narrow screens, where the nav is an overlay that starts closed.
  protected readonly navOpen = signal(false);

  protected readonly nav = computed(() => [
    { path: '/', icon: 'space_dashboard', label: 'Overview', exact: true },
    { path: '/definitions', icon: 'account_tree', label: 'Definitions', exact: false },
    { path: '/instances', icon: 'format_list_bulleted', label: 'Instances', exact: false },
    ...(this.api.hasFeature('designer') && this.config().allowActions
      ? [{ path: '/designer', icon: 'design_services', label: 'Designer', exact: false }]
      : []),
  ]);

  /** Pages such as the designer use the whole content area, without the page padding and width limit. */
  protected readonly fullBleed = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => {
        let route: ActivatedRouteSnapshot | null = this.router.routerState.snapshot.root;
        while (route?.firstChild) route = route.firstChild;
        return route?.data['fullBleed'] === true;
      }),
    ),
    { initialValue: false },
  );

  protected readonly user = computed(() => this.config().user);

  protected readonly initials = computed(() => {
    const words = (this.user()?.name ?? '').split(/[\s@._-]+/).filter(Boolean);
    return ((words[0]?.[0] ?? '?') + (words.length > 1 ? words[words.length - 1][0] : '')).toUpperCase();
  });

  protected readonly roleLabel = computed(() => {
    switch (this.user()?.role) {
      case 'admin':
        return 'Admin';
      case 'viewer':
        return 'Viewer: read-only';
      default:
        return null;
    }
  });

  /** A full page load, not a router link: signing out goes through the server and the identity provider. */
  protected readonly signOutHref = computed(() => {
    const path = this.config().signOutPath;
    return path ? (path.startsWith('/') || path.includes('://') ? path : dashboardUrl(path)) : null;
  });

  protected readonly liveLabel = computed(() => {
    switch (this.live.state()) {
      case 'connected':
        return 'Live';
      case 'connecting':
        return 'Connecting';
      case 'reconnecting':
        return 'Reconnecting';
      default:
        return 'Offline';
    }
  });

  protected readonly theme = signal<Theme>(this.initialTheme());

  constructor() {
    this.applyTheme(this.theme());
  }

  protected toggleTheme(): void {
    const next: Theme = this.theme() === 'dark' ? 'light' : 'dark';
    this.theme.set(next);
    this.applyTheme(next);
    try {
      localStorage.setItem(THEME_KEY, next);
    } catch {
      // Storage may be unavailable; the toggle still works for this session.
    }
  }

  protected closeNavIfCompact(): void {
    if (this.compact()) this.navOpen.set(false);
  }

  protected openStart(): void {
    this.dialog
      .open(StartWorkflowDialog, { width: '640px', maxWidth: '95vw', data: {} })
      .afterClosed()
      .subscribe((id?: string) => {
        if (id) void this.router.navigate(['/instances', id]);
      });
  }

  protected openPublish(): void {
    this.dialog.open(PublishEventDialog, { width: '560px', maxWidth: '95vw' });
  }

  private initialTheme(): Theme {
    try {
      const stored = localStorage.getItem(THEME_KEY);
      if (stored === 'light' || stored === 'dark') return stored;
    } catch {
      // Fall through to the system preference.
    }
    return matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  private applyTheme(theme: Theme): void {
    const root = document.documentElement;
    root.classList.toggle('theme-dark', theme === 'dark');
    root.classList.toggle('theme-light', theme === 'light');
  }
}
