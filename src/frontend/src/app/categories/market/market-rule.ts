import { Component, computed, inject, input, OnInit } from '@angular/core';
import { FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MarketAssets } from './market-assets';

/** Mirrors `Sonrisa.Domain.Categories.Market.MarketRule` (spec §2.2, §2.4). */
interface MarketRule {
  asset: string;
  direction: 'up' | 'down' | 'either';
  thresholdPercent: number;
  stepPercent: number | null;
}

/** Threshold and step are multiples of 0.5 (the grid of spec §2.4). */
const halfPoint = (control: { value: unknown }) => {
  const v = control.value;
  if (v === '' || v == null) {
    return null;
  }
  return Math.abs(Number(v) * 2 - Math.round(Number(v) * 2)) < 1e-9 ? null : { halfPoint: true };
};

export function createMarketRuleForm(fb: NonNullableFormBuilder, rule: unknown): FormGroup {
  const r = rule as MarketRule | null;
  return fb.group({
    asset: [r?.asset ?? '', Validators.required],
    direction: [r?.direction ?? 'up', Validators.required],
    thresholdPercent: [r?.thresholdPercent ?? 5, [Validators.required, Validators.min(1), Validators.max(100), halfPoint]],
    stepPercent: [r?.stepPercent ?? '', [Validators.min(0.5), Validators.max(100), halfPoint]],
  });
}

export function marketRuleData(form: FormGroup): Record<string, unknown> {
  const v = form.getRawValue() as Record<string, string | number | null>;
  return {
    asset: v['asset'],
    direction: v['direction'],
    thresholdPercent: Number(v['thresholdPercent']),
    stepPercent: v['stepPercent'] === '' || v['stepPercent'] == null ? null : Number(v['stepPercent']),
  };
}

@Component({
  selector: 'app-market-rule-form',
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
        Direction
        <select formControlName="direction">
          <option value="up">Rises</option>
          <option value="down">Falls</option>
          <option value="either">Either way</option>
        </select>
      </label>
      <label>
        Threshold (% change)
        <input type="number" step="0.5" min="1" max="100" formControlName="thresholdPercent" required />
        <small>
          1 or more, in steps of 0.5. You are notified when the change crosses it, and again only after it has
          fallen back more than 1 point below it.
        </small>
      </label>
      <label>
        Repeat every (optional, % points)
        <input type="number" step="0.5" min="0.5" max="100" formControlName="stepPercent" />
        <small>While the move continues, notify again for every further step beyond the threshold.</small>
      </label>
    </div>
  `,
})
export class MarketRuleForm implements OnInit {
  protected readonly marketAssets = inject(MarketAssets);
  readonly form = input.required<FormGroup>();

  ngOnInit(): void {
    this.marketAssets.load();
  }
}

/** "Bitcoin rises ≥ 5% · repeat every 2.5%". */
@Component({
  selector: 'app-market-rule-summary',
  template: `<span>{{ text() }}</span>`,
})
export class MarketRuleSummary implements OnInit {
  private readonly marketAssets = inject(MarketAssets);
  readonly rule = input.required<unknown>();

  protected readonly text = computed(() => {
    const r = this.rule() as MarketRule;
    const name = this.marketAssets.assets().find((a) => a.key === r.asset)?.displayName ?? r.asset;
    const verb = r.direction === 'up' ? 'rises' : r.direction === 'down' ? 'falls' : 'moves';
    return `${name} ${verb} ≥ ${r.thresholdPercent}%` + (r.stepPercent ? ` · repeat every ${r.stepPercent}%` : '');
  });

  ngOnInit(): void {
    this.marketAssets.load();
  }
}
