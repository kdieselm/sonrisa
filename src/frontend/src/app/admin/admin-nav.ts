import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** Tabs of the admin area. Later slices add events, deliveries and the Slack mock list here. */
@Component({
  selector: 'app-admin-nav',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="tabs">
      <a routerLink="/admin/users" routerLinkActive="active">Users</a>
      <a routerLink="/admin/audit" routerLinkActive="active">Audit log</a>
    </nav>
  `,
})
export class AdminNav {}
