using Polly;
using Polly.CircuitBreaker;
using Roteamento.Api.Domain;
using Roteamento.Api.Observabilidade;

namespace Roteamento.Api.Resiliencia;

public sealed record ParametrosCircuito(int FalhasParaAbrir, int SegundosAberto);

// Circuit breaker (Polly 7): envolve a chamada síncrona à
// Rastreamento.Api. Abre após N falhas consecutivas; meio-abre após a
// janela e fecha sozinho quando o serviço dependente volta.
// Cada transição atualiza o gauge roteamento_circuito_estado.
public sealed class CircuitoHistorico : IDisposable
{
    private readonly AsyncCircuitBreakerPolicy _politica;
    private readonly ILogger<CircuitoHistorico> _logger;
    private bool _disposed;

    public CircuitoHistorico(ParametrosCircuito parametros, ILogger<CircuitoHistorico> logger)
    {
        _logger = logger;
        _politica = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: parametros.FalhasParaAbrir,
                durationOfBreak: TimeSpan.FromSeconds(parametros.SegundosAberto),
                onBreak: (ex, pausa) =>
                {
                    MetricasRoteamento.DefinirEstadoCircuito("Open");
                    _logger.LogWarning(
                        "Circuit breaker ABERTO após {Falhas} falhas; pausa de {Pausa}s. Erro: {Erro}",
                        parametros.FalhasParaAbrir, pausa.TotalSeconds, ex.Message);
                },
                onReset: () =>
                {
                    MetricasRoteamento.DefinirEstadoCircuito("Closed");
                    _logger.LogInformation(
                        "Circuit breaker FECHADO — Rastreamento.Api voltou a responder.");
                },
                onHalfOpen: () =>
                {
                    MetricasRoteamento.DefinirEstadoCircuito("HalfOpen");
                    _logger.LogInformation(
                        "Circuit breaker MEIO-ABERTO — testando Rastreamento.Api.");
                });
    }

    public string Estado => _politica.CircuitState.ToString();

    public Task<IReadOnlyList<PontoRota>> ExecutarAsync(
        Func<CancellationToken, Task<IReadOnlyList<PontoRota>>> chamada,
        CancellationToken ct = default) =>
        _politica.ExecuteAsync(chamada, ct);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
    }
}
