using System.Text;
using Alerta.Api.Application;
using Alerta.Api.Canais;
using Alerta.Api.Consumidores;
using Alerta.Api.Infrastructure;

namespace Alerta.Api.Api;

public static class AlertaEndpoints
{
    public static IEndpointRouteBuilder MapAlertas(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/alertas");

        // GET /alertas — lista alertas (filtros do dashboard).
        grupo.MapGet("/", (string? tipo, string? severidade, IAlertaRepositorio repo) =>
            Results.Ok(repo.Listar(tipo, severidade)));

        grupo.MapGet("/{id:guid}", (Guid id, IAlertaRepositorio repo) =>
            repo.Obter(id) is { } alerta ? Results.Ok(alerta) : Results.NotFound());

        // Outboxes dos mocks (prova de quais canais dispararam).
        grupo.MapGet("/canais/emails", (OutboxEmail outbox) =>
            Results.Ok(outbox.Itens));
        grupo.MapGet("/canais/sms", (OutboxSms outbox) =>
            Results.Ok(outbox.Itens));

        // POST /alertas/simular-falha — publica poison message REAL no broker
        // (bytes inválidos com routing key válida). O consumer falha ao
        // desserializar, dá nack sem requeue e a mensagem cai na DLQ.
        grupo.MapPost("/simular-falha", (PublicadorCruRabbitMq publicador) =>
        {
            var lixo = Encoding.UTF8.GetBytes(
                "{isto-nao-e-um-evento-valido-");
            publicador.PublicarBytes("desvio.detectado", lixo);
            return Results.Accepted("/alertas/dlq",
                new { mensagem = "Poison message publicada; verifique /alertas/dlq." });
        });

        // GET /alertas/dlq — profundidade real da DLQ + amostra (peek com requeue).
        grupo.MapGet("/dlq", async (InspetorDlq inspetor, CancellationToken ct) =>
        {
            var (total, amostra) = await inspetor.InspecionarAsync(ct);
            return Results.Ok(new { fila = ConsumidorAlertasWorker.FilaDlq, total, amostra });
        });

        return app;
    }
}
