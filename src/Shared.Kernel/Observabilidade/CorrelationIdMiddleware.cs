using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Shared.Kernel.Observabilidade;

// Correlation-id propagado entre serviços: lê X-Correlation-Id
// ou gera um; injeta no Serilog LogContext (todo log carrega a propriedade)
// e devolve no response header. Consumidores RabbitMQ fazem o equivalente
// com LogContext.PushProperty ao redor do processamento.
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext contexto)
    {
        var id = contexto.Request.Headers.TryGetValue(Header, out var recebido)
            && !string.IsNullOrWhiteSpace(recebido)
                ? recebido.ToString()
                : Guid.NewGuid().ToString("N");

        contexto.Items["CorrelationId"] = id;
        contexto.Response.Headers[Header] = id;

        using (LogContext.PushProperty("CorrelationId", id))
            await next(contexto);
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();

    public static string CorrelationIdAtual(this HttpContext contexto) =>
        contexto.Items.TryGetValue("CorrelationId", out var id) && id is string s
            ? s
            : Guid.NewGuid().ToString("N");
}
