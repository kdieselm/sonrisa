import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PagedResult } from '../admin/admin.service';
import { Pager } from '../shared/pager';
import { Notification, UserApi } from '../user/user-api.service';

const PAGE_SIZE = 50;

/**
 * The user's notifications (spec §2.12): one row per event and destination, newest first. Sending starts in S4;
 * until then every new row stays "Pending".
 */
@Component({
  selector: 'app-notifications-page',
  imports: [DatePipe, Pager, RouterLink],
  template: `
    <h1>Notifications</h1>
    @if (error()) {
      <p class="error">Could not load your notifications.</p>
    } @else if (result(); as page) {
      <table class="table">
        <thead>
          <tr><th>Time</th><th>Alert</th><th>Event</th><th>Destination</th><th>Status</th></tr>
        </thead>
        <tbody>
          @for (n of page.items; track n.id) {
            <tr>
              <td>{{ n.createdAt | date: 'short' }}</td>
              <td>
                @if (n.alertDeleted) {
                  {{ n.alertName }} <span class="muted">(deleted)</span>
                } @else {
                  <a [routerLink]="['/alerts', n.alertId]">{{ n.alertName }}</a>
                }
              </td>
              <td>
                @if (n.eventUrl) {
                  <a [href]="n.eventUrl" target="_blank" rel="noopener">{{ n.eventTitle }}</a>
                } @else {
                  {{ n.eventTitle }}
                }
                @if (n.eventIsSimulated) {
                  <span class="badge">simulated</span>
                }
              </td>
              <td>
                {{ n.channelName }} · {{ n.destinationName }}
                @if (n.destinationDeleted) {
                  <span class="muted">(deleted)</span>
                }
              </td>
              <td>
                {{ n.status }}
                @if (n.failureReason) {
                  <div class="muted">{{ n.failureReason }}</div>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="5" class="muted">No notifications yet. They appear here when an event matches one of your alerts.</td></tr>
          }
        </tbody>
      </table>
      <app-pager [page]="page.page" [pageSize]="page.pageSize" [total]="page.total" noun="notifications" (pageChange)="load($event)" />
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
})
export class NotificationsPage {
  private readonly api = inject(UserApi);

  protected readonly result = signal<PagedResult<Notification> | null>(null);
  protected readonly error = signal(false);

  constructor() {
    this.load(1);
  }

  protected load(page: number): void {
    this.api.notifications(page, PAGE_SIZE).subscribe({
      next: (result) => {
        this.error.set(false);
        this.result.set(result);
      },
      error: () => this.error.set(true),
    });
  }
}
