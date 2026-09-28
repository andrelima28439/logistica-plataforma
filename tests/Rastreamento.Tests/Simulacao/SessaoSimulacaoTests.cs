using Rastreamento.Api.Simulacao;

namespace Rastreamento.Tests.Simulacao;

// Lógica pura da sessão GPS (sem I/O): random walk, cadência e gap.
public class SessaoSimulacaoTests
{
    [Fact]
    public void GerarProxima_avanca_a_cadencia_e_move_o_ponto()
    {
        var agora = DateTimeOffset.UtcNow;
        var sessao = new SessaoSimulacao(Guid.NewGuid(), TimeSpan.FromSeconds(5), seed: 42);

        // ProximaEm nasce no instante da construção: consulta com "agora"
        // fresco para não perder por microssegundos (flaky).
        Assert.True(sessao.DeveGerar(DateTimeOffset.UtcNow));
        var p1 = Assert.IsType<Rastreamento.Api.Domain.Posicao>(
            sessao.GerarProxima(agora));
        var p2 = sessao.GerarProxima(agora.AddSeconds(5));

        Assert.NotNull(p2);
        Assert.Equal(agora.AddSeconds(5), p2!.CapturadaEm);
        Assert.True(sessao.PosicoesGeradas == 2);
        Assert.NotEqual((p1.Latitude, p1.Longitude), (p2.Latitude, p2.Longitude));
        Assert.False(sessao.DeveGerar(agora.AddSeconds(6))); // próxima só aos 10s
    }

    [Fact]
    public void Gap_aplicado_faz_GerarProxima_retornar_null_e_depois_retoma()
    {
        var agora = DateTimeOffset.UtcNow;
        var sessao = new SessaoSimulacao(Guid.NewGuid(), TimeSpan.FromSeconds(5), seed: 7);

        sessao.AplicarGap(TimeSpan.FromSeconds(12), agora);
        Assert.True(sessao.EmGap(agora.AddSeconds(1)));

        Assert.Null(sessao.GerarProxima(agora)); // sem sinal: gap no histórico
        Assert.Null(sessao.GerarProxima(agora.AddSeconds(5)));
        Assert.Equal(0, sessao.PosicoesGeradas);

        var retomada = sessao.GerarProxima(agora.AddSeconds(13));
        Assert.NotNull(retomada); // sinal voltou
        Assert.False(sessao.EmGap(agora.AddSeconds(13)));
        Assert.Equal(1, sessao.PosicoesGeradas);
    }

    [Fact]
    public void Gap_programado_dispara_apos_N_posicoes()
    {
        var agora = DateTimeOffset.UtcNow;
        var sessao = new SessaoSimulacao(Guid.NewGuid(), TimeSpan.FromSeconds(5),
            gapAposPosicoes: 2, gapDuracao: TimeSpan.FromSeconds(20), seed: 9);

        Assert.NotNull(sessao.GerarProxima(agora));
        Assert.NotNull(sessao.GerarProxima(agora.AddSeconds(5)));
        Assert.Null(sessao.GerarProxima(agora.AddSeconds(10))); // gap começou
        Assert.True(sessao.EmGap(agora.AddSeconds(11)));
    }
}
