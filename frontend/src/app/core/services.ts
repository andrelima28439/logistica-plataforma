import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_URLS, AlertaItem, AnaliseResultado, CircuitoEstado, DlqInfo, EntregaResumo, HealthResponse, Posicao } from './api';

@Injectable({ providedIn: 'root' })
export class EntregaService {
  private http = inject(HttpClient);
  private base = API_URLS.entrega;

  listar(status?: string, transportadorId?: string): Observable<EntregaResumo[]> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    if (transportadorId) params = params.set('transportadorId', transportadorId);
    return this.http.get<EntregaResumo[]>(`${this.base}/entregas`, { params });
  }

  criar(dados: { origem: string; destino: string; transportadorId: string; tipoCarga: string; prazoEstimado: string }): Observable<EntregaResumo> {
    return this.http.post<EntregaResumo>(`${this.base}/entregas`, dados);
  }
}

@Injectable({ providedIn: 'root' })
export class RastreamentoService {
  private http = inject(HttpClient);
  private base = API_URLS.rastreamento;

  historico(entregaId: string): Observable<{ entregaId: string; total: number; posicoes: Posicao[] }> {
    return this.http.get<{ entregaId: string; total: number; posicoes: Posicao[] }>(`${this.base}/rastreamento/${entregaId}/posicoes`);
  }

  ultimaPosicao(entregaId: string): Observable<{ fonte: string; posicao: Posicao }> {
    return this.http.get<{ fonte: string; posicao: Posicao }>(`${this.base}/rastreamento/${entregaId}/ultima-posicao`);
  }

  iniciarSimulacao(entregaId: string, intervaloSegundos = 2): Observable<unknown> {
    return this.http.post(`${this.base}/rastreamento/${entregaId}/simulacao/iniciar`, { intervaloSegundos });
  }

  aplicarGap(entregaId: string, duracaoSegundos = 8): Observable<unknown> {
    return this.http.post(`${this.base}/rastreamento/${entregaId}/simulacao/gap`, { duracaoSegundos });
  }

  pararSimulacao(entregaId: string): Observable<unknown> {
    return this.http.post(`${this.base}/rastreamento/${entregaId}/simulacao/parar`, {});
  }
}

@Injectable({ providedIn: 'root' })
export class RoteamentoService {
  private http = inject(HttpClient);
  private base = API_URLS.roteamento;

  cadastrarContexto(entregaId: string, tipoCarga: string, rotaEsperada: { latitude: number; longitude: number }[]): Observable<unknown> {
    return this.http.post(`${this.base}/roteamento/entregas`, { entregaId, tipoCarga, rotaEsperada });
  }

  analisar(entregaId: string): Observable<AnaliseResultado> {
    return this.http.post<AnaliseResultado>(`${this.base}/roteamento/entregas/${entregaId}/analisar`, {});
  }

  circuito(): Observable<CircuitoEstado> {
    return this.http.get<CircuitoEstado>(`${this.base}/roteamento/circuit-breaker`);
  }

  saude(): Observable<HealthResponse> {
    return this.http.get<HealthResponse>(`${this.base}/health`);
  }
}

@Injectable({ providedIn: 'root' })
export class AlertaService {
  private http = inject(HttpClient);
  private base = API_URLS.alerta;

  listar(tipo?: string, severidade?: string): Observable<AlertaItem[]> {
    let params = new HttpParams();
    if (tipo) params = params.set('tipo', tipo);
    if (severidade) params = params.set('severidade', severidade);
    return this.http.get<AlertaItem[]>(`${this.base}/alertas`, { params });
  }

  dlq(): Observable<DlqInfo> {
    return this.http.get<DlqInfo>(`${this.base}/alertas/dlq`);
  }

  saude(): Observable<HealthResponse> {
    return this.http.get<HealthResponse>(`${this.base}/health`);
  }
}
