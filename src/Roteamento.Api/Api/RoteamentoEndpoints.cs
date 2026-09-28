using Roteamento.Api.Application;
using Roteamento.Api.Domain;
using Roteamento.Api.Resiliencia;
using Shared.Kernel.Observabilidade;

namespace Roteamento.Api.Api;

public sealed record PontoRequest(double Latitude, double Longitude);
public sealed record CadastrarContextoRequest(
    Guid EntregaId,
    string TipoCarga,
    List<PontoRequest>? RotaEsperada);

public static class RoteamentoEndpoints
{
    public static IEndpointRouteBuilder MapRoteamento(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/roteamento");

        // Cadastro do contexto operacional da entrega (tipo de carga + rota).
        grupo.MapPost("/entregas", (
            CadastrarContextoRequest req,
            IContextoEntregaRepositorio repo) =>
        {
            if (req.EntregaId == Guid.Empty
                || !Enum.TryParse<TipoCarga>(req.TipoCarga, ignoreCase: true, out var tipo))
                return Results.BadRequest(new
                {
                    erro = "entregaId válido e tipoCarga (PADRAO/REFRIGERADA/FRAGIL) são obrigatórios."
                });

            repo.Salvar(new ContextoEntrega(req.EntregaId, tipo,
                req.RotaEsperada?.Select(p => new PontoRota(p.Latitude, p.Longitude)).ToList()
                ?? new List<PontoRota>()));
            return Results.Created($"/roteamento/entregas/{req.EntregaId}",
                new { entregaId = req.EntregaId, tipoCarga = tipo.ToString() });
        });

        // Gatilho manual de análise (usado na demo do circuit breaker).
        grupo.MapPost("/entregas/{id:guid}/analisar", async (
            Guid id,
            HttpContext ctx,
            ServicoRoteamento servico,
            CancellationToken ct) =>
        {
            var correlacao = ctx.CorrelationIdAtual();

            var analise = await servico.AnalisarAsync(id, correlacao, ct);
            if (analise is null)
                return Results.NotFound(new { erro = "Entrega sem contexto cadastrado." });

            return Results.Ok(new
            {
                entregaId = id,
                fonteHistorico = analise.FonteHistorico,
                tipo = analise.Resultado.Tipo.ToString(),
                severidade = analise.Resultado.Severidade,
                motivo = analise.Resultado.Motivo
            });
        });

        // Estado do circuit breaker (exibido no dashboard).
        grupo.MapGet("/circuit-breaker", (CircuitoHistorico circuito) =>
            Results.Ok(new
            {
                estado = circuito.Estado,
                timestamp = DateTimeOffset.UtcNow
            }));

        // Eventos emitidos (desvio/atraso).
        grupo.MapGet("/eventos", (ServicoRoteamento servico) =>
            Results.Ok(servico.Emitidos));

        return app;
    }
}
