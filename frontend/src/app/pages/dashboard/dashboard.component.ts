import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription, forkJoin, of, catchError } from 'rxjs';
import { CORES_STATUS, EntregaResumo, Posicao } from '../../core/api';
import { EntregaService, RastreamentoService } from '../../core/services';
import { SignalrService } from '../../core/signalr.service';

interface EntregaNoMapa extends EntregaResumo {
  pos?: Posicao;
  x?: number;
  y?: number;
}

// Dashboard operacional (tela principal): mapa simples com as
// entregas ativas coloridas por status + atualização via SignalR.
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule],
  template: `
    <h2>Dashboard operacional <small class="hub">SignalR: {{ signalr.estado() }}</small></h2>

    @if (toasts().length > 0) {
      <div class="toasts">
        @for (t of toasts(); track $index) {
          <div class="toast">{{ t }}</div>
        }
      </div>
    }

    @if (loading()) {
      <p>Carregando entregas…</p>
    } @else if (erro()) {
      <p class="erro">Falha ao carregar: {{ erro() }} (Entrega.Api fora do ar?)</p>
      <button (click)="carregar()">Tentar de novo</button>
    } @else {
      <svg viewBox="0 0 800 400" class="mapa" role="img" aria-label="Mapa de entregas">
        <rect x="0" y="0" width="800" height="400" fill="#0f172a" rx="8" />
        <text x="16" y="28" fill="#94a3b8" font-size="14">Região SP — última posição conhecida</text>
        @for (e of entregas(); track e.id) {
          @if (e.x !== undefined) {
            <circle [attr.cx]="e.x" [attr.cy]="e.y" r="9" [attr.fill]="cor(e.status)" stroke="#fff" stroke-width="2">
              <title>{{ e.id }} — {{ e.status }}</title>
            </circle>
          }
        }
      </svg>

      <div class="legenda">
        @for (s of statuses(); track s) {
          <span class="item"><i [style.background]="cor(s)"></i>{{ s }} ({{ contar(s) }})</span>
        }
      </div>

      <table>
        <thead><tr><th>ID</th><th>Origem → Destino</th><th>Carga</th><th>Status</th><th>Última posição</th></tr></thead>
        <tbody>
          @for (e of entregas(); track e.id) {
            <tr>
              <td class="mono">{{ e.id.slice(0, 8) }}…</td>
              <td>{{ e.origem }} → {{ e.destino }}</td>
              <td>{{ e.tipoCarga }}</td>
              <td><span class="badge" [style.background]="cor(e.status)">{{ e.status }}</span></td>
              <td class="mono">{{ e.pos ? (e.pos.latitude.toFixed(4) + ', ' + e.pos.longitude.toFixed(4)) : '—' }}</td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
  styles: [`
    .hub { font-size: 0.7em; color: #64748b; font-weight: normal; }
    .mapa { width: 100%; max-height: 380px; }
    .legenda { display: flex; gap: 16px; margin: 8px 0; flex-wrap: wrap; }
    .legenda .item i { display: inline-block; width: 12px; height: 12px; border-radius: 50%; margin-right: 6px; }
    table { width: 100%; border-collapse: collapse; margin-top: 8px; }
    th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; }
    .mono { font-family: monospace; font-size: 0.85em; }
    .badge { color: #fff; padding: 2px 8px; border-radius: 999px; font-size: 0.8em; }
    .erro { color: #b91c1c; }
    .toasts { position: fixed; top: 12px; right: 12px; display: flex; flex-direction: column; gap: 8px; z-index: 50; }
    .toast { background: #0f172a; color: #fff; padding: 10px 14px; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,.3); }
  `],
})
export class DashboardComponent implements OnInit, OnDestroy {
  private entregasApi = inject(EntregaService);
  private rastreamento = inject(RastreamentoService);
  readonly signalr = inject(SignalrService);

  readonly entregas = signal<EntregaNoMapa[]>([]);
  readonly toasts = signal<string[]>([]);
  readonly loading = signal(true);
  readonly erro = signal('');
  private subs: Subscription[] = [];

  ngOnInit(): void {
    this.signalr.iniciar();
    this.carregar();
    this.subs.push(
      this.signalr.entregaCriada$.subscribe(() => {
        this.avisar('Nova entrega criada');
        this.carregar();
      }),
      this.signalr.statusAlterado$.subscribe((e) => {
        this.avisar(`Status: ${e.entregaId.slice(0, 8)}… ${e.de} → ${e.para}`);
        this.carregar();
      }),
      this.signalr.alertaGerado$.subscribe((e) => {
        this.avisar(`Alerta ${e.tipo}/${e.severidade}: ${e.entregaId.slice(0, 8)}…`);
      }),
    );
  }

  ngOnDestroy(): void {
    this.subs.forEach((s) => s.unsubscribe());
  }

  cor(status: string): string {
    return CORES_STATUS[status] ?? '#64748b';
  }

  statuses(): string[] {
    return [...new Set(this.entregas().map((e) => e.status))];
  }

  contar(status: string): number {
    return this.entregas().filter((e) => e.status === status).length;
  }

  carregar(): void {
    this.loading.set(true);
    this.erro.set('');
    this.entregasApi.listar().subscribe({
      next: (lista) => {
        if (lista.length === 0) {
          this.entregas.set([]);
          this.loading.set(false);
          return;
        }
        forkJoin(
          lista.map((e) =>
            this.rastreamento.ultimaPosicao(e.id).pipe(
              catchError(() => of(null)),
            ),
          ),
        ).subscribe((posicoes) => {
          this.entregas.set(
            lista.map((e, i) => {
              const p = posicoes[i];
              const item: EntregaNoMapa = { ...e, pos: p?.posicao };
              if (p) {
                const [x, y] = this.projetar(p.posicao.latitude, p.posicao.longitude);
                item.x = x;
                item.y = y;
              }
              return item;
            }),
          );
          this.loading.set(false);
        });
      },
      error: (e) => {
        this.erro.set(e.message ?? 'erro desconhecido');
        this.loading.set(false);
      },
    });
  }

  private avisar(msg: string): void {
    this.toasts.update((t) => [...t.slice(-2), msg]);
    setTimeout(() => this.toasts.update((t) => t.slice(1)), 6000);
  }

  // Projeção simples SP: lon [-46.75,-46.45] → x, lat [-23.62,-23.48] → y.
  private projetar(lat: number, lon: number): [number, number] {
    const x = 20 + ((lon + 46.75) / 0.3) * 760;
    const y = 380 - ((lat + 23.62) / 0.14) * 360;
    return [Math.min(790, Math.max(10, x)), Math.min(390, Math.max(10, y))];
  }
}
