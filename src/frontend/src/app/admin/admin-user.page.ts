import { DatePipe, NgComponentOutlet } from '@angular/common';
import { Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CategoryUi, categoryUi } from '../categories/category-registry';
import { ChannelUi, channelUi } from '../channels/channel-registry';
import { Alert, Destination } from '../user/user-api.service';
import { AdminNav } from './admin-nav';
import { AdminService, AdminUser } from './admin.service';

/** Mirrors `Sonrisa.Contracts.Dtos.Admin.AdminUserDetailDto`. */
export interface AdminUserDetail {
  user: AdminUser;
  alerts: Alert[];
  destinations: Destination[];
}

/** Admin view of one user's alerts and destinations (spec §2.9), read-only; webhook URLs are masked. */
@Component({
  selector: 'app-admin-user-page',
  imports: [AdminNav, DatePipe, NgComponentOutlet, RouterLink],
  template: `
    <h1>Admin</h1>
    <app-admin-nav />
    <p><a routerLink="/admin/users">← Users</a></p>
    @if (error()) {
      <p class="error">Could not load this user.</p>
    } @else if (detail(); as d) {
      <h2>{{ d.user.email }}</h2>
      <p class="muted">{{ d.user.roles.length ? d.user.roles.join(', ') : 'Normal user' }} · <span class="mono">{{ d.user.id }}</span></p>

      <h3>Alerts</h3>
      <table class="table">
        <thead><tr><th>Name</th><th>Category</th><th>Rule</th><th>Status</th><th>Active since</th><th>Destinations</th></tr></thead>
        <tbody>
          @for (a of d.alerts; track a.id) {
            <tr>
              <td>{{ a.name }}</td>
              <td>{{ a.categoryName }}</td>
              <td>
                @if (category(a.categoryKey); as c) {
                  <ng-container *ngComponentOutlet="c.ruleSummary; inputs: { rule: a.rule }" />
                }
              </td>
              <td>{{ a.enabled ? 'On' : 'Off' }}</td>
              <td>{{ a.activeSince | date: 'short' }}</td>
              <td class="mono">{{ a.destinationIds.join(', ') || '—' }}</td>
            </tr>
          } @empty {
            <tr><td colspan="6" class="muted">No alerts.</td></tr>
          }
        </tbody>
      </table>

      <h3>Destinations</h3>
      <table class="table">
        <thead><tr><th>ID</th><th>Name</th><th>Channel</th><th>Settings</th></tr></thead>
        <tbody>
          @for (x of d.destinations; track x.id) {
            <tr>
              <td class="mono">{{ x.id }}</td>
              <td>{{ x.displayName }}</td>
              <td>{{ x.channelName }}</td>
              <td>
                @if (channel(x.channelKey); as c) {
                  <ng-container *ngComponentOutlet="c.summary; inputs: { config: x.config }" />
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="4" class="muted">No destinations.</td></tr>
          }
        </tbody>
      </table>
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
})
export class AdminUserPage {
  /** Route parameter `id` (component input binding). */
  readonly id = input.required<string>();

  private readonly admin = inject(AdminService);
  protected readonly detail = signal<AdminUserDetail | null>(null);
  protected readonly error = signal(false);

  ngOnInit(): void {
    this.admin.user(this.id()).subscribe({
      next: (d) => this.detail.set(d),
      error: () => this.error.set(true),
    });
  }

  protected category(key: string): CategoryUi | undefined {
    return categoryUi(key);
  }

  protected channel(key: string): ChannelUi | undefined {
    return channelUi(key);
  }
}
