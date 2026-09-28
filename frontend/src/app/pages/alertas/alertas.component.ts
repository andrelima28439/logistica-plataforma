import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AlertaItem } from '../../core/api';
import { AlertaService } from '../../core/services';

// Lista de alertas com filtro por severidade e tipo.
@Component({
  selector: 'app-alertas',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <h2>Alertas</h2>
    <div class="filtros">
      <label>Tipo:
        <select [(ngModel)]="tipo" (change)="carregar()">
          <option value="">todos</option>
          <option value="DESVIO">DESVIO</option>
          <option value="ATRASO">ATRASO</option>
        </select>
      </label>
      <label>Severidade:
        <select [(ngModel)]="severidade" (change)="carregar()">
          <option value="">todas</option>
          <option value="CRITICA">CRITICA</option>
          <option value="ALTA">ALTA</option>
          <option value="MEDIA">MEDIA</option>
          <option value="BAIXA">BAIXA</option>
        </select>
      </label>
      <button (click)="carregar()">Atualizar</button>
    </div>

    @if (loading()) {
      <p>Carregando alertas…</p>
    } @else if (erro()) {
      <p class="erro">Falha ao carregar: {{ erro() }} (Alerta.Api fora do ar?)</p>
      <button (click)="carregar()">Tentar de novo</button>
    } @else if (alertas().length === 0) {
      <p>Nenhum alerta com esses filtros.</p>
    } @else {
      <table>
        <thead><tr><th>Quando</th><th>Tipo</th><th>Severidade</th><th>Entrega</th><th>Mensagem</th><th>Canais</th></tr></thead>
        <tbody>
          @for (a of alertas(); track a.id) {
            <tr>
              <td class="mono">{{ a.criadoEm | date:'HH:mm:ss' }}</td>
              <td>{{ a.tipo }}</td>
              <td><span class="badge sev-{{ a.severidade }}">{{ a.severidade }}</span></td>
              <td class="mono">{{ a.entregaId.slice(0, 8) }}…</td>
              <td>{{ a.mensagem }}</td>
              <td class="mono">{{ a.canaisAcionados.join(',') }}</td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
  styles: [`
    .filtros { display: flex; gap: 12px; align-items: end; margin-bottom: 12px; }
    table { width: 100%; border-collapse: collapse; }
    th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; }
    .mono { font-family: monospace; font-size: 0.85em; }
    .badge { color: #fff; padding: 2px 8px; border-radius: 999px; font-size: 0.8em; }
    .sev-CRITICA { background: #dc2626; } .sev-ALTA { background: #ea580c; }
    .sev-MEDIA { background: #ca8a04; } .sev-BAIXA { background: #16a34a; }
    .erro { color: #b91c1c; }
  `],
})
export class AlertasComponent implements OnInit {
  private api = inject(AlertaService);
  readonly alertas = signal<AlertaItem[]>([]);
  readonly loading = signal(true);
  readonly erro = signal('');
  tipo = '';
  severidade = '';

  ngOnInit(): void {
    this.carregar();
  }

  carregar(): void {
    this.loading.set(true);
    this.erro.set('');
    this.api.listar(this.tipo || undefined, this.severidade || undefined).subscribe({
      next: (lista) => {
        this.alertas.set(lista);
        this.loading.set(false);
      },
      error: (e) => {
        this.erro.set(e.message ?? 'erro desconhecido');
        this.loading.set(false);
      },
    });
  }
}
