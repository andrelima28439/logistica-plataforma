using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Roteamento.Api.Application;
using Roteamento.Api.Domain;

namespace Roteamento.Tests.Api;

// Endpoints com o histórico trocado por fake (sem Rastreamento.Api real);
// o publicador real degrada com warning sem broker — seguro em teste.
public class RoteamentoApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed class HistoricoFake : IProvedorHistorico
    {
        public Task<ResultadoHistorico> ObterHistoricoAsync(
            Guid entregaId, CancellationToken ct = default) =>
            Task.FromResult(new ResultadoHistorico(
                new[] { new PontoRota(-23.0, -46.0, DateTimeOffset.UtcNow) },
                "fake"));
    }

    private readonly HttpClient _client;

    public RoteamentoApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddSingleton<IProvedorHistorico, HistoricoFake>())).CreateClient();
    }

    [Fact]
    public async Task Cadastrar_contexto_e_analisar_detecta_desvio()
    {
        var entregaId = Guid.NewGuid();
        var cadastro = await _client.PostAsJsonAsync("/roteamento/entregas", new
        {
            entregaId,
            tipoCarga = "REFRIGERADA",
            rotaEsperada = new[] { new { latitude = -22.9, longitude = -43.2 } } // Rio: longe
        });
        Assert.Equal(HttpStatusCode.Created, cadastro.StatusCode);

        var analise = await _client.PostAsJsonAsync(
            $"/roteamento/entregas/{entregaId}/analisar", new { });
        Assert.Equal(HttpStatusCode.OK, analise.StatusCode);

        var corpo = JsonDocument.Parse(await analise.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("DESVIO", corpo.GetProperty("tipo").GetString());
        Assert.Equal("CRITICA", corpo.GetProperty("severidade").GetString());

        var eventos = JsonDocument.Parse(
            await _client.GetStringAsync("/roteamento/eventos")).RootElement;
        Assert.Contains(eventos.EnumerateArray(),
            e => e.GetProperty("routingKey").GetString() == "desvio.detectado");
    }

    [Fact]
    public async Task Analisar_sem_contexto_retorna_404()
    {
        var resposta = await _client.PostAsJsonAsync(
            $"/roteamento/entregas/{Guid.NewGuid()}/analisar", new { });

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Circuit_breaker_expõe_estado()
    {
        var corpo = JsonDocument.Parse(
            await _client.GetStringAsync("/roteamento/circuit-breaker")).RootElement;

        Assert.Equal("Closed", corpo.GetProperty("estado").GetString());
    }
}
