import { Routes } from '@angular/router';
import { AdminAuditPage } from './admin/admin-audit.page';
import { AdminEventsPage } from './admin/admin-events.page';
import { AdminSimulatePage } from './admin/admin-simulate.page';
import { AdminUsersPage } from './admin/admin-users.page';
import { adminGuard, authGuard, guestGuard } from './auth/auth.guards';
import { AccountPage } from './pages/account.page';
import { LandingPage } from './pages/landing.page';
import { LoginPage } from './pages/login.page';
import { RegisterPage } from './pages/register.page';

export const routes: Routes = [
  { path: '', component: LandingPage, title: 'Sonrisa' },
  { path: 'register', component: RegisterPage, canActivate: [guestGuard], title: 'Register · Sonrisa' },
  { path: 'login', component: LoginPage, canActivate: [guestGuard], title: 'Log in · Sonrisa' },
  { path: 'account', component: AccountPage, canActivate: [authGuard], title: 'Account · Sonrisa' },
  { path: 'admin', redirectTo: 'admin/users', pathMatch: 'full' },
  {
    path: 'admin/users',
    component: AdminUsersPage,
    canActivate: [authGuard, adminGuard],
    title: 'Users · Admin · Sonrisa',
  },
  {
    path: 'admin/events',
    component: AdminEventsPage,
    canActivate: [authGuard, adminGuard],
    title: 'Events · Admin · Sonrisa',
  },
  {
    path: 'admin/simulate',
    component: AdminSimulatePage,
    canActivate: [authGuard, adminGuard],
    title: 'Simulate event · Admin · Sonrisa',
  },
  {
    path: 'admin/audit',
    component: AdminAuditPage,
    canActivate: [authGuard, adminGuard],
    title: 'Audit log · Admin · Sonrisa',
  },
  { path: '**', redirectTo: '' },
];
