import { DatePipe, NgComponentOutlet } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CategoryUi, categoryUi } from '../categories/category-registry';
import { errorMessages } from '../pages/form-errors';
import { Alert, Catalog, Destination, RecentMatches, UserApi } from '../user/user-api.service';

/**
 * Create or edit one alert (spec §2.2, §2.5): name, category (fixed after creation), the category's rule form from
 * the registry, and its destinations. An existing alert also lists up to 3 recent matching events (D11); nothing is
 * sent for them.
 */
@Component({
  selector: 'app-alert-edit-page',
  imports: [DatePipe, NgComponentOutlet, ReactiveFormsModule, RouterLink],
  template: `
    <p><a routerLink="/alerts">← Alerts</a></p>
    <h1>{{ alert() ? alert()!.name : 'New alert' }}</h1>

    @if (alert(); as a) {
      <p class="muted">
        {{ a.enabled ? 'On' : 'Off' }} · active since {{ a.activeSince | date: 'medium' }}.
        Only events first seen after this time notify you; changing the rule moves it to now.
      </p>
    }

    <form [formGroup]="form" (ngSubmit)="submit()" class="form">
      <label>
        Name
        <input type="text" formControlName="name" maxlength="100" required />
      </label>
      <label>
        Category
        <select formControlName="categoryKey" required (change)="categoryChanged()">
          <option value="" disabled>Choose…</option>
          @for (c of categories(); track c.key) {
            <option [value]="c.key">{{ c.name }}</option>
          }
        </select>
      </label>

      @if (selected(); as c) {
        <ng-container *ngComponentOutlet="c.ruleForm; inputs: { form: ruleForm()! }" />
      }

      <fieldset class="form">
        <legend>Destinations</legend>
        @for (d of destinations(); track d.id) {
          <label>
            <input type="checkbox" [checked]="chosen().has(d.id)" (change)="toggleDestination(d.id)" />
            {{ d.displayName }} ({{ d.channelName }})
          </label>
        } @empty {
          <p class="muted">You have no destinations yet. <a routerLink="/destinations">Add one first.</a></p>
        }
      </fieldset>

      @for (message of errors(); track message) {
        <p class="error">{{ message }}</p>
      }

      <button type="submit" [disabled]="form.invalid || !ruleForm() || ruleForm()!.invalid || chosen().size === 0 || busy()">
        {{ alert() ? 'Save' : 'Create alert' }}
      </button>
    </form>

    @if (alert()) {
      <h2>Recent matches</h2>
      @if (recent(); as r) {
        @if (!r.supported) {
          <p class="muted">{{ r.reason }}</p>
        } @else {
          <p class="muted">Up to 3 events of the last 24 hours that match this rule. They were not sent to you.</p>
          <ul>
            @for (m of r.items; track m.eventId) {
              <li>
                @if (m.url) {
                  <a [href]="m.url" target="_blank" rel="noopener">{{ m.title }}</a>
                } @else {
                  {{ m.title }}
                }
                @if (m.isSimulated) {
                  <span class="badge">simulated</span>
                }
                <span class="muted"> · {{ m.sourceName }} · {{ m.firstSeenAt | date: 'short' }}</span>
                @if (selected(); as c) {
                  · <ng-container *ngComponentOutlet="c.summary; inputs: { data: m.data }" />
                }
              </li>
            } @empty {
              <li class="muted">No matching events in the last 24 hours.</li>
            }
          </ul>
        }
      }
    }
  `,
})
export class AlertEditPage {
  /** Route parameter `id` (component input binding); absent on `/alerts/new`. */
  readonly id = input<string>();

  private readonly api = inject(UserApi);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly router = inject(Router);

  private readonly catalog = signal<Catalog | null>(null);
  protected readonly categories = computed(() =>
    (this.catalog()?.categories ?? []).filter((c) => categoryUi(c.key) !== undefined),
  );
  protected readonly destinations = signal<Destination[]>([]);
  protected readonly alert = signal<Alert | null>(null);
  protected readonly recent = signal<RecentMatches | null>(null);

  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    categoryKey: ['', Validators.required],
  });
  protected readonly selected = signal<CategoryUi | null>(null);
  protected readonly ruleForm = signal<FormGroup | null>(null);
  protected readonly chosen = signal(new Set<number>());
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);

  constructor() {
    this.api.catalog().subscribe({
      next: (c) => this.catalog.set(c),
      error: () => this.errors.set(['Could not load the categories.']),
    });
    this.api.destinations().subscribe({
      next: (page) => this.destinations.set(page.items),
      error: () => this.errors.set(['Could not load your destinations.']),
    });
  }

  ngOnInit(): void {
    const id = Number(this.id());
    if (!this.id() || !Number.isInteger(id)) {
      return;
    }
    this.api.alert(id).subscribe({
      next: (a) => this.show(a),
      error: () => this.errors.set(['This alert does not exist.']),
    });
  }

  protected categoryChanged(): void {
    const c = categoryUi(this.form.controls.categoryKey.value) ?? null;
    this.selected.set(c);
    this.ruleForm.set(c?.createRuleForm(this.fb, null) ?? null);
  }

  protected toggleDestination(id: number): void {
    const next = new Set(this.chosen());
    if (!next.delete(id)) {
      next.add(id);
    }
    this.chosen.set(next);
  }

  protected submit(): void {
    const c = this.selected();
    const ruleForm = this.ruleForm();
    if (!c || !ruleForm) {
      return;
    }
    const { name, categoryKey } = this.form.getRawValue();
    const request = { name, rule: c.ruleData(ruleForm), destinationIds: [...this.chosen()] };
    const current = this.alert();
    const call = current
      ? this.api.updateAlert(current.id, request)
      : this.api.createAlert({ ...request, categoryKey });

    this.busy.set(true);
    this.errors.set([]);
    call.subscribe({
      next: (saved) => {
        this.busy.set(false);
        if (current) {
          this.show(saved);
        } else {
          void this.router.navigate(['/alerts', saved.id]);
        }
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.errors.set(errorMessages(error, 'Could not save the alert.'));
      },
    });
  }

  private show(a: Alert): void {
    const c = categoryUi(a.categoryKey) ?? null;
    this.alert.set(a);
    this.form.setValue({ name: a.name, categoryKey: a.categoryKey });
    this.form.controls.categoryKey.disable(); // the category of an alert does not change
    this.selected.set(c);
    this.ruleForm.set(c?.createRuleForm(this.fb, a.rule) ?? null);
    this.chosen.set(new Set(a.destinationIds));
    this.api.recentMatches(a.id).subscribe({
      next: (r) => this.recent.set(r),
      error: () => this.recent.set(null),
    });
  }
}
