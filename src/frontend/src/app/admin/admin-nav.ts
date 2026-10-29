import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** Tabs of the admin area. */
@Component({
  selector: 'app-admin-nav',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="tabs">
      <a routerLink="/admin/users" routerLinkActive="active">Users</a>
      <a routerLink="/admin/events" routerLinkActive="active">Events</a>
      <a routerLink="/admin/simulate" routerLinkActive="active">Simulate event</a>
      <a routerLink="/admin/deliveries" routerLinkActive="active">Deliveries</a>
      <a routerLink="/admin/slack-mock" routerLinkActive="active">Slack mock</a>
      <a routerLink="/admin/audit" routerLinkActive="active">Audit log</a>
    </nav>
  `,
})
export class AdminNav {}
