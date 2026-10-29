import { Component, computed, input } from '@angular/core';

/** Mirrors `Sonrisa.Domain.Categories.Disaster.DisasterData`. */
interface DisasterData {
  magnitude: number;
  place: string;
  latitude: number | null;
  longitude: number | null;
  depthKm: number | null;
}

/** "M 5.2 · 10 km S of …" for the admin event list. */
@Component({
  selector: 'app-disaster-summary',
  template: `<span>{{ text() }}</span>`,
})
export class DisasterSummary {
  readonly data = input.required<unknown>();

  protected readonly text = computed(() => {
    const d = this.data() as DisasterData;
    return `M ${d.magnitude.toFixed(1)} · ${d.place}`;
  });
}
