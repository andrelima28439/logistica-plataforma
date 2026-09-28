using Rastreamento.Api.Application;
using Rastreamento.Api.Simulacao;
using Shared.Kernel.Observabilidade;

namespace Rastreamento.Api.Api;

public sealed record EnviarPosicaoRequest(double? Latitude, double? Longitude);
public sealed record IniciarSimulacaoRequest(
    double? IntervaloSegundos,
    int? GapAposPosicoes,
    double? GapDuracaoSegundos);
public sealed record AplicarGapRequest(double? DuracaoSegundos);

public static class RastreamentoEndpoints
{

    public static IEndpointRouteBuilder MapRastreamento(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/rastreamento");

        // POST /rastreamento/{entregaId}/posicao — dispositivo GPS real/manual.
        grupo.MapPost("/{entregaId:guid}/posicao", async (
            Guid entregaId,
            EnviarPosicaoRequest req,
            HttpContext ctx,
            ServicoRastreamento servico) =>
        {
            if (req.Latitude is null || req.Longitude is null
                || req.Latitude is < -90 or > 90
                || req.Longitude is < -180 or > 180)
            {
                return Results.BadRequest(new
                {
                    erro = "latitude (-90..90) e longitude (-180..180) são obrigatórias."
                });
            }

            var correlacao = ctx.CorrelationIdAtual();

            var posicao = await servico.RegistrarPosicaoAsync(
                entregaId, req.Latitude.Value, req.Longitude.Value, correlacao);
            return Results.Created($"/rastreamento/{entregaId}/posicoes", posicao);
        });

        // GET /rastreamento/{entregaId}/posicoes — histórico completo.
        grupo.MapGet("/{entregaId:guid}/posicoes", (Guid entregaId, ServicoRastreamento servico) =>
        {
            var historico = servico.Historico(entregaId);
            return Results.Ok(new
            {
                entregaId,
                total = historico.Count,
                posicoes = historico
            });
        });

        // GET /rastreamento/{entregaId}/ultima-posicao — lê DO CACHE Redis.
        grupo.MapGet("/{entregaId:guid}/ultima-posicao", async (
            Guid entregaId, ServicoRastreamento servico, CancellationToken ct) =>
        {
            var ultima = await servico.UltimaConhecidaAsync(entregaId, ct);
            return ultima is null
                ? Results.NotFound(new { erro = "Nenhuma posição conhecida (cache vazio)." })
                : Results.Ok(new { fonte = "cache", posicao = ultima });
        });

        // POST .../simulacao/iniciar — liga o GPS mockado da entrega.
        grupo.MapPost("/{entregaId:guid}/simulacao/iniciar", (
            Guid entregaId,
            IniciarSimulacaoRequest req,
            SimuladorGpsWorker worker) =>
        {
            var intervalo = TimeSpan.FromSeconds(
                req.IntervaloSegundos is > 0 ? req.IntervaloSegundos.Value : 5);
            TimeSpan? gapDuracao = req.GapDuracaoSegundos is > 0
                ? TimeSpan.FromSeconds(req.GapDuracaoSegundos.Value)
                : null;

            return Results.Ok(worker.Iniciar(entregaId, intervalo,
                req.GapAposPosicoes, gapDuracao));
        });

        // POST .../simulacao/gap — simula perda de sinal IMEDIATA.
        grupo.MapPost("/{entregaId:guid}/simulacao/gap", (
            Guid entregaId,
            AplicarGapRequest req,
            SimuladorGpsWorker worker) =>
        {
            if (req.DuracaoSegundos is not > 0)
                return Results.BadRequest(new { erro = "duracaoSegundos deve ser > 0." });

            var status = worker.AplicarGap(entregaId,
                TimeSpan.FromSeconds(req.DuracaoSegundos.Value));
            return status is null
                ? Results.NotFound(new { erro = "Simulação não iniciada para esta entrega." })
                : Results.Ok(status);
        });

        // POST .../simulacao/parar — desliga o GPS mockado.
        grupo.MapPost("/{entregaId:guid}/simulacao/parar", (
            Guid entregaId, SimuladorGpsWorker worker) =>
        {
            var existia = worker.Parar(entregaId);
            return Results.Ok(new { ativa = false, simulacaoExistia = existia });
        });

        // GET .../simulacao/status — estado da simulação.
        grupo.MapGet("/{entregaId:guid}/simulacao/status", (
            Guid entregaId, SimuladorGpsWorker worker) =>
            Results.Ok(worker.Status(entregaId)));

        return app;
    }
}
