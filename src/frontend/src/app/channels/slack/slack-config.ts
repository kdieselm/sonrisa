import { Component, computed, input } from '@angular/core';
import { FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

/** Mirrors `Sonrisa.Infrastructure.Channels.Slack.SlackConfig`. The API only ever sends the masked URL. */
interface SlackConfig {
  webhookUrl: string | null;
}

/** The URL field always starts empty: the stored URL is a secret and only its masked form is known here. */
export function createSlackConfigForm(fb: NonNullableFormBuilder): FormGroup {
  return fb.group({
    webhookUrl: ['', [Validators.maxLength(500), Validators.pattern(/^https?:\/\/\S+$/)]],
  });
}

/**
 * Create: an empty URL asks the API for a new local mock receiver URL. Edit: an empty URL keeps the stored one
 * (null = "do not change the config").
 */
export function slackConfigData(form: FormGroup, editing: boolean): Record<string, unknown> | null {
  const url = ((form.getRawValue() as SlackConfig).webhookUrl ?? '').trim();
  if (!url) {
    return editing ? null : {};
  }
  return { webhookUrl: url };
}

@Component({
  selector: 'app-slack-config-form',
  imports: [ReactiveFormsModule],
  template: `
    <div [formGroup]="form()" class="form">
      <label>
        Incoming webhook URL
        <input type="url" formControlName="webhookUrl" placeholder="https://hooks.slack.com/services/…" />
        <small>
          @if (editing()) {
            Leave empty to keep the current URL.
          } @else {
            Leave empty to use the local Slack mock (messages appear in the admin view).
          }
        </small>
      </label>
    </div>
  `,
})
export class SlackConfigForm {
  readonly form = input.required<FormGroup>();
  readonly editing = input(false);
}

@Component({
  selector: 'app-slack-config-summary',
  template: `<span class="mono">{{ text() }}</span>`,
})
export class SlackConfigSummary {
  readonly config = input.required<unknown>();
  protected readonly text = computed(() => (this.config() as SlackConfig).webhookUrl ?? '');
}
