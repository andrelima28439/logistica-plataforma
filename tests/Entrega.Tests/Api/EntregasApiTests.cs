using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Entrega.Tests.Api;

// TDD (RED): testes da API REST escritos ANTES dos endpoints existirem.
public class EntregasApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public EntregasApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private static object NovaEntregaPayload(string transportadorId = "transp-1") => new
    {
        origem = "São Paulo/SP",
        destino = "Rio de Janeiro/RJ",
        transportadorId,
        tipoCarga = "REFRIGERADA",
        prazoEstimado = DateTimeOffset.UtcNow.AddDays(3)
    };

    [Fact]
    public async Task POST_entregas_cria_e_retorna_201_com_status_CRIADA()
    {
        var resposta = await _client.PostAsJsonAsync("/entregas", NovaEntregaPayload());

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.NotEqual(Guid.Empty.ToString(), corpo.GetProperty("id").GetString());
        Assert.Equal("CRIADA", corpo.GetProperty("status").GetString());
        Assert.Equal("REFRIGERADA", corpo.GetProperty("tipoCarga").GetString());
        Assert.NotNull(resposta.Headers.Location);
    }

    [Fact]
    public async Task POST_entregas_sem_origem_retorna_400()
    {
        var resposta = await _client.PostAsJsonAsync("/entregas", new
        {
            origem = "",
            destino = "Rio de Janeiro/RJ",
            transportadorId = "transp-1",
            tipoCarga = "PADRAO",
            prazoEstimado = DateTimeOffset.UtcNow.AddDays(1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task GET_entregas_filtra_por_transportador()
    {
        await _client.PostAsJsonAsync("/entregas", NovaEntregaPayload("transp-1"));
        await _client.PostAsJsonAsync("/entregas", NovaEntregaPayload("transp-2"));

        var corpo = JsonDocument.Parse(
            await _client.GetStringAsync("/entregas?transportadorId=transp-2")).RootElement;

        Assert.All(corpo.EnumerateArray(),
            e => Assert.Equal("transp-2", e.GetProperty("transportadorId").GetString()));
        Assert.NotEmpty(corpo.EnumerateArray());
    }

    [Fact]
    public async Task GET_entregas_filtra_por_status()
    {
        await _client.PostAsJsonAsync("/entregas", NovaEntregaPayload());

        var criadas = JsonDocument.Parse(
            await _client.GetStringAsync("/entregas?status=CRIADA")).RootElement;
        Assert.NotEmpty(criadas.EnumerateArray());

        var entregues = JsonDocument.Parse(
            await _client.GetStringAsync("/entregas?status=ENTREGUE")).RootElement;
        Assert.Empty(entregues.EnumerateArray());
    }

    [Fact]
    public async Task GET_entregas_id_retorna_detalhe_com_posicoes_e_eventos()
    {
        var criada = await _client.PostAsJsonAsync("/entregas", NovaEntregaPayload());
        var id = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var resposta = await _client.GetAsync($"/entregas/{id}");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(id, corpo.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Array, corpo.GetProperty("posicoes").ValueKind);
        var eventos = corpo.GetProperty("eventos");
        Assert.NotEmpty(eventos.EnumerateArray());
    }

    [Fact]
    public async Task GET_entregas_id_desconhecido_retorna_404()
    {
        var resposta = await _client.GetAsync($"/entregas/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task POST_transicao_aplica_regra_do_dominio()
    {
        var criada = await _client.PostAsJsonAsync("/entregas", NovaEntregaPayload());
        var id = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var ok = await _client.PostAsJsonAsync($"/entregas/{id}/transicao",
            new { status = "EM_TRANSITO" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("EM_TRANSITO", JsonDocument.Parse(await ok.Content.ReadAsStringAsync())
            .RootElement.GetProperty("status").GetString());

        // Pular para ENTREGUE direto de EM_TRANSITO é válido; de CRIADA não.
        var criada2 = await _client.PostAsJsonAsync("/entregas", NovaEntregaPayload());
        var id2 = JsonDocument.Parse(await criada2.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();
        var invalida = await _client.PostAsJsonAsync($"/entregas/{id2}/transicao",
            new { status = "ENTREGUE" });
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
    }

    [Fact]
    public async Task POST_transicao_de_desvio_em_entrega_criada_passa_por_em_transito()
    {
        var criada = await _client.PostAsJsonAsync("/entregas", NovaEntregaPayload());
        var id = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var ok = await _client.PostAsJsonAsync($"/entregas/{id}/transicao",
            new { status = "DESVIO_DETECTADO" });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("DESVIO_DETECTADO", JsonDocument.Parse(await ok.Content.ReadAsStringAsync())
            .RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task POST_notificar_alerta_retorna_202_e_404_para_desconhecida()
    {
        var criada = await _client.PostAsJsonAsync("/entregas", NovaEntregaPayload());
        var id = JsonDocument.Parse(await criada.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var ok = await _client.PostAsJsonAsync($"/entregas/{id}/notificacoes/alerta",
            new { tipo = "DESVIO", severidade = "CRITICA", mensagem = "teste" });
        Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);

        var naoExiste = await _client.PostAsJsonAsync(
            $"/entregas/{Guid.NewGuid()}/notificacoes/alerta",
            new { tipo = "DESVIO", severidade = "CRITICA", mensagem = "teste" });
        Assert.Equal(HttpStatusCode.NotFound, naoExiste.StatusCode);
    }
}
