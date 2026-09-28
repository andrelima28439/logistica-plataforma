using Rastreamento.Api.Domain;

namespace Rastreamento.Api.Simulacao;

// Estado de simulação de UM dispositivo GPS. Lógica pura (sem I/O) para
// ser testável: o worker só chama DeveGerar/GerarProxima a cada tick.
// A "perda de sinal" é uma janela (GapAte) em que GerarProxima retorna
// null — o histórico fica com um gap real de timestamps.
public sealed class SessaoSimulacao
{
    // Centro inicial: São Paulo/SP (random walk do dispositivo mockado).
    private const double LatInicial = -23.5505;
    private const double LonInicial = -46.6333;
    private const double PassoMaximoGraus = 0.004;

    private readonly Random _random;
    private readonly object _trava = new();
    private double _lat;
    private double _lon;

    public Guid EntregaId { get; }
    public TimeSpan Intervalo { get; }
    public int? GapAposPosicoes { get; }
    public TimeSpan? GapDuracao { get; }
    public int PosicoesGeradas { get; private set; }
    public DateTimeOffset ProximaEm { get; private set; }
    public DateTimeOffset? GapAte { get; private set; }

    public SessaoSimulacao(
        Guid entregaId,
        TimeSpan intervalo,
        int? gapAposPosicoes = null,
        TimeSpan? gapDuracao = null,
        int? seed = null)
    {
        EntregaId = entregaId;
        Intervalo = intervalo;
        GapAposPosicoes = gapAposPosicoes;
        GapDuracao = gapDuracao;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        _lat = LatInicial + (_random.NextDouble() - 0.5) * 0.02;
        _lon = LonInicial + (_random.NextDouble() - 0.5) * 0.02;
        ProximaEm = DateTimeOffset.UtcNow;
    }

    public bool EmGap(DateTimeOffset agora)
    {
        lock (_trava)
        {
            return GapAte.HasValue && agora < GapAte.Value;
        }
    }

    public bool DeveGerar(DateTimeOffset agora)
    {
        lock (_trava)
        {
            return agora >= ProximaEm;
        }
    }

    // Simula perda de sinal imediata pelos próximos `duracao`.
    public void AplicarGap(TimeSpan duracao, DateTimeOffset? agora = null)
    {
        lock (_trava)
        {
            GapAte = (agora ?? DateTimeOffset.UtcNow) + duracao;
        }
    }

    // Retorna null quando o dispositivo está sem sinal (gap).
    public Posicao? GerarProxima(DateTimeOffset? agora = null)
    {
        lock (_trava)
        {
            var instante = agora ?? DateTimeOffset.UtcNow;

            // Agenda o gap programado (ex.: "perder sinal após 10 posições").
            if (GapAposPosicoes.HasValue && GapDuracao.HasValue
                && GapAte is null && PosicoesGeradas >= GapAposPosicoes.Value)
            {
                GapAte = instante + GapDuracao.Value;
            }

            ProximaEm = instante + Intervalo;

            if (GapAte.HasValue && instante < GapAte.Value)
                return null; // sem sinal: nada é gerado (gap no histórico)

            _lat += (_random.NextDouble() - 0.5) * 2 * PassoMaximoGraus;
            _lon += (_random.NextDouble() - 0.5) * 2 * PassoMaximoGraus;
            PosicoesGeradas++;

            return new Posicao(EntregaId, _lat, _lon, instante);
        }
    }
}
