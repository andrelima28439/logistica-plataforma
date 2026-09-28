using Entrega.Api.Application;
using Entrega.Api.Domain;
using Entrega.Api.Observabilidade;
using Microsoft.AspNetCore.SignalR;

namespace Entrega.Api.Api;

public sealed record CriarEntregaRequest(
    string? Origem,
    string? Destino,
    string? TransportadorId,
    TipoCarga? TipoCarga,
    DateTimeOffset? PrazoEstimado);

public sealed record TransicaoRequest(string? Status);

public sealed record NotificarAlertaRequest(
    string? Tipo,
    string? Severidade,
    string? Mensagem);

public static class EntregasEndpoints
{
    public static IEndpointRouteBuilder MapEntregas(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/entregas");

        // POST /entregas — cria entrega
        grupo.MapPost("/", async (
            CriarEntregaRequest req,
            IEntregaRepositorio repo,
            IHubContext<EntregasHub> hub) =>
        {
            if (string.IsNullOrWhiteSpace(req.Origem)
                || string.IsNullOrWhiteSpace(req.Destino)
                || string.IsNullOrWhiteSpace(req.TransportadorId)
                || req.TipoCarga is null
                || req.PrazoEstimado is null)
            {
                return Results.BadRequest(new
                {
                    erro = "origem, destino, transportadorId, tipoCarga e prazoEstimado são obrigatórios."
                });
            }

            var entrega = Domain.Entrega.Criar(
                req.Origem, req.Destino, req.TransportadorId,
                req.TipoCarga.Value, req.PrazoEstimado.Value);
            repo.Adicionar(entrega);

            MetricasEntrega.EntregasCriadas.Inc();

            var resumo = ParaResumo(entrega);
            await hub.Clients.All.SendAsync("EntregaCriada", resumo);
            return Results.Created($"/entregas/{entrega.Id}", resumo);
        });

        // GET /entregas — lista ativas com filtros
        grupo.MapGet("/", (string? status, string? transportadorId, IEntregaRepositorio repo) =>
        {
            StatusEntrega? filtro = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<StatusEntrega>(status, ignoreCase: true, out var parsed))
                    return Results.BadRequest(new { erro = $"Status desconhecido: {status}." });
                filtro = parsed;
            }

            return Results.Ok(repo.Listar(filtro, transportadorId).Select(ParaResumo));
        });

        // GET /entregas/{id} — detalhe com posições e eventos.
        // "posicoes" retorna lista vazia até o Rastreamento preencher —
        // contrato já estável para o frontend.
        grupo.MapGet("/{id:guid}", (Guid id, IEntregaRepositorio repo) =>
        {
            var entrega = repo.ObterPorId(id);
            if (entrega is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                entrega.Id,
                entrega.Origem,
                entrega.Destino,
                entrega.TransportadorId,
                entrega.TipoCarga,
                entrega.Status,
                entrega.DataCriacao,
                entrega.PrazoEstimado,
                posicoes = Array.Empty<object>(),
                eventos = entrega.Eventos.Select(E => new
                {
                    tipo = E.GetType().Name,
                    entregaId = E.EntregaId,
                    de = E is StatusAlterado s ? s.De.ToString() : null,
                    para = E is StatusAlterado s2 ? s2.Para.ToString() : null,
                    quando = E.Quando
                })
            });
        });

        // POST /entregas/{id}/transicao — chamado pelo Roteamento.Api
        // (best-effort) quando detecta desvio/atraso: aplica a regra de
        // negócio do domínio e avisa o dashboard via SignalR.
        grupo.MapPost("/{id:guid}/transicao", async (
            Guid id,
            TransicaoRequest req,
            IEntregaRepositorio repo,
            IHubContext<EntregasHub> hub) =>
        {
            var entrega = repo.ObterPorId(id);
            if (entrega is null)
                return Results.NotFound();

            if (!Enum.TryParse<StatusEntrega>(req.Status, ignoreCase: true, out var destino))
                return Results.BadRequest(new { erro = $"Status desconhecido: {req.Status}." });

            var de = entrega.Status;
            try
            {
                // Chegou telemetria de uma entrega ainda CRIADA: ela entrou em
                // trânsito (passagem automática, com broadcasts de cada transição).
                if (de == StatusEntrega.CRIADA
                    && (destino == StatusEntrega.DESVIO_DETECTADO
                        || destino == StatusEntrega.ATRASADA))
                {
                    entrega.TransitarPara(StatusEntrega.EM_TRANSITO);
                    await hub.Clients.All.SendAsync("StatusAlterado",
                        new { entregaId = id, de = de.ToString(), para = StatusEntrega.EM_TRANSITO.ToString() });
                    de = StatusEntrega.EM_TRANSITO;
                }

                entrega.TransitarPara(destino);
            }
            catch (TransicaoDeStatusInvalidaException ex)
            {
                return Results.BadRequest(new { erro = ex.Message });
            }

            await hub.Clients.All.SendAsync("StatusAlterado",
                new { entregaId = id, de = de.ToString(), para = destino.ToString() });
            return Results.Ok(ParaResumo(entrega));
        });

        // POST /entregas/{id}/notificacoes/alerta — chamado pelo Alerta.Api
        // (best-effort) após persistir um alerta: só faz broadcast.
        grupo.MapPost("/{id:guid}/notificacoes/alerta", async (
            Guid id,
            NotificarAlertaRequest req,
            IEntregaRepositorio repo,
            IHubContext<EntregasHub> hub) =>
        {
            if (repo.ObterPorId(id) is null)
                return Results.NotFound();

            await hub.Clients.All.SendAsync("AlertaGerado",
                new { entregaId = id, tipo = req.Tipo, severidade = req.Severidade, mensagem = req.Mensagem });
            return Results.Accepted();
        });

        return app;
    }

    private static object ParaResumo(Domain.Entrega e) => new
    {
        e.Id,
        e.Origem,
        e.Destino,
        e.TransportadorId,
        e.TipoCarga,
        e.Status,
        e.DataCriacao,
        e.PrazoEstimado
    };
}
