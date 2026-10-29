import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';

@Component({
  selector: 'app-landing-page',
  imports: [RouterLink],
  template: `
    <h1>World event alerts</h1>
    <p>
      Get notified by email or Slack when something important happens in the world: breaking news,
      market movements and natural disasters.
    </p>

    @if (auth.isLoggedIn()) {
      <p>You are logged in as <strong>{{ auth.email() }}</strong>.</p>
      <p><a routerLink="/account">Your account</a></p>
    } @else {
      <p class="actions">
        <a routerLink="/register" class="button">Create an account</a>
        <a routerLink="/login">I already have an account</a>
      </p>
    }
  `,
})
export class LandingPage {
  protected readonly auth = inject(AuthService);
}
