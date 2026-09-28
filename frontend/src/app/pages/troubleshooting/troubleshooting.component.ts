import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AnaliseResultado } from '../../core/api';
import { RoteamentoService } from '../../core/services';
import { SaudeService, SaudeServico } from '../../core/saude.service';

// Mini-painel de operações: saúde dos serviços, circuit breaker,
// DLQ e gatilho manual de análise — sem F5 (polling de 5s).
@Component({
  selector: 'app-troubleshooting',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <h2>Saúde do sistema <small class="auto">auto-refresh 5s</small></h2>

    <div class="cards">
      @for (s of saude(); track s.nome) {
        <div class="card" [class.off]="s.status !== 'Healthy'">
          <h3><i class="dot" [class.ok]="s.status === 'Healthy'"></i>{{ s.nome }}</h3>
          <p><strong>{{ s.status }}</strong>{{ s.erro ? ' — ' + s.erro : '' }}</p>
          <ul>
            @for (c of s.checks; track c.nome) {
              <li>{{ c.nome }}: <strong>{{ c.status }}</strong> <small>{{ c.descricao }}</small></li>
            }
          </ul>
        </div>
      }
    </div>

    <div class="linha">
      <div class="card">
        <h3>Circuit breaker (Rastreamento.Api)</h3>
        <p class="grande">{{ circuito() }}</p>
        <p><small>0/green = fechado · endpoint: GET /roteamento/circuit-breaker</small></p>
      </div>
      <div class="card">
        <h3>DLQ (alerta.eventos.dlq)</h3>
        <p class="grande">{{ dlqTotal() < 0 ? '?' : dlqTotal() }}</p>
        <p><small>via GET /alertas/dlq</small></p>
      </div>
      <div class="card">
        <h3>Forçar análise</h3>
        <input [(ngModel)]="entregaId" placeholder="entregaId" size="36" />
        <button (click)="forcarAnalise()" [disabled]="analisando()">Analisar agora</button>
        @if (ultimaAnalise()) {
          <p class="mono">fonte={{ ultimaAnalise()!.fonteHistorico }} tipo={{ ultimaAnalise()!.tipo }} ({{ ultimaAnalise()!.severidade }})</p>
        }
        @if (erroAnalise()) {
          <p class="erro">{{ erroAnalise() }}</p>
        }
      </div>
    </div>
  `,
  styles: [`
    .auto { font-size: 0.7em; color: #64748b; font-weight: normal; }
    .cards, .linha { display: flex; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .card { border: 1px solid #e2e8f0; border-radius: 8px; padding: 12px 16px; min-width: 260px; flex: 1; }
    .card.off { border-color: #ef4444; background: #fef2f2; }
    .dot { display: inline-block; width: 10px; height: 10px; border-radius: 50%; background: #ef4444; margin-right: 8px; }
    .dot.ok { background: #22c55e; }
    .grande { font-size: 2em; margin: 4px 0; }
    .mono { font-family: monospace; font-size: 0.85em; }
    .erro { color: #b91c1c; }
    ul { margin: 4px 0; padding-left: 18px; }
  `],
})
export class TroubleshootingComponent implements OnInit, OnDestroy {
  private saudeApi = inject(SaudeService);
  private roteamento = inject(RoteamentoService);

  readonly saude = signal<SaudeServico[]>([]);
  readonly circuito = signal('?');
  readonly dlqTotal = signal(-1);
  readonly ultimaAnalise = signal<AnaliseResultado | null>(null);
  readonly erroAnalise = signal('');
  readonly analisando = signal(false);
  entregaId = '';
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit(): void {
    this.atualizar();
    this.timer = setInterval(() => this.atualizar(), 5000);
  }

  ngOnDestroy(): void {
    if (this.timer) clearInterval(this.timer);
  }

  atualizar(): void {
    this.saudeApi.consultar().subscribe((s) => this.saude.set(s));
    this.saudeApi.circuito().subscribe((c) => this.circuito.set(c.estado));
    this.saudeApi.dlq().subscribe((d) => this.dlqTotal.set(d.total));
  }

  forcarAnalise(): void {
    if (!this.entregaId) return;
    this.analisando.set(true);
    this.erroAnalise.set('');
    this.roteamento.analisar(this.entregaId.trim()).subscribe({
      next: (r) => {
        this.ultimaAnalise.set(r);
        this.analisando.set(false);
        this.atualizar();
      },
      error: (e) => {
        this.erroAnalise.set(e.error?.erro ?? e.message);
        this.analisando.set(false);
      },
    });
  }
}
