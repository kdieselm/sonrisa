import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';

/** Logged-in area. Alerts, destinations and notifications are added here in later slices. */
@Component({
  selector: 'app-account-page',
  template: `
    <h1>Your account</h1>
    <p>Logged in as <strong>{{ auth.email() }}</strong>.</p>
    <p>Alerts and notifications will appear here.</p>
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
