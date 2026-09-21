import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./features/shell/shell.component').then((m) => m.ShellComponent),
    children: [
      {
        path: '',
        loadComponent: () => import('./features/results/results.component').then((m) => m.ResultsComponent),
        title: 'Jobs · TalentBridge',
      },
      {
        path: 'jobs/:slug',
        loadComponent: () => import('./features/detail/detail.component').then((m) => m.DetailComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
