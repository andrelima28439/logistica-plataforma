import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { EntregaService, RastreamentoService, RoteamentoService } from '../../core/services';

// Simulador de cenários: cria entrega, liga o GPS mockado,
// simula desvio (rota esperada longe) e gap de sinal.
@Component({
  selector: 'app-simulador',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <h2>Simulador de cenários</h2>
    <p>Use esta tela para o teste de aceite: simule um problema e veja o alerta no dashboard em tempo real.</p>

    <div class="grid">
      <div class="card">
        <h3>1. Criar entrega</h3>
        <label>Origem <input [(ngModel)]="origem" /></label>
        <label>Destino <input [(ngModel)]="destino" /></label>
        <label>Transportador <input [(ngModel)]="transportador" /></label>
        <label>Carga
          <select [(ngModel)]="carga">
            <option>PADRAO</option><option>REFRIGERADA</option><option>FRAGIL</option>
          </select>
        </label>
        <button (click)="criar()" [disabled]="ocupado()">Criar</button>
      </div>

      <div class="card">
        <h3>2. GPS mockado</h3>
        <label>Entrega ID <input [(ngModel)]="entregaId" size="36" /></label>
        <button (click)="gps()" [disabled]="ocupado()">Iniciar GPS (2s)</button>
        <button (click)="gap()" [disabled]="ocupado()">Simular gap 8s</button>
        <button (click)="parar()" [disabled]="ocupado()">Parar GPS</button>
      </div>

      <div class="card">
        <h3>3. Simular desvio de rota</h3>
        <p><small>Cadastra contexto com rota esperada no RJ (GPS está em SP) → o Roteamento detecta DESVIO em segundos.</small></p>
        <button (click)="desvio()" [disabled]="ocupado()">Simular desvio</button>
      </div>
    </div>

    @if (msg()) { <p class="msg">{{ msg() }}</p> }
    @if (erro()) { <p class="erro">{{ erro() }}</p> }
  `,
  styles: [`
    .grid { display: flex; gap: 12px; flex-wrap: wrap; }
    .card { border: 1px solid #e2e8f0; border-radius: 8px; padding: 12px 16px; min-width: 280px; flex: 1; display: flex; flex-direction: column; gap: 8px; }
    label { display: flex; flex-direction: column; font-size: 0.9em; }
    .msg { color: #15803d; } .erro { color: #b91c1c; }
  `],
})
export class SimuladorComponent {
  private entregas = inject(EntregaService);
  private rastreamento = inject(RastreamentoService);
  private roteamento = inject(RoteamentoService);

  readonly msg = signal('');
  readonly erro = signal('');
  readonly ocupado = signal(false);

  origem = 'São Paulo/SP';
  destino = 'Santos/SP';
  transportador = 'transp-demo';
  carga = 'REFRIGERADA';
  entregaId = '';

  private executar(obs: Observable<unknown>, okMsg: (v: unknown) => string): void {
    this.ocupado.set(true);
    this.erro.set('');
    this.msg.set('');
    obs.subscribe({
      next: (v) => {
        this.msg.set(okMsg(v));
        this.ocupado.set(false);
      },
      error: (e) => {
        this.erro.set(e.error?.erro ?? e.message ?? 'erro');
        this.ocupado.set(false);
      },
    });
  }

  criar(): void {
    this.executar(
      this.entregas.criar({
        origem: this.origem,
        destino: this.destino,
        transportadorId: this.transportador,
        tipoCarga: this.carga,
        prazoEstimado: new Date(Date.now() + 2 * 864e5).toISOString(),
      }),
      (v: unknown) => {
        const e = v as { id: string };
        this.entregaId = e.id;
        return `Entrega criada: ${e.id}`;
      },
    );
  }

  gps(): void {
    this.executar(this.rastreamento.iniciarSimulacao(this.entregaId.trim()), () => 'GPS mockado ligado (posição a cada 2s).');
  }

  gap(): void {
    this.executar(this.rastreamento.aplicarGap(this.entregaId.trim(), 8), () => 'Gap de sinal de 8s aplicado.');
  }

  parar(): void {
    this.executar(this.rastreamento.pararSimulacao(this.entregaId.trim()), () => 'GPS parado.');
  }

  desvio(): void {
    this.executar(
      this.roteamento.cadastrarContexto(this.entregaId.trim(), this.carga, [{ latitude: -22.9068, longitude: -43.1729 }]),
      () => 'Contexto com rota no RJ cadastrado — aguarde o DESVIO no dashboard (via SignalR, sem F5).',
    );
  }
}
