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

/** Mirrors `Sonrisa.Contracts.Dtos.Admin.AdminDeliveryDto`. */
export interface AdminDelivery {
  id: number;
  createdAt: string;
  ownerUserId: string;
  ownerEmail: string | null;
  alertId: number;
  alertName: string;
  eventId: number;
  eventTitle: string;
  eventIsSimulated: boolean;
  channelKey: string;
  channelName: string;
  destinationName: string;
  status: 'Pending' | 'Sent' | 'Failed';
  attempts: number;
  nextAttemptAt: string | null;
  sentAt: string | null;
  failure: string | null;
  lastError: string | null;
}

/** Mirrors `Sonrisa.Contracts.Dtos.Admin.SlackMockMessageDto`. */
export interface SlackMockMessage {
  id: number;
  hookName: string;
  body: string;
  receivedAt: string;
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

  deliveries(page: number, pageSize: number, status: string): Observable<PagedResult<AdminDelivery>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (status) {
      params = params.set('status', status);
    }
    return this.http.get<PagedResult<AdminDelivery>>('/api/admin/deliveries', { params });
  }

  slackMock(page: number, pageSize: number): Observable<PagedResult<SlackMockMessage>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<SlackMockMessage>>('/api/admin/slack-mock', { params });
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
