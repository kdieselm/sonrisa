import { Component, computed, input, output } from '@angular/core';

/** Previous / next controls for a `PagedResult` (page is 1-based). */
@Component({
  selector: 'app-pager',
  template: `
    <div class="actions">
      <button type="button" (click)="pageChange.emit(page() - 1)" [disabled]="page() <= 1">{{ previousLabel() }}</button>
      <span class="muted">Page {{ page() }} of {{ pageCount() }} ({{ total() }} {{ noun() }})</span>
      <button type="button" (click)="pageChange.emit(page() + 1)" [disabled]="page() >= pageCount()">{{ nextLabel() }}</button>
    </div>
  `,
})
export class Pager {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly noun = input('entries');
  readonly previousLabel = input('Previous');
  readonly nextLabel = input('Next');
  readonly pageChange = output<number>();

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
}
