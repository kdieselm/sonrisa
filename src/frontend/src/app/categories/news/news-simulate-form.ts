import { Component, input } from '@angular/core';
import { FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

/** News fields of the simulate form (the headline is the event title); mirrors `NewsCategory.ValidateSimulatedData`. */
export function createNewsSimulateForm(fb: NonNullableFormBuilder): FormGroup {
  return fb.group({
    feedName: ['', Validators.maxLength(100)],
  });
}

export function newsSimulateData(form: FormGroup): Record<string, unknown> {
  const value = form.getRawValue() as Record<string, string>;
  return { feedName: value['feedName'] || null };
}

@Component({
  selector: 'app-news-simulate-form',
  imports: [ReactiveFormsModule],
  template: `
    <div [formGroup]="form()" class="form">
      <label>
        Feed name (optional)
        <input type="text" formControlName="feedName" placeholder="Simulated feed" />
      </label>
    </div>
  `,
})
export class NewsSimulateForm {
  readonly form = input.required<FormGroup>();
}
