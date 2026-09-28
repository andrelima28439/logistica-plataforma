using System.Net;
using System.Text.Json;
using Alerta.Api.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Alerta.Tests.Api;

public class AlertaApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AlertaApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GET_alertas_lista_persistidos_com_filtros()
    {
        using var scope = _factory.Services.CreateScope();
        var servico = scope.ServiceProvider.GetRequiredService<ServicoAlerta>();
        var entregaId = Guid.NewGuid();
        await servico.ProcessarDesvioAsync(entregaId, "rota errada", "ALTA", "corr-api-1");
        await servico.ProcessarAtrasoAsync(Guid.NewGuid(), 5, "BAIXA", "corr-api-2");

        var todos = JsonDocument.Parse(
            await _client.GetStringAsync("/alertas")).RootElement;
        Assert.True(todos.GetArrayLength() >= 2);

        var soDesvio = JsonDocument.Parse(
            await _client.GetStringAsync("/alertas?tipo=DESVIO")).RootElement;
        Assert.All(soDesvio.EnumerateArray(),
            e => Assert.Equal("DESVIO", e.GetProperty("tipo").GetString()));

        var detalhe = JsonDocument.Parse(await _client.GetStringAsync(
            $"/alertas/{todos[0].GetProperty("id").GetString()}")).RootElement;
        Assert.Equal(todos[0].GetProperty("id").GetString(),
            detalhe.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GET_alertas_id_desconhecido_retorna_404()
    {
        var resposta = await _client.GetAsync($"/alertas/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Outboxes_expoem_emails_e_sms_mockados()
    {
        using var scope = _factory.Services.CreateScope();
        var servico = scope.ServiceProvider.GetRequiredService<ServicoAlerta>();
        await servico.ProcessarDesvioAsync(Guid.NewGuid(), "frio em risco", "CRITICA", "corr-api-3");

        var emails = JsonDocument.Parse(
            await _client.GetStringAsync("/alertas/canais/emails")).RootElement;
        Assert.Contains(emails.EnumerateArray(),
            e => e.GetString()!.Contains("corr-api-3") || e.GetString()!.Contains("frio em risco"));

        var sms = JsonDocument.Parse(
            await _client.GetStringAsync("/alertas/canais/sms")).RootElement;
        Assert.NotEmpty(sms.EnumerateArray());
    }
}
