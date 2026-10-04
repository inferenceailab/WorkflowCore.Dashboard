import { Routes } from '@angular/router';
import type { DesignerPage } from './pages/designer/designer-page';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home/home').then((m) => m.HomePage), title: 'Overview' },
  {
    path: 'definitions',
    loadComponent: () => import('./pages/definitions/definitions').then((m) => m.DefinitionsPage),
    title: 'Definitions',
  },
  {
    path: 'definitions/:id/:version',
    loadComponent: () => import('./pages/definitions/definition-detail').then((m) => m.DefinitionDetailPage),
    title: 'Definition',
  },
  {
    path: 'instances',
    loadComponent: () => import('./pages/instances/instances').then((m) => m.InstancesPage),
    title: 'Instances',
  },
  {
    path: 'instances/:id',
    loadComponent: () => import('./pages/instances/instance-detail').then((m) => m.InstanceDetailPage),
    title: 'Instance',
  },
  {
    path: 'designer',
    loadComponent: () => import('./pages/designer/designer-list').then((m) => m.DesignerListPage),
    title: 'Designer',
  },
  {
    path: 'designer/:id',
    loadComponent: () => import('./pages/designer/designer-page').then((m) => m.DesignerPage),
    canDeactivate: [(page: DesignerPage) => page.canLeave()],
    data: { fullBleed: true },
    title: 'Designer',
  },
  { path: '**', redirectTo: '' },
];
