import { Type } from '@angular/core';
import { FormGroup, NonNullableFormBuilder } from '@angular/forms';
import { DisasterRuleForm, DisasterRuleSummary, createDisasterRuleForm, disasterRuleData } from './disaster/disaster-rule';
import { DisasterSimulateForm, createDisasterSimulateForm, disasterSimulateData } from './disaster/disaster-simulate-form';
import { DisasterSummary } from './disaster/disaster-summary';
import { MarketRuleForm, MarketRuleSummary, createMarketRuleForm, marketRuleData } from './market/market-rule';
import { MarketSimulateForm, createMarketSimulateForm, marketSimulateData } from './market/market-simulate-form';
import { MarketSummary } from './market/market-summary';
import { NewsRuleForm, NewsRuleSummary, createNewsRuleForm, newsRuleData } from './news/news-rule';
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
  /** One-line view of an event's `data` (admin list, recent matches); receives the `data` input. */
  summary: Type<unknown>;
  /** Alert rule fields; receives the `form` input created by `createRuleForm`. */
  ruleForm: Type<unknown>;
  /** `rule`: the stored rule when editing, null for a new alert. */
  createRuleForm(fb: NonNullableFormBuilder, rule: unknown): FormGroup;
  /** Turns the form value into the `rule` object the backend category validates. */
  ruleData(form: FormGroup): Record<string, unknown>;
  /** One-line view of an alert's `rule`; receives the `rule` input. */
  ruleSummary: Type<unknown>;
}

/** Category key (as the backend sends it) → its components. The one place that lists the categories. */
export const CATEGORY_UI: Readonly<Record<string, CategoryUi>> = {
  disaster: {
    simulateForm: DisasterSimulateForm,
    createSimulateForm: createDisasterSimulateForm,
    simulateData: disasterSimulateData,
    summary: DisasterSummary,
    ruleForm: DisasterRuleForm,
    createRuleForm: createDisasterRuleForm,
    ruleData: disasterRuleData,
    ruleSummary: DisasterRuleSummary,
  },
  market: {
    simulateForm: MarketSimulateForm,
    createSimulateForm: createMarketSimulateForm,
    simulateData: marketSimulateData,
    summary: MarketSummary,
    ruleForm: MarketRuleForm,
    createRuleForm: createMarketRuleForm,
    ruleData: marketRuleData,
    ruleSummary: MarketRuleSummary,
  },
  news: {
    simulateForm: NewsSimulateForm,
    createSimulateForm: createNewsSimulateForm,
    simulateData: newsSimulateData,
    summary: NewsSummary,
    ruleForm: NewsRuleForm,
    createRuleForm: createNewsRuleForm,
    ruleData: newsRuleData,
    ruleSummary: NewsRuleSummary,
  },
};

export function categoryUi(key: string): CategoryUi | undefined {
  return Object.hasOwn(CATEGORY_UI, key) ? CATEGORY_UI[key] : undefined;
}
