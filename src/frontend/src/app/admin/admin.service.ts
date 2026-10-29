import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type { AdminUserDetail } from './admin-user.page';

/** Mirrors `Sonrisa.Contracts.Dtos.Admin.AdminUserDto`. */
export interface AdminUser {
  id: string;
  email: string;
  roles: string[];
}

/** Mirrors `Sonrisa.Contracts.Dtos.Admin.AuditEntryDto`. */
export interface AuditEntry {
  id: number;
  at: string;
  actorUserId: string | null;
  actorEmail: string | null;
  action: string;
  targetType: string | null;
  targetId: string | null;
  outcome: 'Success' | 'Failure';
}

/** Mirrors `Sonrisa.Contracts.Dtos.PagedResult<T>`. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

/** Mirrors `Sonrisa.Contracts.Dtos.Events.AdminEventDto`. `data` has the category's own shape. */
export interface AdminEvent {
  id: number;
  categoryKey: string;
  categoryName: string;
  sourceKey: string;
  sourceName: string;
  externalId: string;
  title: string;
  summary: string | null;
  url: string | null;
  publishedAt: string;
  firstSeenAt: string;
  updatedAt: string;
  isSimulated: boolean;
  data: unknown;
}

/** Mirrors `Sonrisa.Contracts.Dtos.Events.EventFiltersDto`. */
export interface EventFilters {
  categories: { key: string; name: string }[];
  sources: { key: string; name: string; categoryKey: string | null }[];
}

/** Mirrors `Sonrisa.Contracts.Dtos.Events.SimulateEventRequest`. */
export interface SimulateEventRequest {
  categoryKey: string;
  title: string;
  summary: string | null;
  url: string | null;
  data: Record<string, unknown>;
}

/** Admin API (spec §2.9): read-only lists, plus the "simulate event" form. */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  users(page: number, pageSize: number): Observable<PagedResult<AdminUser>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<AdminUser>>('/api/admin/users', { params });
  }

  user(id: string): Observable<AdminUserDetail> {
    return this.http.get<AdminUserDetail>(`/api/admin/users/${encodeURIComponent(id)}`);
  }

  audit(page: number, pageSize: number): Observable<PagedResult<AuditEntry>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<AuditEntry>>('/api/admin/audit', { params });
  }

  events(page: number, pageSize: number, category: string, source: string): Observable<PagedResult<AdminEvent>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (category) {
      params = params.set('category', category);
    }
    if (source) {
      params = params.set('source', source);
    }
    return this.http.get<PagedResult<AdminEvent>>('/api/admin/events', { params });
  }

  eventFilters(): Observable<EventFilters> {
    return this.http.get<EventFilters>('/api/admin/events/filters');
  }

  simulateEvent(request: SimulateEventRequest): Observable<AdminEvent> {
    return this.http.post<AdminEvent>('/api/admin/events/simulate', request);
  }
}
