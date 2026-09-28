using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace Rastreamento.Api.Observabilidade;

// Tenta abrir conexão AMQP de verdade (curta, só para o check).
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
            using var conexao = factory.CreateConnection("rastreamento-api-healthcheck");
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

// PING de verdade no Redis.
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
