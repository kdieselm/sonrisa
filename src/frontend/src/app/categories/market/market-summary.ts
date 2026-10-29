import { Component, computed, input } from '@angular/core';

/** Mirrors `Sonrisa.Domain.Categories.Market.MarketData`. */
interface MarketData {
  asset: string;
  assetName: string;
  previousChangePercent: number;
  currentChangePercent: number;
  baseline: string;
}

export function signedPercent(value: number): string {
  const text = (Math.round(value * 100) / 100).toString();
  return `${value > 0 ? '+' : ''}${text}%`;
}

/** "Bitcoin +4.6% → +5.2% (rolling 24 h change)" for the admin event list. */
@Component({
  selector: 'app-market-summary',
  template: `<span>{{ text() }}</span>`,
})
export class MarketSummary {
  readonly data = input.required<unknown>();

  protected readonly text = computed(() => {
    const d = this.data() as MarketData;
    return `${d.assetName} ${signedPercent(d.previousChangePercent)} → ${signedPercent(d.currentChangePercent)} (${d.baseline})`;
  });
}
