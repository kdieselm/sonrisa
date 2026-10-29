import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminNav } from './admin-nav';
import { AdminService, AdminUser, PagedResult } from './admin.service';
import { Pager } from '../shared/pager';

const PAGE_SIZE = 50;

/** Admin view: users and their roles, paged by email (spec §2.9); each links to the user's alerts and destinations. */
@Component({
  selector: 'app-admin-users-page',
  imports: [AdminNav, Pager, RouterLink],
  template: `
    <h1>Admin</h1>
    <app-admin-nav />
    @if (error()) {
      <p class="error">Could not load the users.</p>
    } @else if (result(); as page) {
      <table class="table">
        <thead>
          <tr><th>Email</th><th>Roles</th><th>ID</th></tr>
        </thead>
        <tbody>
          @for (user of page.items; track user.id) {
            <tr>
              <td><a [routerLink]="['/admin/users', user.id]">{{ user.email }}</a></td>
              <td>{{ user.roles.length ? user.roles.join(', ') : '—' }}</td>
              <td class="mono">{{ user.id }}</td>
            </tr>
          } @empty {
            <tr><td colspan="3" class="muted">No users on this page.</td></tr>
          }
        </tbody>
      </table>
      <app-pager [page]="page.page" [pageSize]="page.pageSize" [total]="page.total" noun="users" (pageChange)="load($event)" />
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
})
export class AdminUsersPage {
  private readonly admin = inject(AdminService);

  protected readonly result = signal<PagedResult<AdminUser> | null>(null);
  protected readonly error = signal(false);

  constructor() {
    this.load(1);
  }

  protected load(page: number): void {
    this.admin.users(page, PAGE_SIZE).subscribe({
      next: (result) => {
        this.error.set(false);
        this.result.set(result);
      },
      error: () => this.error.set(true),
    });
  }
}
