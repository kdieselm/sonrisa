import { Component, computed, input } from '@angular/core';

/** Mirrors `Sonrisa.Domain.Categories.News.NewsData`. */
interface NewsData {
  feedName: string;
}

/** The feed name for the admin event list. */
@Component({
  selector: 'app-news-summary',
  template: `<span>{{ text() }}</span>`,
})
export class NewsSummary {
  readonly data = input.required<unknown>();

  protected readonly text = computed(() => (this.data() as NewsData).feedName);
}
