import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from '../admin/admin.service';

/** Mirrors `Sonrisa.Contracts.Dtos.Destinations.DestinationDto`. `config` is the channel's masked config. */
export interface Destination {
  id: number;
  channelKey: string;
  channelName: string;
  displayName: string;
  config: unknown;
  createdAt: string;
}

/** Mirrors `Sonrisa.Contracts.Dtos.Alerts.AlertDto`. `rule` has the category's own shape. */
export interface Alert {
  id: number;
  name: string;
  categoryKey: string;
  categoryName: string;
  rule: unknown;
  enabled: boolean;
  activeSince: string;
  createdAt: string;
  destinationIds: number[];
}

/** Mirrors `Sonrisa.Contracts.Dtos.Alerts.RecentMatchesDto`. */
export interface RecentMatches {
  supported: boolean;
  reason: string | null;
  items: {
    eventId: number;
    title: string;
    url: string | null;
    sourceName: string;
    publishedAt: string;
    firstSeenAt: string;
    isSimulated: boolean;
    data: unknown;
  }[];
}

/** Mirrors `Sonrisa.Contracts.Dtos.Notifications.NotificationDto`. */
export interface Notification {
  id: number;
  createdAt: string;
  alertId: number;
  alertName: string;
  alertDeleted: boolean;
  eventTitle: string;
  eventUrl: string | null;
  eventIsSimulated: boolean;
  channelKey: string;
  channelName: string;
  destinationName: string;
  destinationDeleted: boolean;
  status: 'Pending' | 'Sent' | 'Failed';
  failureReason: string | null;
  attempts: number;
  sentAt: string | null;
}

/** Mirrors `Sonrisa.Contracts.Dtos.Catalog.CatalogDto`. */
export interface Catalog {
  categories: { key: string; name: string; supportsBackfill: boolean }[];
  channels: { key: string; name: string; defaultConfig: unknown }[];
}

export interface AlertRequest {
  name: string;
  categoryKey?: string;
  rule: Record<string, unknown>;
  destinationIds: number[];
}

/** The caller's own data (spec §2.6, §2.2, §2.12). Another user's ID answers 404. */
@Injectable({ providedIn: 'root' })
export class UserApi {
  private readonly http = inject(HttpClient);

  catalog(): Observable<Catalog> {
    return this.http.get<Catalog>('/api/catalog');
  }

  destinations(page = 1, pageSize = 100): Observable<PagedResult<Destination>> {
    return this.http.get<PagedResult<Destination>>('/api/destinations', { params: paging(page, pageSize) });
  }

  createDestination(channelKey: string, displayName: string, config: Record<string, unknown>): Observable<Destination> {
    return this.http.post<Destination>('/api/destinations', { channelKey, displayName, config });
  }

  /** `config` null keeps the stored config (the client only has the masked one). */
  updateDestination(id: number, displayName: string, config: Record<string, unknown> | null): Observable<Destination> {
    return this.http.put<Destination>(`/api/destinations/${id}`, { displayName, config });
  }

  deleteDestination(id: number): Observable<void> {
    return this.http.delete<void>(`/api/destinations/${id}`);
  }

  alerts(page = 1, pageSize = 100): Observable<PagedResult<Alert>> {
    return this.http.get<PagedResult<Alert>>('/api/alerts', { params: paging(page, pageSize) });
  }

  alert(id: number): Observable<Alert> {
    return this.http.get<Alert>(`/api/alerts/${id}`);
  }

  createAlert(request: AlertRequest): Observable<Alert> {
    return this.http.post<Alert>('/api/alerts', request);
  }

  updateAlert(id: number, request: AlertRequest): Observable<Alert> {
    return this.http.put<Alert>(`/api/alerts/${id}`, request);
  }

  setAlertEnabled(id: number, enabled: boolean): Observable<Alert> {
    return this.http.post<Alert>(`/api/alerts/${id}/${enabled ? 'enable' : 'disable'}`, null);
  }

  deleteAlert(id: number): Observable<void> {
    return this.http.delete<void>(`/api/alerts/${id}`);
  }

  recentMatches(id: number): Observable<RecentMatches> {
    return this.http.get<RecentMatches>(`/api/alerts/${id}/recent-matches`);
  }

  notifications(page: number, pageSize: number): Observable<PagedResult<Notification>> {
    return this.http.get<PagedResult<Notification>>('/api/notifications', { params: paging(page, pageSize) });
  }
}

function paging(page: number, pageSize: number): HttpParams {
  return new HttpParams().set('page', page).set('pageSize', pageSize);
}
