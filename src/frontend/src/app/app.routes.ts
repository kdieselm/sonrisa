import { Routes } from '@angular/router';
import { AdminAuditPage } from './admin/admin-audit.page';
import { AdminEventsPage } from './admin/admin-events.page';
import { AdminSimulatePage } from './admin/admin-simulate.page';
import { AdminUserPage } from './admin/admin-user.page';
import { AdminUsersPage } from './admin/admin-users.page';
import { AlertEditPage } from './alerts/alert-edit.page';
import { AlertsPage } from './alerts/alerts.page';
import { adminGuard, authGuard, guestGuard } from './auth/auth.guards';
import { DestinationsPage } from './destinations/destinations.page';
import { NotificationsPage } from './notifications/notifications.page';
import { AccountPage } from './pages/account.page';
import { LandingPage } from './pages/landing.page';
import { LoginPage } from './pages/login.page';
import { RegisterPage } from './pages/register.page';

export const routes: Routes = [
  { path: '', component: LandingPage, title: 'Sonrisa' },
  { path: 'register', component: RegisterPage, canActivate: [guestGuard], title: 'Register · Sonrisa' },
  { path: 'login', component: LoginPage, canActivate: [guestGuard], title: 'Log in · Sonrisa' },
  { path: 'account', component: AccountPage, canActivate: [authGuard], title: 'Account · Sonrisa' },
  { path: 'alerts', component: AlertsPage, canActivate: [authGuard], title: 'Alerts · Sonrisa' },
  { path: 'alerts/new', component: AlertEditPage, canActivate: [authGuard], title: 'New alert · Sonrisa' },
  { path: 'alerts/:id', component: AlertEditPage, canActivate: [authGuard], title: 'Alert · Sonrisa' },
  { path: 'destinations', component: DestinationsPage, canActivate: [authGuard], title: 'Destinations · Sonrisa' },
  {
    path: 'notifications',
    component: NotificationsPage,
    canActivate: [authGuard],
    title: 'Notifications · Sonrisa',
  },
  { path: 'admin', redirectTo: 'admin/users', pathMatch: 'full' },
  {
    path: 'admin/users',
    component: AdminUsersPage,
    canActivate: [authGuard, adminGuard],
    title: 'Users · Admin · Sonrisa',
  },
  {
    path: 'admin/users/:id',
    component: AdminUserPage,
    canActivate: [authGuard, adminGuard],
    title: 'User · Admin · Sonrisa',
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
