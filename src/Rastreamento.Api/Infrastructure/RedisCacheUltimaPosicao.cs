using System.Text.Json;
using Rastreamento.Api.Application;
using Rastreamento.Api.Domain;
using StackExchange.Redis;

namespace Rastreamento.Api.Infrastructure;

// Última posição conhecida no Redis (leitura rápida do dashboard).
// Resiliente: se o Redis estiver fora, degrada para "sem cache"
// (retorna null) em vez de derrubar o registro da posição.
public sealed class RedisCacheUltimaPosicao : ICacheUltimaPosicao, IDisposable
{
    private static readonly TimeSpan Expiracao = TimeSpan.FromHours(24);

    private readonly ILogger<RedisCacheUltimaPosicao> _logger;
    private readonly Lazy<Task<ConnectionMultiplexer?>> _conexao;

    public RedisCacheUltimaPosicao(string connectionString, ILogger<RedisCacheUltimaPosicao> logger)
    {
        _logger = logger;
        _conexao = new Lazy<Task<ConnectionMultiplexer?>>(() => ConectarAsync(connectionString));
    }

    public async Task DefinirAsync(Posicao posicao, CancellationToken ct = default)
    {
        var db = await BancoAsync();
        if (db is null)
            return;

        try
        {
            await db.StringSetAsync(Chave(posicao.EntregaId),
                JsonSerializer.Serialize(posicao), Expiracao);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao gravar cache Redis da entrega {EntregaId}",
                posicao.EntregaId);
        }
    }

    public async Task<Posicao?> ObterAsync(Guid entregaId, CancellationToken ct = default)
    {
        var db = await BancoAsync();
        if (db is null)
            return null;

        try
        {
            var valor = await db.StringGetAsync(Chave(entregaId));
            return valor.IsNullOrEmpty
                ? null
                : JsonSerializer.Deserialize<Posicao>(valor.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ler cache Redis da entrega {EntregaId}", entregaId);
            return null;
        }
    }

    public void Dispose()
    {
        if (_conexao.IsValueCreated && _conexao.Value.IsCompletedSuccessfully
            && _conexao.Value.Result is { } mux)
            mux.Dispose();
    }

    private static string Chave(Guid entregaId) => $"rastreamento:{entregaId}:ultima";

    private async Task<IDatabase?> BancoAsync()
    {
        try
        {
            var mux = await _conexao.Value;
            return mux?.IsConnected == true ? mux.GetDatabase() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis indisponível; seguindo sem cache");
            return null;
        }
    }

    private static async Task<ConnectionMultiplexer?> ConectarAsync(string connectionString)
    {
        try
        {
            return await ConnectionMultiplexer.ConnectAsync(connectionString);
        }
        catch
        {
            return null;
        }
    }
}
