import { NgComponentOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ChannelUi, channelUi } from '../channels/channel-registry';
import { errorMessages } from '../pages/form-errors';
import { Catalog, Destination, UserApi } from '../user/user-api.service';

/**
 * The user's destinations (spec §2.6): list, create, edit, delete. Channel fields come from the channel registry;
 * a Slack webhook URL is only ever shown masked.
 */
@Component({
  selector: 'app-destinations-page',
  imports: [NgComponentOutlet, ReactiveFormsModule],
  template: `
    <h1>Destinations</h1>
    <p class="muted">Where your alerts are delivered. One alert can use several destinations.</p>

    <table class="table">
      <thead>
        <tr><th>Name</th><th>Channel</th><th>Settings</th><th></th></tr>
      </thead>
      <tbody>
        @for (d of destinations(); track d.id) {
          <tr>
            <td>{{ d.displayName }}</td>
            <td>{{ d.channelName }}</td>
            <td>
              @if (ui(d.channelKey); as c) {
                <ng-container *ngComponentOutlet="c.summary; inputs: { config: d.config }" />
              }
            </td>
            <td class="actions">
              <button type="button" class="link" (click)="edit(d)">Edit</button>
              <button type="button" class="link" (click)="remove(d)">Delete</button>
            </td>
          </tr>
        } @empty {
          <tr><td colspan="4" class="muted">No destinations yet.</td></tr>
        }
      </tbody>
    </table>

    <h2>{{ editing() ? 'Edit “' + editing()!.displayName + '”' : 'New destination' }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" class="form">
      <label>
        Channel
        <select formControlName="channelKey" required (change)="channelChanged()">
          <option value="" disabled>Choose…</option>
          @for (c of channels(); track c.key) {
            <option [value]="c.key">{{ c.name }}</option>
          }
        </select>
      </label>
      <label>
        Name
        <input type="text" formControlName="displayName" maxlength="100" required placeholder="e.g. Work inbox" />
      </label>

      @if (selected(); as c) {
        <ng-container *ngComponentOutlet="c.configForm; inputs: { form: configForm()!, editing: !!editing() }" />
      }

      @for (message of errors(); track message) {
        <p class="error">{{ message }}</p>
      }

      <div class="actions">
        <button type="submit" [disabled]="form.invalid || !configForm() || configForm()!.invalid || busy()">
          {{ editing() ? 'Save' : 'Add destination' }}
        </button>
        @if (editing()) {
          <button type="button" (click)="reset()">Cancel</button>
        }
      </div>
    </form>
  `,
})
export class DestinationsPage {
  private readonly api = inject(UserApi);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly destinations = signal<Destination[]>([]);
  private readonly catalog = signal<Catalog | null>(null);
  protected readonly channels = computed(() =>
    (this.catalog()?.channels ?? []).filter((c) => channelUi(c.key) !== undefined),
  );

  protected readonly form = this.fb.group({
    channelKey: ['', Validators.required],
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
  });
  protected readonly editing = signal<Destination | null>(null);
  protected readonly selected = signal<ChannelUi | null>(null);
  protected readonly configForm = signal<FormGroup | null>(null);
  protected readonly errors = signal<string[]>([]);
  protected readonly busy = signal(false);

  constructor() {
    this.api.catalog().subscribe({
      next: (c) => this.catalog.set(c),
      error: () => this.errors.set(['Could not load the channels.']),
    });
    this.load();
  }

  protected ui(key: string): ChannelUi | undefined {
    return channelUi(key);
  }

  protected channelChanged(): void {
    const key = this.form.controls.channelKey.value;
    const c = channelUi(key) ?? null;
    const start = this.catalog()?.channels.find((x) => x.key === key)?.defaultConfig ?? null;
    this.selected.set(c);
    this.configForm.set(c?.createConfigForm(this.fb, start) ?? null);
  }

  protected edit(d: Destination): void {
    const c = channelUi(d.channelKey) ?? null;
    this.editing.set(d);
    this.errors.set([]);
    this.form.setValue({ channelKey: d.channelKey, displayName: d.displayName });
    this.form.controls.channelKey.disable(); // the channel of a destination does not change
    this.selected.set(c);
    this.configForm.set(c?.createConfigForm(this.fb, d.config) ?? null);
  }

  protected reset(): void {
    this.editing.set(null);
    this.form.reset({ channelKey: '', displayName: '' });
    this.form.controls.channelKey.enable();
    this.selected.set(null);
    this.configForm.set(null);
  }

  protected submit(): void {
    const c = this.selected();
    const configForm = this.configForm();
    if (!c || !configForm) {
      return;
    }
    const { channelKey, displayName } = this.form.getRawValue();
    const current = this.editing();
    const config = c.configData(configForm, current !== null);
    const request = current
      ? this.api.updateDestination(current.id, displayName, config)
      : this.api.createDestination(channelKey, displayName, config ?? {});

    this.busy.set(true);
    this.errors.set([]);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.reset();
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.errors.set(errorMessages(error, 'Could not save the destination.'));
      },
    });
  }

  protected remove(d: Destination): void {
    if (!confirm(`Delete “${d.displayName}”? It is removed from your alerts; pending notifications to it fail.`)) {
      return;
    }
    this.api.deleteDestination(d.id).subscribe({
      next: () => this.load(),
      error: () => this.errors.set(['Could not delete the destination.']),
    });
  }

  private load(): void {
    this.api.destinations().subscribe({
      next: (page) => this.destinations.set(page.items),
      error: () => this.errors.set(['Could not load your destinations.']),
    });
  }
}
