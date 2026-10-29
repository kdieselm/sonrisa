import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';

/** Logged-in area: who is logged in, and shortcuts to the pages of the account. */
@Component({
  selector: 'app-account-page',
  imports: [RouterLink],
  template: `
    <h1>Your account</h1>
    <p>Logged in as <strong>{{ auth.email() }}</strong>.</p>
    <p>
      <a routerLink="/alerts">Alerts</a> ·
      <a routerLink="/destinations">Destinations</a> ·
      <a routerLink="/notifications">Notifications</a>
    </p>
    <button type="button" (click)="logout()">Log out</button>
  `,
})
export class AccountPage {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected logout(): void {
    this.auth.logout().subscribe(() => void this.router.navigate(['/']));
  }
}
