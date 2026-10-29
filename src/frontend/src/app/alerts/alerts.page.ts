import { DatePipe, NgComponentOutlet } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CategoryUi, categoryUi } from '../categories/category-registry';
import { Alert, UserApi } from '../user/user-api.service';

/** The user's alerts (spec §2.2, AC5): list, enable/disable, delete; create and edit on the alert page. */
@Component({
  selector: 'app-alerts-page',
  imports: [DatePipe, NgComponentOutlet, RouterLink],
  template: `
    <h1>Alerts</h1>
    <p class="actions"><a routerLink="/alerts/new">New alert</a></p>

    @if (error()) {
      <p class="error">{{ error() }}</p>
    }

    <table class="table">
      <thead>
        <tr><th>Name</th><th>Category</th><th>Rule</th><th>Active since</th><th>Status</th><th></th></tr>
      </thead>
      <tbody>
        @for (a of alerts(); track a.id) {
          <tr>
            <td><a [routerLink]="['/alerts', a.id]">{{ a.name }}</a></td>
            <td>{{ a.categoryName }}</td>
            <td>
              @if (ui(a.categoryKey); as c) {
                <ng-container *ngComponentOutlet="c.ruleSummary; inputs: { rule: a.rule }" />
              }
            </td>
            <td>{{ a.activeSince | date: 'short' }}</td>
            <td>
              {{ a.enabled ? 'On' : 'Off' }}
              @if (a.destinationIds.length === 0) {
                <span class="error">· no destination</span>
              }
            </td>
            <td class="actions">
              <button type="button" class="link" (click)="toggle(a)">{{ a.enabled ? 'Disable' : 'Enable' }}</button>
              <button type="button" class="link" (click)="remove(a)">Delete</button>
            </td>
          </tr>
        } @empty {
          <tr><td colspan="6" class="muted">No alerts yet.</td></tr>
        }
      </tbody>
    </table>
  `,
})
export class AlertsPage {
  private readonly api = inject(UserApi);

  protected readonly alerts = signal<Alert[]>([]);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected ui(key: string): CategoryUi | undefined {
    return categoryUi(key);
  }

  protected toggle(a: Alert): void {
    this.api.setAlertEnabled(a.id, !a.enabled).subscribe({
      next: () => this.load(),
      error: () => this.error.set('Could not change the alert.'),
    });
  }

  protected remove(a: Alert): void {
    if (!confirm(`Delete “${a.name}”? Its pending notifications fail; its history stays.`)) {
      return;
    }
    this.api.deleteAlert(a.id).subscribe({
      next: () => this.load(),
      error: () => this.error.set('Could not delete the alert.'),
    });
  }

  private load(): void {
    this.api.alerts().subscribe({
      next: (page) => {
        this.error.set(null);
        this.alerts.set(page.items);
      },
      error: () => this.error.set('Could not load your alerts.'),
    });
  }
}
