using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Rastreamento.Api.Application;
using Rastreamento.Tests.Application;

namespace Rastreamento.Tests.Api;

// Endpoints com dependências externas (RabbitMQ/Redis) trocadas por fakes:
// o contrato HTTP é testado sem broker nem cache reais.
public class RastreamentoApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly PublicadorFake _publicador;
    private readonly CacheEmMemoriaFake _cache;

    public RastreamentoApiTests(WebApplicationFactory<Program> factory)
    {
        _publicador = new PublicadorFake();
        _cache = new CacheEmMemoriaFake();
        _client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IPublicadorPosicao>(_publicador);
            s.AddSingleton<ICacheUltimaPosicao>(_cache);
        })).CreateClient();
    }

    [Fact]
    public async Task POST_posicao_registra_e_GET_posicoes_lista()
    {
        var entregaId = Guid.NewGuid();

        var resposta = await _client.PostAsJsonAsync(
            $"/rastreamento/{entregaId}/posicao", new { latitude = -23.55, longitude = -46.63 });

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Single(_publicador.Publicados); // evento publicado (fake)

        var corpo = JsonDocument.Parse(
            await _client.GetStringAsync($"/rastreamento/{entregaId}/posicoes")).RootElement;
        Assert.Equal(1, corpo.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task POST_posicao_invalida_retorna_400()
    {
        var resposta = await _client.PostAsJsonAsync(
            $"/rastreamento/{Guid.NewGuid()}/posicao", new { latitude = 999.0, longitude = 0.0 });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task GET_ultima_posicao_vem_do_cache()
    {
        var entregaId = Guid.NewGuid();
        await _client.PostAsJsonAsync(
            $"/rastreamento/{entregaId}/posicao", new { latitude = -23.5, longitude = -46.6 });

        var resposta = await _client.GetAsync($"/rastreamento/{entregaId}/ultima-posicao");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("cache", corpo.GetProperty("fonte").GetString());
    }

    [Fact]
    public async Task GET_ultima_posicao_sem_dados_retorna_404()
    {
        var resposta = await _client.GetAsync(
            $"/rastreamento/{Guid.NewGuid()}/ultima-posicao");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Simulacao_iniciar_gap_e_parar_mudam_o_status()
    {
        var entregaId = Guid.NewGuid();

        var iniciar = await _client.PostAsJsonAsync(
            $"/rastreamento/{entregaId}/simulacao/iniciar",
            new { intervaloSegundos = 60.0 });
        Assert.Equal(HttpStatusCode.OK, iniciar.StatusCode);
        var st1 = JsonDocument.Parse(await iniciar.Content.ReadAsStringAsync()).RootElement;
        Assert.True(st1.GetProperty("ativa").GetBoolean());

        var gap = await _client.PostAsJsonAsync(
            $"/rastreamento/{entregaId}/simulacao/gap", new { duracaoSegundos = 60.0 });
        var st2 = JsonDocument.Parse(await gap.Content.ReadAsStringAsync()).RootElement;
        Assert.True(st2.GetProperty("emGap").GetBoolean());

        var status = JsonDocument.Parse(await _client.GetStringAsync(
            $"/rastreamento/{entregaId}/simulacao/status")).RootElement;
        Assert.True(status.GetProperty("ativa").GetBoolean());

        var parar = await _client.PostAsJsonAsync(
            $"/rastreamento/{entregaId}/simulacao/parar", new { });
        var st3 = JsonDocument.Parse(await parar.Content.ReadAsStringAsync()).RootElement;
        Assert.False(st3.GetProperty("ativa").GetBoolean());
    }

    [Fact]
    public async Task Simulacao_gap_sem_simulacao_ativa_retorna_404()
    {
        var resposta = await _client.PostAsJsonAsync(
            $"/rastreamento/{Guid.NewGuid()}/simulacao/gap", new { duracaoSegundos = 10.0 });

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }
}
