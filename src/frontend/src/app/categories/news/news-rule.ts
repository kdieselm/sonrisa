import { Component, computed, input } from '@angular/core';
import { FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

/** Mirrors `Sonrisa.Domain.Categories.News.NewsRule` (spec §2.2, D15). */
interface NewsRule {
  keywords: string[];
}

/** Keywords are edited as one per line; the backend trims, collapses spaces and drops duplicates. */
export function createNewsRuleForm(fb: NonNullableFormBuilder, rule: unknown): FormGroup {
  const keywords = (rule as NewsRule | null)?.keywords ?? [];
  return fb.group({
    keywords: [
      keywords.join('\n'),
      [
        Validators.required,
        (control: { value: string }) => {
          const count = lines(control.value).length;
          return count >= 1 && count <= 10 ? null : { keywordCount: true };
        },
      ],
    ],
  });
}

export function newsRuleData(form: FormGroup): Record<string, unknown> {
  return { keywords: lines((form.getRawValue() as { keywords: string }).keywords) };
}

function lines(text: string): string[] {
  return (text ?? '')
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l.length > 0);
}

@Component({
  selector: 'app-news-rule-form',
  imports: [ReactiveFormsModule],
  template: `
    <div [formGroup]="form()" class="form">
      <label>
        Keywords or phrases, one per line (1–10)
        <textarea formControlName="keywords" rows="4" placeholder="earthquake&#10;central bank"></textarea>
        <small>Matches whole words in the headline only, case-insensitive. The summary is not searched.</small>
      </label>
    </div>
  `,
})
export class NewsRuleForm {
  readonly form = input.required<FormGroup>();
}

@Component({
  selector: 'app-news-rule-summary',
  template: `<span>{{ text() }}</span>`,
})
export class NewsRuleSummary {
  readonly rule = input.required<unknown>();
  protected readonly text = computed(() => (this.rule() as NewsRule).keywords.map((k) => `“${k}”`).join(', '));
}
