using Alerta.Api.Infrastructure;
using Alerta.Api.Observabilidade;

namespace Alerta.Api.Observabilidade;

// Poller da DLQ (15s): mantém o gauge alerta_dlq_mensagens atualizado
// para o Prometheus/Grafana. Resiliente: sem management API, só loga.
public sealed class DlqMetricasWorker(
    InspetorDlq inspetor,
    ILogger<DlqMetricasWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (total, _) = await inspetor.InspecionarAsync(stoppingToken);
                MetricasAlerta.DlqMensagens.Set(total);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Management API indisponível para o gauge da DLQ.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
