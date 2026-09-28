using Rastreamento.Api.Application;
using Rastreamento.Api.Domain;
using Rastreamento.Api.Infrastructure;

namespace Rastreamento.Tests.Application;

public sealed class CacheEmMemoriaFake : ICacheUltimaPosicao
{
    private readonly Dictionary<Guid, Posicao> _cache = new();
    public Task DefinirAsync(Posicao p, CancellationToken ct = default)
    {
        _cache[p.EntregaId] = p;
        return Task.CompletedTask;
    }
    public Task<Posicao?> ObterAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_cache.TryGetValue(id, out var p) ? p : null);
}

public sealed class PublicadorFake : IPublicadorPosicao
{
    public readonly List<(Posicao Posicao, string CorrelationId)> Publicados = new();
    public Task<bool> PublicarAsync(Posicao p, string correlationId, CancellationToken ct = default)
    {
        Publicados.Add((p, correlationId));
        return Task.FromResult(true);
    }
}

public class ServicoRastreamentoTests
{
    [Fact]
    public async Task Registrar_grava_historico_atualiza_cache_e_publica_com_correlacao()
    {
        var repo = new PosicaoEmMemoriaRepositorio();
        var cache = new CacheEmMemoriaFake();
        var pub = new PublicadorFake();
        var servico = new ServicoRastreamento(repo, cache, pub,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ServicoRastreamento>.Instance);
        var entregaId = Guid.NewGuid();

        var pos = await servico.RegistrarPosicaoAsync(
            entregaId, -23.55, -46.63, correlationId: "corr-123");

        Assert.Single(repo.Listar(entregaId));
        Assert.Equal(pos, await cache.ObterAsync(entregaId));
        var publicado = Assert.Single(pub.Publicados);
        Assert.Equal("corr-123", publicado.CorrelationId);
        Assert.Equal(pos, publicado.Posicao);
    }

    [Fact]
    public async Task Registrar_sem_correlacao_gera_uma()
    {
        var repo = new PosicaoEmMemoriaRepositorio();
        var cache = new CacheEmMemoriaFake();
        var pub = new PublicadorFake();
        var servico = new ServicoRastreamento(repo, cache, pub,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ServicoRastreamento>.Instance);

        await servico.RegistrarPosicaoAsync(Guid.NewGuid(), -23.55, -46.63);

        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(pub.Publicados).CorrelationId));
    }
}
