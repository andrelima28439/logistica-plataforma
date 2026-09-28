using Roteamento.Api.Domain;
using Roteamento.Api.Estrategias;

namespace Roteamento.Tests.Estrategias;

// Prova isolada (sem subir a API): MESMA sequência de
// posições, tipos de carga diferentes => estratégias/resultados diferentes.
public class EstrategiaSelecaoTests
{
    private static readonly ResolvedorEstrategia Resolvedor = new(
        new IEstrategiaAlerta[]
        {
            new EstrategiaCargaPadrao(),
            new EstrategiaCargaRefrigerada(),
            new EstrategiaCargaFragil()
        });

    private static ContextoRota Contexto(
        TipoCarga tipo, double desvioGraus, double idadeMinutos, DateTimeOffset agora) =>
        new(
            Guid.NewGuid(),
            tipo,
            new[] { new PontoRota(-23.55, -46.63, agora.AddMinutes(-idadeMinutos)) },
            new[] { new PontoRota(-23.55 + desvioGraus, -46.63) },
            agora);

    [Fact]
    public void Mesmas_posicoes_estrategias_diferentes_para_cada_carga()
    {
        var agora = DateTimeOffset.UtcNow;

        // Desvio de 0,04° + última posição há 5 min.
        var refrigerada = Resolvedor.Resolver(TipoCarga.REFRIGERADA)
            .Analisar(Contexto(TipoCarga.REFRIGERADA, 0.04, 5, agora));
        var padrao = Resolvedor.Resolver(TipoCarga.PADRAO)
            .Analisar(Contexto(TipoCarga.PADRAO, 0.04, 5, agora));
        var fragil = Resolvedor.Resolver(TipoCarga.FRAGIL)
            .Analisar(Contexto(TipoCarga.FRAGIL, 0.04, 5, agora));

        // Refrigerada é rígida: 0,04° > 0,02° => DESVIO crítico.
        Assert.Equal(TipoResultado.DESVIO, refrigerada.Tipo);
        Assert.Equal("CRITICA", refrigerada.Severidade);

        // Padrão tolera: 0,04° < 0,05° e 5 min < 15 min => NENHUM.
        Assert.Equal(TipoResultado.NENHUM, padrao.Tipo);

        // Frágil: 0,04° > 0,03° => DESVIO (severidade própria, não a da refrigerada).
        Assert.Equal(TipoResultado.DESVIO, fragil.Tipo);
        Assert.Equal("ALTA", fragil.Severidade);
    }

    [Fact]
    public void Tolerancia_de_atraso_e_mais_rigida_na_refrigerada()
    {
        var agora = DateTimeOffset.UtcNow;

        // Na rota (desvio 0) mas sem posição nova há 12 min.
        var refrigerada = Resolvedor.Resolver(TipoCarga.REFRIGERADA)
            .Analisar(Contexto(TipoCarga.REFRIGERADA, 0.0, 12, agora));
        var fragil = Resolvedor.Resolver(TipoCarga.FRAGIL)
            .Analisar(Contexto(TipoCarga.FRAGIL, 0.0, 12, agora));
        var padrao = Resolvedor.Resolver(TipoCarga.PADRAO)
            .Analisar(Contexto(TipoCarga.PADRAO, 0.0, 12, agora));

        Assert.Equal(TipoResultado.ATRASO, refrigerada.Tipo); // 12 > 3
        Assert.Equal(TipoResultado.ATRASO, fragil.Tipo);      // 12 > 10
        Assert.Equal(TipoResultado.NENHUM, padrao.Tipo);      // 12 < 15
        Assert.Equal("MEDIA", fragil.Severidade);
    }

    [Theory]
    [InlineData(TipoCarga.PADRAO, typeof(EstrategiaCargaPadrao))]
    [InlineData(TipoCarga.REFRIGERADA, typeof(EstrategiaCargaRefrigerada))]
    [InlineData(TipoCarga.FRAGIL, typeof(EstrategiaCargaFragil))]
    public void Resolvedor_retorna_a_implementacao_correta(
        TipoCarga tipo, Type esperado) =>
        Assert.IsType(esperado, Resolvedor.Resolver(tipo));

    [Fact]
    public void Sem_posicoes_nao_ha_analise()
    {
        var agora = DateTimeOffset.UtcNow;
        var ctx = new ContextoRota(Guid.NewGuid(), TipoCarga.PADRAO,
            Array.Empty<PontoRota>(),
            new[] { new PontoRota(-23.55, -46.63) }, agora);

        Assert.Equal(TipoResultado.NENHUM,
            Resolvedor.Resolver(TipoCarga.PADRAO).Analisar(ctx).Tipo);
    }
}
