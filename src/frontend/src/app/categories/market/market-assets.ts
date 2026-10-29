import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

/** Mirrors `Sonrisa.Domain.Categories.Market.MarketAsset`; the list comes from configuration (spec §2.5). */
export interface MarketAsset {
  key: string;
  displayName: string;
  kind: string;
}

/** Loads the configured assets once and shares them with the market forms. */
@Injectable({ providedIn: 'root' })
export class MarketAssets {
  private readonly http = inject(HttpClient);

  /** Empty until loaded, or if the call failed (the forms then show an empty list). */
  readonly assets = signal<MarketAsset[]>([]);
  private loaded = false;

  load(): void {
    if (this.loaded) {
      return;
    }
    this.loaded = true;
    this.http.get<MarketAsset[]>('/api/market/assets').subscribe({
      next: (assets) => this.assets.set(assets),
      error: () => (this.loaded = false),
    });
  }
}
