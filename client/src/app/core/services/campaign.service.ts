import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class CampaignService {
  constructor(private http: HttpClient) {}

  createCampaign(name: string): Observable<{ id: string }> {
    return this.http.post<{ id: string }>('/api/campaigns', { name });
  }

  startSession(campaignId: string): Observable<{ joinCode: string; gameSessionId: string }> {
    return this.http.post<{ joinCode: string; gameSessionId: string }>(
      `/api/campaigns/${campaignId}/sessions`,
      {}
    );
  }
}
