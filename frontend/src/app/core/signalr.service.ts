import { Injectable, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { API_URLS } from './api';

export interface StatusAlteradoEvt {
  entregaId: string;
  de: string;
  para: string;
}

export interface AlertaGeradoEvt {
  entregaId: string;
  tipo: string;
  severidade: string;
  mensagem: string;
}

// Hub SignalR da Entrega.Api (/entregas/stream).
// Eventos: EntregaCriada | StatusAlterado | AlertaGerado.
@Injectable({ providedIn: 'root' })
export class SignalrService {
  readonly entregaCriada$ = new Subject<unknown>();
  readonly statusAlterado$ = new Subject<StatusAlteradoEvt>();
  readonly alertaGerado$ = new Subject<AlertaGeradoEvt>();
  readonly estado = signal('desconectado');

  private conexao?: signalR.HubConnection;
  private iniciado = false;

  iniciar(): void {
    if (this.iniciado) return;
    this.iniciado = true;

    this.conexao = new signalR.HubConnectionBuilder()
      .withUrl(API_URLS.hubEntregas)
      .withAutomaticReconnect()
      .build();

    this.conexao.on('EntregaCriada', (e) => this.entregaCriada$.next(e));
    this.conexao.on('StatusAlterado', (e) => this.statusAlterado$.next(e as StatusAlteradoEvt));
    this.conexao.on('AlertaGerado', (e) => this.alertaGerado$.next(e as AlertaGeradoEvt));
    this.conexao.onreconnecting(() => this.estado.set('reconectando'));
    this.conexao.onreconnected(() => this.estado.set('conectado'));
    this.conexao.onclose(() => this.estado.set('desconectado'));

    this.estado.set('conectando');
    this.conexao.start()
      .then(() => this.estado.set('conectado'))
      .catch(() => this.estado.set('falha (API fora do ar?)'));
  }
}
