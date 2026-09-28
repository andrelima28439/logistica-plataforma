import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable, catchError, forkJoin, map, of } from 'rxjs';
import { API_URLS, HealthResponse } from './api';
import { AlertaService, RoteamentoService } from './services';

export interface SaudeServico {
  nome: string;
  url: string;
  status: 'Healthy' | 'Degraded' | 'Unhealthy' | 'Offline';
  checks: { nome: string; status: string; descricao: string }[];
  erro?: string;
}

// Agrega /health das 4 APIs + circuit breaker + DLQ para a tela de
// troubleshooting (polling de 5s no componente; sem F5).
@Injectable({ providedIn: 'root' })
export class SaudeService {
  private http = inject(HttpClient);
  private roteamento = inject(RoteamentoService);
  private alerta = inject(AlertaService);

  private servicos = [
    { nome: 'Entrega.Api', url: `${API_URLS.entrega}/health` },
    { nome: 'Rastreamento.Api', url: `${API_URLS.rastreamento}/health` },
    { nome: 'Roteamento.Api', url: `${API_URLS.roteamento}/health` },
    { nome: 'Alerta.Api', url: `${API_URLS.alerta}/health` },
  ];

  consultar(): Observable<SaudeServico[]> {
    return forkJoin(
      this.servicos.map((s) =>
        this.http.get<HealthResponse>(s.url).pipe(
          map((r) => ({
            nome: s.nome,
            url: s.url,
            status: r.status as SaudeServico['status'],
            checks: r.checks ?? [],
          })),
          catchError((e: HttpErrorResponse) =>
            of({
              nome: s.nome,
              url: s.url,
              status: 'Offline' as const,
              checks: [],
              erro: `HTTP ${e.status || 'inacessível'}`,
            })),
        ),
      ),
    );
  }

  circuito() {
    return this.roteamento.circuito().pipe(catchError(() => of({ estado: 'Offline', timestamp: '' })));
  }

  dlq() {
    return this.alerta.dlq().pipe(catchError(() => of({ fila: 'alerta.eventos.dlq', total: -1, amostra: [] })));
  }
}
