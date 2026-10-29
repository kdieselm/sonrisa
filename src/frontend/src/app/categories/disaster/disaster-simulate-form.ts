import { Component, input } from '@angular/core';
import { FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

/** Disaster fields of the simulate form; mirrors `DisasterCategory.ValidateSimulatedData`. */
export function createDisasterSimulateForm(fb: NonNullableFormBuilder): FormGroup {
  return fb.group({
    magnitude: [5.5, [Validators.required, Validators.min(0), Validators.max(10)]],
    place: ['', [Validators.required, Validators.maxLength(200)]],
    latitude: ['', [Validators.min(-90), Validators.max(90)]],
    longitude: ['', [Validators.min(-180), Validators.max(180)]],
    depthKm: ['', [Validators.min(-10), Validators.max(1000)]],
  });
}

export function disasterSimulateData(form: FormGroup): Record<string, unknown> {
  const value = form.getRawValue() as Record<string, string | number | null>;
  // An emptied number input yields null; an untouched one keeps the initial ''.
  const optional = (v: string | number | null | undefined) => (v === '' || v == null ? null : Number(v));
  return {
    magnitude: Number(value['magnitude']),
    place: value['place'],
    latitude: optional(value['latitude']),
    longitude: optional(value['longitude']),
    depthKm: optional(value['depthKm']),
  };
}

@Component({
  selector: 'app-disaster-simulate-form',
  imports: [ReactiveFormsModule],
  template: `
    <div [formGroup]="form()" class="form">
      <label>
        Magnitude
        <input type="number" step="0.1" min="0" max="10" formControlName="magnitude" required />
      </label>
      <label>
        Place
        <input type="text" formControlName="place" placeholder="e.g. 10 km S of Example, Chile" required />
      </label>
      <label>
        Latitude (optional)
        <input type="number" step="any" formControlName="latitude" />
      </label>
      <label>
        Longitude (optional)
        <input type="number" step="any" formControlName="longitude" />
      </label>
      <label>
        Depth in km (optional)
        <input type="number" step="any" formControlName="depthKm" />
      </label>
    </div>
  `,
})
export class DisasterSimulateForm {
  readonly form = input.required<FormGroup>();
}
