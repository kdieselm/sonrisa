import { Type } from '@angular/core';
import { FormGroup, NonNullableFormBuilder } from '@angular/forms';
import { DisasterSimulateForm, createDisasterSimulateForm, disasterSimulateData } from './disaster/disaster-simulate-form';
import { DisasterSummary } from './disaster/disaster-summary';
import { NewsSimulateForm, createNewsSimulateForm, newsSimulateData } from './news/news-simulate-form';
import { NewsSummary } from './news/news-summary';

/**
 * Frontend side of one event category (spec §2.11). Pages render these components through `NgComponentOutlet`
 * and never branch on a category key (AC11). How to add one: `docs/how-to/add-a-category.md`.
 */
export interface CategoryUi {
  /** Simulate form fields; receives the `form` input created by `createSimulateForm`. */
  simulateForm: Type<unknown>;
  createSimulateForm(fb: NonNullableFormBuilder): FormGroup;
  /** Turns the form value into the `data` object the backend category validates. */
  simulateData(form: FormGroup): Record<string, unknown>;
  /** One-line view of the event's `data` in the admin list; receives the `data` input. */
  summary: Type<unknown>;
}

/** Category key (as the backend sends it) → its components. The one place that lists the categories. */
export const CATEGORY_UI: Readonly<Record<string, CategoryUi>> = {
  disaster: {
    simulateForm: DisasterSimulateForm,
    createSimulateForm: createDisasterSimulateForm,
    simulateData: disasterSimulateData,
    summary: DisasterSummary,
  },
  news: {
    simulateForm: NewsSimulateForm,
    createSimulateForm: createNewsSimulateForm,
    simulateData: newsSimulateData,
    summary: NewsSummary,
  },
};

export function categoryUi(key: string): CategoryUi | undefined {
  return Object.hasOwn(CATEGORY_UI, key) ? CATEGORY_UI[key] : undefined;
}
