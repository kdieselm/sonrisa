import { Component, inject, input, OnInit } from '@angular/core';
import { FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MarketAssets } from './market-assets';

/** Market fields of the simulate form; mirrors `MarketCategory.ValidateSimulatedData`. */
export function createMarketSimulateForm(fb: NonNullableFormBuilder): FormGroup {
  return fb.group({
    asset: ['', Validators.required],
    previousChangePercent: [4.6, [Validators.required, Validators.min(-100), Validators.max(1000)]],
    currentChangePercent: [5.2, [Validators.required, Validators.min(-100), Validators.max(1000)]],
  });
}

export function marketSimulateData(form: FormGroup): Record<string, unknown> {
  const v = form.getRawValue() as Record<string, string | number>;
  return {
    asset: v['asset'],
    previousChangePercent: Number(v['previousChangePercent']),
    currentChangePercent: Number(v['currentChangePercent']),
  };
}

@Component({
  selector: 'app-market-simulate-form',
  imports: [ReactiveFormsModule],
  template: `
    <div [formGroup]="form()" class="form">
      <label>
        Asset
        <select formControlName="asset" required>
          <option value="" disabled>Choose an asset</option>
          @for (asset of marketAssets.assets(); track asset.key) {
            <option [value]="asset.key">{{ asset.displayName }}</option>
          }
        </select>
      </label>
      <label>
        Previous change (%)
        <input type="number" step="0.1" formControlName="previousChangePercent" required />
      </label>
      <label>
        Current change (%)
        <input type="number" step="0.1" formControlName="currentChangePercent" required />
        <small>The provider percentage change, as in a real market event.</small>
      </label>
    </div>
  `,
})
export class MarketSimulateForm implements OnInit {
  protected readonly marketAssets = inject(MarketAssets);
  readonly form = input.required<FormGroup>();

  ngOnInit(): void {
    this.marketAssets.load();
  }
}
