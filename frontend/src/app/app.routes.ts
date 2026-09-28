import { Routes } from '@angular/router';
import { DashboardComponent } from './pages/dashboard/dashboard.component';
import { AlertasComponent } from './pages/alertas/alertas.component';
import { TroubleshootingComponent } from './pages/troubleshooting/troubleshooting.component';
import { SimuladorComponent } from './pages/simulador/simulador.component';

export const routes: Routes = [
  { path: '', component: DashboardComponent },
  { path: 'alertas', component: AlertasComponent },
  { path: 'saude', component: TroubleshootingComponent },
  { path: 'simulador', component: SimuladorComponent },
  { path: '**', redirectTo: '' },
];
