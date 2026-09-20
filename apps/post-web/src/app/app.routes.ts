import { Routes } from '@angular/router';
import { anonymousGuard, authGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [anonymousGuard],
    loadComponent: () => import('./features/auth/login.component').then((m) => m.LoginComponent),
    title: 'Log in · Post',
  },
  {
    path: 'signup',
    canActivate: [anonymousGuard],
    loadComponent: () => import('./features/auth/signup.component').then((m) => m.SignupComponent),
    title: 'Create your hiring account · Post',
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./features/shell/shell.component').then((m) => m.ShellComponent),
    children: [
      {
        path: '',
        loadComponent: () => import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
        title: 'Your postings · Post',
      },
      {
        path: 'postings/new',
        loadComponent: () => import('./features/postings/create-posting.component').then((m) => m.CreatePostingComponent),
        canDeactivate: [(component: { canLeave: () => Promise<boolean> | boolean }) => component.canLeave()],
        title: 'New job posting · Post',
      },
      {
        path: 'postings/:id/confirmation',
        loadComponent: () => import('./features/postings/confirmation.component').then((m) => m.ConfirmationComponent),
        title: 'Posting saved · Post',
      },
      {
        path: 'postings/:id',
        loadComponent: () => import('./features/postings/edit-posting.component').then((m) => m.EditPostingComponent),
        canDeactivate: [(component: { canLeave: () => Promise<boolean> | boolean }) => component.canLeave()],
        title: 'Edit posting · Post',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
