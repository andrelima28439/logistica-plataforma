using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace Roteamento.Api.Observabilidade;

// GET {rastreamento}/health com timeout curto — é exatamente a dependência
// que o circuit breaker protege; /health do Roteamento reflete a saúde dela.
public sealed class RastreamentoHttpHealthCheck(HttpClient http) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var resposta = await http.GetAsync("/health", cts.Token);
            return resposta.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Rastreamento.Api com /health OK.")
                : HealthCheckResult.Degraded(
                    $"Rastreamento.Api respondeu {(int)resposta.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Rastreamento.Api inacessível.", ex);
        }
    }
}

public sealed class RabbitMqHealthCheck(
    string host, int port, string user, string pass) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = host,
                Port = port,
                UserName = user,
                Password = pass,
                RequestedHeartbeat = TimeSpan.FromSeconds(5)
            };
            using var conexao = factory.CreateConnection("roteamento-api-healthcheck");
            return Task.FromResult(conexao.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ aceitando conexões AMQP.")
                : HealthCheckResult.Unhealthy("Conexão AMQP não abriu."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ inacessível.", ex));
        }
    }
}

public sealed class RedisHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var mux = await ConnectionMultiplexer.ConnectAsync(
                new ConfigurationOptions
                {
                    EndPoints = { connectionString.Split(',')[0] },
                    AbortOnConnectFail = true,
                    ConnectTimeout = 2000
                });
            var pong = await mux.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Redis respondeu PING em {pong.TotalMilliseconds:F0} ms.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis inacessível.", ex);
        }
    }
}
