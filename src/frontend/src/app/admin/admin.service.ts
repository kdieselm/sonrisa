import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

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

/** Read-only admin API (spec §2.9). */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  users(page: number, pageSize: number): Observable<PagedResult<AdminUser>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<AdminUser>>('/api/admin/users', { params });
  }

  audit(page: number, pageSize: number): Observable<PagedResult<AuditEntry>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<AuditEntry>>('/api/admin/audit', { params });
  }
}
