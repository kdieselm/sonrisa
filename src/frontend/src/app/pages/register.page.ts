import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { switchMap } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { errorMessages } from './form-errors';

@Component({
  selector: 'app-register-page',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <h1>Create an account</h1>
    <form [formGroup]="form" (ngSubmit)="submit()" class="form">
      <label>
        Email
        <input type="email" formControlName="email" autocomplete="username" required />
      </label>
      <label>
        Password
        <input type="password" formControlName="password" autocomplete="new-password" required />
        <small>At least 6 characters, with an uppercase letter, a lowercase letter, a digit and a symbol.</small>
      </label>

      @for (message of errors(); track message) {
        <p class="error">{{ message }}</p>
      }

      <button type="submit" [disabled]="form.invalid || busy()">Register</button>
    </form>
    <p>Already registered? <a routerLink="/login">Log in</a></p>
  `,
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);

  /** Registers, then logs in with the same credentials. */
  protected submit(): void {
    const { email, password } = this.form.getRawValue();
    this.busy.set(true);
    this.errors.set([]);

    this.auth
      .register(email, password)
      .pipe(switchMap(() => this.auth.login(email, password)))
      .subscribe({
        next: () => void this.router.navigate(['/account']),
        error: (error: unknown) => {
          this.errors.set(errorMessages(error, 'Registration failed. Please try again.'));
          this.busy.set(false);
        },
      });
  }
}
