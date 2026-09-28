using System.Diagnostics;
using Roteamento.Api.Domain;
using Roteamento.Api.Estrategias;
using Roteamento.Api.Infrastructure;
using Roteamento.Api.Observabilidade;
using Shared.Kernel;

namespace Roteamento.Api.Application;

public sealed record EventoEmitido(
    string RoutingKey,
    Guid EntregaId,
    TipoResultado Tipo,
    string Severidade,
    string Motivo,
    DateTimeOffset Quando);

// Uma análise = UMA busca de histórico (contagem exata de falhas no breaker).
public sealed record AnaliseConcluida(ResultadoAnalise Resultado, string FonteHistorico);

// Orquestra a análise: busca histórico (resiliente),
// resolve a estratégia pelo tipo de carga, analisa e publica o evento
// quando há desvio ou atraso.
public sealed class ServicoRoteamento(
    IContextoEntregaRepositorio contextos,
    IProvedorHistorico historico,
    ResolvedorEstrategia resolvedor,
    RabbitMqPublicadorRoteamento publicador,
    NotificadorEntrega notificador,
    ILogger<ServicoRoteamento> logger)
{
    private readonly List<EventoEmitido> _emitidos = new();
    private readonly object _trava = new();

    public IReadOnlyList<EventoEmitido> Emitidos
    {
        get { lock (_trava) { return _emitidos.ToList(); } }
    }

    public async Task<AnaliseConcluida?> AnalisarAsync(
        Guid entregaId, string correlationId, CancellationToken ct = default)
    {
        var contexto = contextos.Obter(entregaId);
        if (contexto is null)
        {
            logger.LogWarning("Entrega {EntregaId} sem contexto cadastrado; análise ignorada.",
                entregaId);
            return null;
        }

        var (posicoes, fonte) = await historico.ObterHistoricoAsync(entregaId, ct);
        var estrategia = resolvedor.Resolver(contexto.TipoCarga);

        ResultadoAnalise resultado;
        var inicio = Stopwatch.GetTimestamp();
        try
        {
            resultado = estrategia.Analisar(new ContextoRota(
                entregaId, contexto.TipoCarga, posicoes, contexto.RotaEsperada,
                DateTimeOffset.UtcNow));
        }
        finally
        {
            MetricasRoteamento.DuracaoAnalise.Observe(
                Stopwatch.GetElapsedTime(inicio).TotalSeconds);
        }
        MetricasRoteamento.Analises.Labels(resultado.Tipo.ToString()).Inc();

        var agora = DateTimeOffset.UtcNow;
        logger.LogInformation(
            "Análise {EntregaId}: {Tipo} ({Severidade}) via {Estrategia} fonte={Fonte}: {Motivo}",
            entregaId, resultado.Tipo, resultado.Severidade,
            estrategia.GetType().Name, fonte, resultado.Motivo);

        if (resultado.Tipo == TipoResultado.NENHUM)
            return new AnaliseConcluida(resultado, fonte);

        var routingKey = resultado.Tipo == TipoResultado.DESVIO
            ? "desvio.detectado"
            : "atraso.detectado";
        if (resultado.Tipo == TipoResultado.DESVIO)
            publicador.Publicar(routingKey,
                new DesvioDetectadoEvent(Guid.NewGuid(), agora, correlationId,
                    entregaId, resultado.Motivo, resultado.Severidade), correlationId);
        else
            publicador.Publicar(routingKey,
                new AtrasoDetectadoEvent(Guid.NewGuid(), agora, correlationId,
                    entregaId, 0, resultado.Severidade), correlationId);
        MetricasRoteamento.EventosPublicados.Labels(routingKey).Inc();

        // Transita o status na Entrega.Api (best-effort) para o dashboard
        // receber "StatusAlterado" via SignalR em tempo real.
        var statusDestino = resultado.Tipo == TipoResultado.DESVIO
            ? "DESVIO_DETECTADO"
            : "ATRASADA";
        await notificador.TransicaoAsync(entregaId, statusDestino, ct);

        lock (_trava)
        {
            _emitidos.Add(new EventoEmitido(
                routingKey,
                entregaId, resultado.Tipo, resultado.Severidade, resultado.Motivo, agora));
        }

        return new AnaliseConcluida(resultado, fonte);
    }
}
