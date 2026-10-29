import { NgComponentOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CategoryUi, categoryUi } from '../categories/category-registry';
import { errorMessages } from '../pages/form-errors';
import { AdminNav } from './admin-nav';
import { AdminEvent, AdminService, EventFilters } from './admin.service';

/**
 * Admin "simulate event" form (spec §2.3, §2.9): common fields here, category fields from the category registry.
 * The event goes through the same ingestion as a real one and is labelled "simulated".
 */
@Component({
  selector: 'app-admin-simulate-page',
  imports: [AdminNav, NgComponentOutlet, ReactiveFormsModule, RouterLink],
  template: `
    <h1>Admin</h1>
    <app-admin-nav />
    <h2>Simulate an event</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" class="form">
      <label>
        Category
        <select formControlName="categoryKey" required>
          <option value="" disabled>Choose…</option>
          @for (c of availableCategories(); track c.key) {
            <option [value]="c.key">{{ c.name }}</option>
          }
        </select>
      </label>
      <label>
        Title
        <input type="text" formControlName="title" maxlength="500" required />
        <small>For news, this is the headline that keyword alerts will search.</small>
      </label>
      <label>
        Summary (optional)
        <input type="text" formControlName="summary" maxlength="2000" />
      </label>
      <label>
        Link (optional)
        <input type="url" formControlName="url" placeholder="https://…" />
      </label>

      @if (selected(); as ui) {
        <ng-container *ngComponentOutlet="ui.simulateForm; inputs: { form: dataForm()! }" />
      }

      @for (message of errors(); track message) {
        <p class="error">{{ message }}</p>
      }

      <button type="submit" [disabled]="form.invalid || !dataForm() || dataForm()!.invalid || busy()">
        Create simulated event
      </button>
    </form>

    @if (created(); as event) {
      <p>
        Created event #{{ event.id }} “{{ event.title }}” (<span class="badge">simulated</span>).
        <a routerLink="/admin/events">See it in the event list</a>
      </p>
    }
  `,
})
export class AdminSimulatePage {
  private readonly admin = inject(AdminService);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly form = this.fb.group({
    categoryKey: ['', Validators.required],
    title: ['', [Validators.required, Validators.maxLength(500)]],
    summary: ['', Validators.maxLength(2000)],
    url: ['', Validators.pattern(/^https?:\/\/\S+$/)],
  });

  private readonly filters = signal<EventFilters | null>(null);
  private readonly categoryKey = toSignal(this.form.controls.categoryKey.valueChanges, { initialValue: '' });

  /** Only categories the backend has and the frontend can render a form for. */
  protected readonly availableCategories = computed(() =>
    (this.filters()?.categories ?? []).filter((c) => categoryUi(c.key) !== undefined),
  );
  protected readonly selected = computed<CategoryUi | null>(() => categoryUi(this.categoryKey()) ?? null);
  protected readonly dataForm = computed<FormGroup | null>(() => this.selected()?.createSimulateForm(this.fb) ?? null);

  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);
  protected readonly created = signal<AdminEvent | null>(null);

  constructor() {
    this.admin.eventFilters().subscribe({
      next: (f) => this.filters.set(f),
      error: () => this.errors.set(['Could not load the categories.']),
    });
  }

  protected submit(): void {
    const ui = this.selected();
    const dataForm = this.dataForm();
    if (!ui || !dataForm) {
      return;
    }

    const { categoryKey, title, summary, url } = this.form.getRawValue();
    this.busy.set(true);
    this.errors.set([]);
    this.created.set(null);

    this.admin
      .simulateEvent({
        categoryKey,
        title,
        summary: summary || null,
        url: url || null,
        data: ui.simulateData(dataForm),
      })
      .subscribe({
        next: (event) => {
          this.created.set(event);
          this.busy.set(false);
        },
        error: (error: unknown) => {
          this.errors.set(errorMessages(error, 'Could not create the event.'));
          this.busy.set(false);
        },
      });
  }
}
