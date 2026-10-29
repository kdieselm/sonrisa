import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminNav } from './admin-nav';
import { AdminDelivery, AdminService, PagedResult } from './admin.service';
import { Pager } from '../shared/pager';

const PAGE_SIZE = 50;

/**
 * Admin view: the delivery log (spec §2.7, §2.9), newest first. The same rows the users see on their Notifications
 * page, plus the owner, the attempt count and the last error text.
 */
@Component({
  selector: 'app-admin-deliveries-page',
  imports: [AdminNav, DatePipe, FormsModule, Pager, RouterLink],
  template: `
    <h1>Admin</h1>
    <app-admin-nav />
    <div class="actions filters">
      <label>
        Status
        <select [(ngModel)]="status" (ngModelChange)="load(1)">
          <option value="">All</option>
          <option value="Pending">Pending</option>
          <option value="Sent">Sent</option>
          <option value="Failed">Failed</option>
        </select>
      </label>
      <button type="button" (click)="load(result()?.page ?? 1)">Refresh</button>
    </div>

    @if (error()) {
      <p class="error">Could not load the delivery log.</p>
    } @else if (result(); as page) {
      <table class="table">
        <thead>
          <tr>
            <th>Created</th><th>User</th><th>Alert</th><th>Event</th><th>Destination</th>
            <th>Status</th><th>Attempts</th><th>Last error</th>
          </tr>
        </thead>
        <tbody>
          @for (d of page.items; track d.id) {
            <tr>
              <td>{{ d.createdAt | date: 'yyyy-MM-dd HH:mm:ss' }}</td>
              <td><a [routerLink]="['/admin/users', d.ownerUserId]">{{ d.ownerEmail ?? d.ownerUserId }}</a></td>
              <td>{{ d.alertName }}</td>
              <td class="wrap">
                {{ d.eventTitle }}
                @if (d.eventIsSimulated) {
                  <span class="badge">simulated</span>
                }
              </td>
              <td>{{ d.channelName }} · {{ d.destinationName }}</td>
              <td [class.error]="d.status === 'Failed'">
                {{ d.status }}
                @if (d.status === 'Pending' && d.nextAttemptAt) {
                  <div class="muted">next try {{ d.nextAttemptAt | date: 'HH:mm:ss' }}</div>
                }
              </td>
              <td>{{ d.attempts }}</td>
              <td class="wrap mono">{{ d.lastError ?? '' }}</td>
            </tr>
          } @empty {
            <tr><td colspan="8" class="muted">No deliveries.</td></tr>
          }
        </tbody>
      </table>
      <app-pager
        [page]="page.page"
        [pageSize]="page.pageSize"
        [total]="page.total"
        noun="deliveries"
        (pageChange)="load($event)"
      />
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
})
export class AdminDeliveriesPage {
  private readonly admin = inject(AdminService);

  protected readonly result = signal<PagedResult<AdminDelivery> | null>(null);
  protected readonly error = signal(false);
  protected status = '';

  constructor() {
    this.load(1);
  }

  protected load(page: number): void {
    this.admin.deliveries(page, PAGE_SIZE, this.status).subscribe({
      next: (result) => {
        this.error.set(false);
        this.result.set(result);
      },
      error: () => this.error.set(true),
    });
  }
}
