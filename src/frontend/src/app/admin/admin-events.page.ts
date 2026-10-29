import { DatePipe, NgComponentOutlet } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { categoryUi } from '../categories/category-registry';
import { AdminNav } from './admin-nav';
import { AdminEvent, AdminService, EventFilters, PagedResult } from './admin.service';
import { Pager } from '../shared/pager';

const PAGE_SIZE = 50;

/** Admin view: ingested events, newest first, filterable by category and source (spec §2.9). */
@Component({
  selector: 'app-admin-events-page',
  imports: [AdminNav, DatePipe, FormsModule, NgComponentOutlet, Pager, RouterLink],
  template: `
    <h1>Admin</h1>
    <app-admin-nav />
    <div class="actions filters">
      <label>
        Category
        <select [(ngModel)]="category" (ngModelChange)="load(1)">
          <option value="">All</option>
          @for (c of filters()?.categories ?? []; track c.key) {
            <option [value]="c.key">{{ c.name }}</option>
          }
        </select>
      </label>
      <label>
        Source
        <select [(ngModel)]="source" (ngModelChange)="load(1)">
          <option value="">All</option>
          @for (s of filters()?.sources ?? []; track s.key) {
            <option [value]="s.key">{{ s.name }}</option>
          }
        </select>
      </label>
      <button type="button" (click)="load(result()?.page ?? 1)">Refresh</button>
      <a routerLink="/admin/simulate">Simulate an event</a>
    </div>

    @if (error()) {
      <p class="error">Could not load the events.</p>
    } @else if (result(); as page) {
      <table class="table">
        <thead>
          <tr><th>First seen</th><th>Published</th><th>Category</th><th>Source</th><th>Title</th><th>Details</th></tr>
        </thead>
        <tbody>
          @for (event of page.items; track event.id) {
            <tr>
              <td>{{ event.firstSeenAt | date: 'yyyy-MM-dd HH:mm' }}</td>
              <td>{{ event.publishedAt | date: 'yyyy-MM-dd HH:mm' }}</td>
              <td>{{ event.categoryName }}</td>
              <td>
                {{ event.sourceName }}
                @if (event.isSimulated) {
                  <span class="badge">simulated</span>
                }
              </td>
              <td class="wrap">
                @if (event.url) {
                  <a [href]="event.url" target="_blank" rel="noopener noreferrer">{{ event.title }}</a>
                } @else {
                  {{ event.title }}
                }
              </td>
              <td class="wrap">
                @if (summaryOf(event); as summary) {
                  <ng-container *ngComponentOutlet="summary; inputs: { data: event.data }" />
                } @else {
                  <span class="mono">{{ json(event.data) }}</span>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="6" class="muted">No events yet. Real sources are polled every 1–5 minutes.</td></tr>
          }
        </tbody>
      </table>
      <app-pager
        [page]="page.page"
        [pageSize]="page.pageSize"
        [total]="page.total"
        noun="events"
        previousLabel="Newer"
        nextLabel="Older"
        (pageChange)="load($event)"
      />
    } @else {
      <p class="muted">Loading…</p>
    }
  `,
})
export class AdminEventsPage {
  private readonly admin = inject(AdminService);

  protected readonly result = signal<PagedResult<AdminEvent> | null>(null);
  protected readonly filters = signal<EventFilters | null>(null);
  protected readonly error = signal(false);
  protected category = '';
  protected source = '';

  constructor() {
    this.admin.eventFilters().subscribe({ next: (f) => this.filters.set(f), error: () => this.error.set(true) });
    this.load(1);
  }

  protected load(page: number): void {
    this.admin.events(page, PAGE_SIZE, this.category, this.source).subscribe({
      next: (result) => {
        this.error.set(false);
        this.result.set(result);
      },
      error: () => this.error.set(true),
    });
  }

  protected summaryOf(event: AdminEvent) {
    return categoryUi(event.categoryKey)?.summary ?? null;
  }

  protected json(data: unknown): string {
    return JSON.stringify(data);
  }
}
