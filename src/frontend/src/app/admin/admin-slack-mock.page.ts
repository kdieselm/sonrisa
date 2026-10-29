import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { AdminNav } from './admin-nav';
import { AdminService, PagedResult, SlackMockMessage } from './admin.service';
import { Pager } from '../shared/pager';

const PAGE_SIZE = 50;

/** Admin view: the messages the local Slack mock receiver accepted, newest first (spec §2.6, §2.9). */
@Component({
  selector: 'app-admin-slack-mock-page',
  imports: [AdminNav, DatePipe, Pager],
  template: `
    <h1>Admin</h1>
    <app-admin-nav />
    <div class="actions filters">
      <button type="button" (click)="load(result()?.page ?? 1)">Refresh</button>
    </div>

    @if (error()) {
      <p class="error">Could not load the Slack mock messages.</p>
    } @else if (result(); as page) {
      <table class="table">
        <thead>
          <tr><th>Received</th><th>Hook</th><th>Message</th></tr>
        </thead>
        <tbody>
          @for (m of page.items; track m.id) {
            <tr>
              <td>{{ m.receivedAt | date: 'yyyy-MM-dd HH:mm:ss' }}</td>
              <td class="mono">{{ m.hookName }}</td>
              <td class="wrap"><pre class="message">{{ m.body }}</pre></td>
            </tr>
          } @empty {
            <tr><td colspan="3" class="muted">No messages yet.</td></tr>
          }
        </tbody>
      </table>
      <app-pager
        [page]="page.page"
        [pageSize]="page.pageSize"
        [total]="page.total"
        noun="messages"
        (pageChange)="load($event)"
      />
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
  styles: `
    .message {
      margin: 0;
      white-space: pre-wrap;
      font: inherit;
    }
  `,
})
export class AdminSlackMockPage {
  private readonly admin = inject(AdminService);

  protected readonly result = signal<PagedResult<SlackMockMessage> | null>(null);
  protected readonly error = signal(false);

  constructor() {
    this.load(1);
  }

  protected load(page: number): void {
    this.admin.slackMock(page, PAGE_SIZE).subscribe({
      next: (result) => {
        this.error.set(false);
        this.result.set(result);
      },
      error: () => this.error.set(true),
    });
  }
}
