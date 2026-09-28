using Microsoft.Extensions.Logging.Abstractions;
using Roteamento.Api.Application;
using Roteamento.Api.Domain;
using Roteamento.Api.Infrastructure;
using Roteamento.Api.Resiliencia;

namespace Roteamento.Tests.Resiliencia;

// Ciclo do circuit breaker em teste (primário/fallback fakes, janela de 1s):
// falha, falha => ABRE => fallback => (janela passa) => FECHA sozinho.
public class CircuitoHistoricoTests
{
    private sealed class PrimarioFake : IProvedorHistoricoPrimario
    {
        public int FalhasRestantes;
        public int Chamadas;
        public Task<IReadOnlyList<PontoRota>> ObterAsync(Guid id, CancellationToken ct)
        {
            Chamadas++;
            if (FalhasRestantes > 0)
            {
                FalhasRestantes--;
                throw new HttpRequestException("Rastreamento.Api fora do ar (fake).");
            }
            return Task.FromResult<IReadOnlyList<PontoRota>>(
                new[] { new PontoRota(-23.55, -46.63, DateTimeOffset.UtcNow) });
        }
    }

    private sealed class FallbackFake : IFallbackHistorico
    {
        public Task<IReadOnlyList<PontoRota>> ObterAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PontoRota>>(
                new[] { new PontoRota(-23.5, -46.6, DateTimeOffset.UtcNow.AddMinutes(-30)) });
    }

    private static (ProvedorHistoricoResiliente provedor, CircuitoHistorico circuito, PrimarioFake primario)
        Montar(int falhasParaAbrir, int segundosAberto, int falhasDoPrimario)
    {
        var circuito = new CircuitoHistorico(
            new ParametrosCircuito(falhasParaAbrir, segundosAberto),
            NullLogger<CircuitoHistorico>.Instance);
        var primario = new PrimarioFake { FalhasRestantes = falhasDoPrimario };
        var provedor = new ProvedorHistoricoResiliente(circuito, primario, new FallbackFake(),
            NullLogger<ProvedorHistoricoResiliente>.Instance);
        return (provedor, circuito, primario);
    }

    [Fact]
    public async Task Falhas_consecutivas_abrem_o_circuito_e_acionam_fallback()
    {
        var (provedor, circuito, _) = Montar(
            falhasParaAbrir: 2, segundosAberto: 60, falhasDoPrimario: 10);
        var id = Guid.NewGuid();

        var r1 = await provedor.ObterHistoricoAsync(id);
        Assert.Equal("fallback-cache", r1.Fonte); // 1ª falha: ainda fechado, mas caiu no fallback

        var r2 = await provedor.ObterHistoricoAsync(id);
        Assert.Equal("fallback-cache", r2.Fonte); // 2ª falha consecutiva => ABRE

        Assert.Equal("Open", circuito.Estado);

        var r3 = await provedor.ObterHistoricoAsync(id);
        Assert.Equal("fallback-cache", r3.Fonte); // aberto: vai direto ao fallback
    }

    [Fact]
    public async Task Circuito_fecha_sozinho_quando_o_servico_volta()
    {
        var (provedor, circuito, primario) = Montar(
            falhasParaAbrir: 2, segundosAberto: 1, falhasDoPrimario: 2);
        var id = Guid.NewGuid();

        await provedor.ObterHistoricoAsync(id);
        await provedor.ObterHistoricoAsync(id);
        Assert.Equal("Open", circuito.Estado);

        await Task.Delay(TimeSpan.FromSeconds(1.2)); // janela passa => meio-aberto

        var recuperado = await provedor.ObterHistoricoAsync(id);
        Assert.Equal("rastreamento", recuperado.Fonte); // primário voltou a responder
        Assert.Equal("Closed", circuito.Estado); // ...e o circuito fechou sozinho
        Assert.True(primario.Chamadas >= 3);
    }
}
