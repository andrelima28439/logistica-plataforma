using System.Collections.Concurrent;
using Rastreamento.Api.Application;
using Rastreamento.Api.Domain;

namespace Rastreamento.Api.Simulacao;

public sealed record SimulacaoStatus(
    bool Ativa,
    bool EmGap,
    double IntervaloSegundos,
    int PosicoesGeradas,
    Posicao? UltimaPosicao);

// BackgroundService que simula dispositivos GPS: a cada tick (1s) gera a
// próxima posição de cada simulação ativa e registra via ServicoRastreamento
// (histórico + Redis + RabbitMQ). Escopo ISingleton acessado pelos endpoints.
public sealed class SimuladorGpsWorker : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<Guid, SessaoSimulacao> _sessoes = new();
    private readonly IServiceProvider _services;
    private readonly ILogger<SimuladorGpsWorker> _logger;

    public SimuladorGpsWorker(IServiceProvider services, ILogger<SimuladorGpsWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    public SimulacaoStatus Iniciar(Guid entregaId, TimeSpan intervalo,
        int? gapAposPosicoes = null, TimeSpan? gapDuracao = null)
    {
        var sessao = new SessaoSimulacao(entregaId, intervalo, gapAposPosicoes, gapDuracao);
        _sessoes[entregaId] = sessao;
        _logger.LogInformation(
            "Simulação GPS iniciada entrega {EntregaId} intervalo {Intervalo}s",
            entregaId, intervalo.TotalSeconds);
        return Status(entregaId);
    }

    public bool Parar(Guid entregaId) => _sessoes.TryRemove(entregaId, out _);

    public SimulacaoStatus? AplicarGap(Guid entregaId, TimeSpan duracao)
    {
        if (!_sessoes.TryGetValue(entregaId, out var sessao))
            return null;
        sessao.AplicarGap(duracao);
        _logger.LogWarning(
            "Perda de sinal simulada entrega {EntregaId} por {Duracao}s",
            entregaId, duracao.TotalSeconds);
        return Status(entregaId);
    }

    public SimulacaoStatus Status(Guid entregaId)
    {
        if (!_sessoes.TryGetValue(entregaId, out var sessao))
            return new SimulacaoStatus(false, false, 0, 0, null);

        using var scope = _services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPosicaoRepositorio>();
        return new SimulacaoStatus(true, sessao.EmGap(DateTimeOffset.UtcNow),
            sessao.Intervalo.TotalSeconds, sessao.PosicoesGeradas, repo.Ultima(entregaId));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Simulador GPS em execução (tick de {Tick}s)", Tick.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var agora = DateTimeOffset.UtcNow;

            foreach (var (entregaId, sessao) in _sessoes.ToArray())
            {
                if (!sessao.DeveGerar(agora))
                    continue;

                var posicao = sessao.GerarProxima(agora);
                if (posicao is null)
                    continue; // gap de sinal: nenhuma posição neste tick

                try
                {
                    using var scope = _services.CreateScope();
                    var servico = scope.ServiceProvider.GetRequiredService<ServicoRastreamento>();
                    await servico.RegistrarPosicaoAsync(
                        entregaId, posicao.Latitude, posicao.Longitude,
                        correlationId: null, origem: "simulada", ct: stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha ao registrar posição simulada da entrega {EntregaId}",
                        entregaId);
                }
            }

            try
            {
                await Task.Delay(Tick, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
