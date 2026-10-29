import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { errorMessages } from './form-errors';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <h1>Log in</h1>
    <form [formGroup]="form" (ngSubmit)="submit()" class="form">
      <label>
        Email
        <input type="email" formControlName="email" autocomplete="username" required />
      </label>
      <label>
        Password
        <input type="password" formControlName="password" autocomplete="current-password" required />
      </label>

      @for (message of errors(); track message) {
        <p class="error">{{ message }}</p>
      }

      <button type="submit" [disabled]="form.invalid || busy()">Log in</button>
    </form>
    <p>No account yet? <a routerLink="/register">Register</a></p>
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);

  protected submit(): void {
    const { email, password } = this.form.getRawValue();
    this.busy.set(true);
    this.errors.set([]);

    this.auth.login(email, password).subscribe({
      next: () => void this.router.navigate(['/account']),
      error: (error: unknown) => {
        this.errors.set(errorMessages(error, 'Login failed. Please try again.'));
        this.busy.set(false);
      },
    });
  }
}
