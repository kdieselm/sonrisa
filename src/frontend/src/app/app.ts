import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './auth/auth.service';

/** App shell: top navigation and the routed page. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topbar">
      <a routerLink="/" class="brand">Sonrisa</a>
      <nav>
        @if (auth.isLoggedIn()) {
          <a routerLink="/account" routerLinkActive="active">{{ auth.email() }}</a>
          <button type="button" class="link" (click)="logout()">Log out</button>
        } @else {
          <a routerLink="/login" routerLinkActive="active">Log in</a>
          <a routerLink="/register" routerLinkActive="active">Register</a>
        }
      </nav>
    </header>
    <main class="content">
      <router-outlet />
    </main>
  `,
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected logout(): void {
    this.auth.logout().subscribe(() => void this.router.navigate(['/']));
  }
}
