using System.Diagnostics;
using Alerta.Api.Canais;
using Alerta.Api.Domain;
using Alerta.Api.Infrastructure;
using Alerta.Api.Observabilidade;

namespace Alerta.Api.Application;

// Processa um evento (desvio/atraso): persiste o alerta e aciona os canais
// decididos pela fábrica.
public sealed class ServicoAlerta(
    IAlertaRepositorio repositorio,
    FabricaAlerta fabrica,
    NotificadorEntrega notificador,
    ILogger<ServicoAlerta> logger)
{
    public async Task<Domain.Alerta> ProcessarDesvioAsync(
        Guid entregaId, string motivo, string severidade,
        string correlationId, CancellationToken ct = default) =>
        await ProcessarAsync(TipoEventoAlerta.DESVIO, entregaId,
            $"Desvio de rota: {motivo}", severidade, correlationId, ct);

    public async Task<Domain.Alerta> ProcessarAtrasoAsync(
        Guid entregaId, int atrasoEstimadoMinutos, string severidade,
        string correlationId, CancellationToken ct = default) =>
        await ProcessarAsync(TipoEventoAlerta.ATRASO, entregaId,
            $"Atraso estimado de {atrasoEstimadoMinutos} min", severidade,
            correlationId, ct);

    private async Task<Domain.Alerta> ProcessarAsync(
        TipoEventoAlerta tipo, Guid entregaId, string mensagem,
        string severidade, string correlationId, CancellationToken ct)
    {
        var canais = fabrica.Criar(tipo, severidade);
        Domain.Alerta alerta;
        var inicio = Stopwatch.GetTimestamp();
        try
        {
            alerta = new Domain.Alerta
            {
                EntregaId = entregaId,
                Tipo = tipo,
                Severidade = severidade.ToUpperInvariant(),
                Mensagem = mensagem,
                CanaisAcionados = canais.Select(c => c.Nome).ToList(),
                CorrelationId = correlationId
            };

            foreach (var canal in canais)
            {
                await canal.EnviarAsync(alerta, ct);
                MetricasAlerta.CanaisAcionados.Labels(canal.Nome).Inc();
            }

            repositorio.Adicionar(alerta);
        }
        finally
        {
            MetricasAlerta.DuracaoProcessamento.Observe(
                Stopwatch.GetElapsedTime(inicio).TotalSeconds);
        }
        MetricasAlerta.AlertasProcessados.Labels(tipo.ToString()).Inc();
        logger.LogInformation(
            "Alerta {Tipo}/{Severidade} entrega {EntregaId} via [{Canais}] correlacao {Correlacao}",
            tipo, alerta.Severidade, entregaId,
            string.Join(",", alerta.CanaisAcionados), correlationId);

        // Broadcast "AlertaGerado" via Hub da Entrega.Api (best-effort).
        await notificador.NotificarAlertaAsync(entregaId, tipo.ToString(),
            alerta.Severidade, mensagem, ct);

        return alerta;
    }
}
