import { Component, computed, input } from '@angular/core';
import { FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

/** Mirrors `Sonrisa.Domain.Categories.Disaster.DisasterRule` (spec §2.2, D27). */
interface DisasterRule {
  minMagnitude: number;
  placeContains: string | null;
  near: { latitude: number; longitude: number; radiusKm: number } | null;
}

/** Point and radius are all-or-nothing: the form checks that the three fields are filled together. */
export function createDisasterRuleForm(fb: NonNullableFormBuilder, rule: unknown): FormGroup {
  const r = rule as DisasterRule | null;
  return fb.group(
    {
      minMagnitude: [r?.minMagnitude ?? 6, [Validators.required, Validators.min(4.5), Validators.max(10)]],
      placeContains: [r?.placeContains ?? '', Validators.maxLength(100)],
      latitude: [r?.near?.latitude ?? '', [Validators.min(-90), Validators.max(90)]],
      longitude: [r?.near?.longitude ?? '', [Validators.min(-180), Validators.max(180)]],
      radiusKm: [r?.near?.radiusKm ?? '', [Validators.min(1), Validators.max(20000)]],
    },
    {
      validators: (group) => {
        const filled = ['latitude', 'longitude', 'radiusKm'].filter((k) => !isEmpty(group.get(k)?.value)).length;
        return filled === 0 || filled === 3 ? null : { nearIncomplete: true };
      },
    },
  );
}

export function disasterRuleData(form: FormGroup): Record<string, unknown> {
  const v = form.getRawValue() as Record<string, string | number | null>;
  const near = isEmpty(v['latitude'])
    ? null
    : { latitude: Number(v['latitude']), longitude: Number(v['longitude']), radiusKm: Number(v['radiusKm']) };
  return {
    minMagnitude: Number(v['minMagnitude']),
    placeContains: String(v['placeContains'] ?? '').trim() || null,
    near,
  };
}

function isEmpty(v: unknown): boolean {
  return v === '' || v == null;
}

@Component({
  selector: 'app-disaster-rule-form',
  imports: [ReactiveFormsModule],
  template: `
    <div [formGroup]="form()" class="form">
      <label>
        Minimum magnitude
        <input type="number" step="0.1" min="4.5" max="10" formControlName="minMagnitude" required />
        <small>The USGS feed starts at 4.5.</small>
      </label>
      <label>
        Place contains (optional)
        <input type="text" formControlName="placeContains" placeholder="e.g. Japan" />
        <small>Text match on the USGS place description, case-insensitive.</small>
      </label>
      <fieldset class="form">
        <legend>Near a point (optional)</legend>
        <label>Latitude <input type="number" step="any" formControlName="latitude" /></label>
        <label>Longitude <input type="number" step="any" formControlName="longitude" /></label>
        <label>Radius in km <input type="number" step="any" min="1" max="20000" formControlName="radiusKm" /></label>
        @if (form().hasError('nearIncomplete')) {
          <p class="error">Give latitude, longitude and radius together, or none of them.</p>
        }
      </fieldset>
    </div>
  `,
})
export class DisasterRuleForm {
  readonly form = input.required<FormGroup>();
}

/** "M ≥ 6.0 · place “Japan” · within 300 km of 35.68, 139.69". */
@Component({
  selector: 'app-disaster-rule-summary',
  template: `<span>{{ text() }}</span>`,
})
export class DisasterRuleSummary {
  readonly rule = input.required<unknown>();

  protected readonly text = computed(() => {
    const r = this.rule() as DisasterRule;
    const parts = [`M ≥ ${r.minMagnitude.toFixed(1)}`];
    if (r.placeContains) {
      parts.push(`place “${r.placeContains}”`);
    }
    if (r.near) {
      parts.push(`within ${r.near.radiusKm} km of ${r.near.latitude}, ${r.near.longitude}`);
    }
    return parts.join(' · ');
  });
}
