import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { AdminNav } from './admin-nav';
import { AdminService, AuditEntry, PagedResult } from './admin.service';
import { Pager } from './pager';

const PAGE_SIZE = 50;

/** Admin view: the audit log, newest first, paged (spec §2.10). */
@Component({
  selector: 'app-admin-audit-page',
  imports: [AdminNav, DatePipe, Pager],
  template: `
    <h1>Admin</h1>
    <app-admin-nav />
    @if (error()) {
      <p class="error">Could not load the audit log.</p>
    } @else if (result(); as page) {
      <table class="table">
        <thead>
          <tr><th>Time</th><th>Actor</th><th>Action</th><th>Target</th><th>Outcome</th></tr>
        </thead>
        <tbody>
          @for (entry of page.items; track entry.id) {
            <tr>
              <td>{{ entry.at | date: 'yyyy-MM-dd HH:mm:ss' }}</td>
              <td>{{ actor(entry) }}</td>
              <td class="mono">{{ entry.action }}</td>
              <td>{{ entry.targetType ?? '' }} <span class="mono">{{ entry.targetId ?? '' }}</span></td>
              <td [class.error]="entry.outcome === 'Failure'">{{ entry.outcome }}</td>
            </tr>
          } @empty {
            <tr><td colspan="5" class="muted">No entries yet.</td></tr>
          }
        </tbody>
      </table>
      <app-pager
        [page]="page.page"
        [pageSize]="page.pageSize"
        [total]="page.total"
        previousLabel="Newer"
        nextLabel="Older"
        (pageChange)="load($event)"
      />
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
})
export class AdminAuditPage {
  private readonly admin = inject(AdminService);

  protected readonly result = signal<PagedResult<AuditEntry> | null>(null);
  protected readonly error = signal(false);

  constructor() {
    this.load(1);
  }

  protected load(page: number): void {
    this.admin.audit(page, PAGE_SIZE).subscribe({
      next: (result) => {
        this.error.set(false);
        this.result.set(result);
      },
      error: () => this.error.set(true),
    });
  }

  protected actor(entry: AuditEntry): string {
    return entry.actorEmail ?? entry.actorUserId ?? '—';
  }
}
