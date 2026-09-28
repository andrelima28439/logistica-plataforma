using System.Text.Json;
using Roteamento.Api.Application;
using Roteamento.Api.Domain;
using StackExchange.Redis;

namespace Roteamento.Api.Infrastructure;

// Chamada síncrona primária: GET /rastreamento/{id}/posicoes.
public sealed class HttpProvedorHistorico(HttpClient http) : IProvedorHistoricoPrimario
{
    public async Task<IReadOnlyList<PontoRota>> ObterAsync(Guid entregaId, CancellationToken ct)
    {
        using var resposta = await http.GetAsync($"/rastreamento/{entregaId}/posicoes", ct);
        resposta.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("posicoes").EnumerateArray()
            .Select(p => new PontoRota(
                p.GetProperty("latitude").GetDouble(),
                p.GetProperty("longitude").GetDouble(),
                p.TryGetProperty("capturadaEm", out var q) && q.ValueKind == JsonValueKind.String
                    ? q.GetDateTimeOffset()
                    : null))
            .ToList();
    }
}

// Provedor primário (sob o circuit breaker) — separado do fallback.
public interface IProvedorHistoricoPrimario
{
    Task<IReadOnlyList<PontoRota>> ObterAsync(Guid entregaId, CancellationToken ct);
}

// Fallback: lê a última posição DIRETO do Redis (mesma chave que o
// Rastreamento.Api grava: rastreamento:{id}:ultima). O Redis é
// infraestrutura independente — continua disponível mesmo com a
// Rastreamento.Api fora do ar.
public sealed class RedisFallbackHistorico : IFallbackHistorico
{
    private readonly Lazy<Task<ConnectionMultiplexer?>> _conexao;
    private readonly ILogger<RedisFallbackHistorico> _logger;

    public RedisFallbackHistorico(string connectionString, ILogger<RedisFallbackHistorico> logger)
    {
        _logger = logger;
        _conexao = new Lazy<Task<ConnectionMultiplexer?>>(() => ConectarAsync(connectionString));
    }

    public async Task<IReadOnlyList<PontoRota>> ObterAsync(Guid entregaId, CancellationToken ct)
    {
        try
        {
            var mux = await _conexao.Value;
            if (mux?.IsConnected != true)
                return Array.Empty<PontoRota>();

            var valor = await mux.GetDatabase()
                .StringGetAsync($"rastreamento:{entregaId}:ultima");
            if (valor.IsNullOrEmpty)
                return Array.Empty<PontoRota>();

            var doc = JsonDocument.Parse(valor.ToString());
            var raiz = doc.RootElement;
            return new[]
            {
                new PontoRota(
                    raiz.GetProperty("Latitude").GetDouble(),
                    raiz.GetProperty("Longitude").GetDouble(),
                    raiz.TryGetProperty("CapturadaEm", out var q)
                        && q.ValueKind == JsonValueKind.String
                        ? q.GetDateTimeOffset()
                        : null)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallback Redis indisponível para {EntregaId}", entregaId);
            return Array.Empty<PontoRota>();
        }
    }

    private static async Task<ConnectionMultiplexer?> ConectarAsync(string cs)
    {
        try
        {
            return await ConnectionMultiplexer.ConnectAsync(cs);
        }
        catch
        {
            return null;
        }
    }
}

public interface IFallbackHistorico
{
    Task<IReadOnlyList<PontoRota>> ObterAsync(Guid entregaId, CancellationToken ct);
}
