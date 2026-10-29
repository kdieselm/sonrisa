import { Type } from '@angular/core';
import { FormGroup, NonNullableFormBuilder } from '@angular/forms';
import { EmailConfigForm, EmailConfigSummary, createEmailConfigForm, emailConfigData } from './email/email-config';
import { SlackConfigForm, SlackConfigSummary, createSlackConfigForm, slackConfigData } from './slack/slack-config';

/**
 * Frontend side of one notification channel (spec §2.11). Pages render these components through
 * `NgComponentOutlet` and never branch on a channel key (AC11). How to add one: `docs/how-to/add-a-channel.md`.
 */
export interface ChannelUi {
  /** Config fields; receives the `form` input (and `editing`) from the destinations page. */
  configForm: Type<unknown>;
  /** `start`: the catalog's default config on create, the stored (masked) config on edit. */
  createConfigForm(fb: NonNullableFormBuilder, start: unknown): FormGroup;
  /** The `config` object the backend channel validates; null on edit = keep the stored config. */
  configData(form: FormGroup, editing: boolean): Record<string, unknown> | null;
  /** One-line view of a destination's masked config; receives the `config` input. */
  summary: Type<unknown>;
}

/** Channel key (as the backend sends it) → its components. The one place that lists the channels. */
export const CHANNEL_UI: Readonly<Record<string, ChannelUi>> = {
  email: {
    configForm: EmailConfigForm,
    createConfigForm: createEmailConfigForm,
    configData: emailConfigData,
    summary: EmailConfigSummary,
  },
  slack: {
    configForm: SlackConfigForm,
    createConfigForm: createSlackConfigForm,
    configData: slackConfigData,
    summary: SlackConfigSummary,
  },
};

export function channelUi(key: string): ChannelUi | undefined {
  return Object.hasOwn(CHANNEL_UI, key) ? CHANNEL_UI[key] : undefined;
}
