import { Component, computed, input } from '@angular/core';
import { FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

/** Mirrors `Sonrisa.Infrastructure.Channels.Email.EmailConfig`. */
interface EmailConfig {
  address: string | null;
}

/** Starts from the stored config when editing, else from the channel default (the account email). */
export function createEmailConfigForm(fb: NonNullableFormBuilder, start: unknown): FormGroup {
  return fb.group({
    address: [(start as EmailConfig | null)?.address ?? '', [Validators.required, Validators.email, Validators.maxLength(254)]],
  });
}

/** The address is not a secret, so it is always sent. */
export function emailConfigData(form: FormGroup): Record<string, unknown> {
  return { address: (form.getRawValue() as EmailConfig).address?.trim() };
}

@Component({
  selector: 'app-email-config-form',
  imports: [ReactiveFormsModule],
  template: `
    <div [formGroup]="form()" class="form">
      <label>
        Email address
        <input type="email" formControlName="address" required />
        <small>Defaults to your account email.</small>
      </label>
    </div>
  `,
})
export class EmailConfigForm {
  readonly form = input.required<FormGroup>();
}

@Component({
  selector: 'app-email-config-summary',
  template: `<span>{{ text() }}</span>`,
})
export class EmailConfigSummary {
  readonly config = input.required<unknown>();
  protected readonly text = computed(() => (this.config() as EmailConfig).address ?? '');
}
