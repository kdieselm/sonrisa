import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './auth/auth.guards';
import { AccountPage } from './pages/account.page';
import { LandingPage } from './pages/landing.page';
import { LoginPage } from './pages/login.page';
import { RegisterPage } from './pages/register.page';

export const routes: Routes = [
  { path: '', component: LandingPage, title: 'Sonrisa' },
  { path: 'register', component: RegisterPage, canActivate: [guestGuard], title: 'Register · Sonrisa' },
  { path: 'login', component: LoginPage, canActivate: [guestGuard], title: 'Log in · Sonrisa' },
  { path: 'account', component: AccountPage, canActivate: [authGuard], title: 'Account · Sonrisa' },
  { path: '**', redirectTo: '' },
];
