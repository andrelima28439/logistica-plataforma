using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using Roteamento.Api.Api;
using Roteamento.Api.Application;
using Roteamento.Api.Consumidores;
using Roteamento.Api.Estrategias;
using Roteamento.Api.Infrastructure;
using Roteamento.Api.Observabilidade;
using Roteamento.Api.Resiliencia;
using Serilog;
using Serilog.Formatting.Json;
using Shared.Kernel.Observabilidade;

var builder = WebApplication.CreateBuilder(args);

var seqUrl = builder.Configuration["Observabilidade:Seq:Url"] ?? "http://localhost:5341";
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Servico", "roteamento-api")
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.Seq(seqUrl)
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var cfg = builder.Configuration;
var rabbitHost = cfg["Roteamento:RabbitMq:Host"] ?? "localhost";
var rabbitPort = int.TryParse(cfg["Roteamento:RabbitMq:Port"], out var porta) ? porta : 5672;
var rabbitUser = cfg["Roteamento:RabbitMq:User"] ?? "logistica";
var rabbitPass = cfg["Roteamento:RabbitMq:Pass"] ?? "logistica-dev-2026";
var rabbitOpcoes = new RabbitMqOpcoes(rabbitHost, rabbitPort, rabbitUser, rabbitPass,
    cfg["Roteamento:RabbitMq:Exchange"] ?? "logistica.eventos");
var rastreamentoBase = cfg["Roteamento:Rastreamento:BaseUrl"] ?? "http://localhost:5002";
var entregaBase = cfg["Roteamento:Entrega:BaseUrl"] ?? "http://localhost:5001";
var redisConnection = cfg["Roteamento:Redis:ConnectionString"]
    ?? "localhost:6379,abortConnect=false,connectTimeout=2000";
var parametrosCircuito = new ParametrosCircuito(
    int.TryParse(cfg["Roteamento:Circuito:FalhasParaAbrir"], out var f) ? f : 3,
    int.TryParse(cfg["Roteamento:Circuito:SegundosAberto"], out var s) ? s : 15);

builder.Services.AddSingleton(rabbitOpcoes);
builder.Services.AddSingleton(parametrosCircuito);
builder.Services.AddSingleton<IContextoEntregaRepositorio, ContextoEntregaEmMemoria>();
builder.Services.AddSingleton<EstrategiaCargaPadrao>();
builder.Services.AddSingleton<EstrategiaCargaRefrigerada>();
builder.Services.AddSingleton<EstrategiaCargaFragil>();
builder.Services.AddSingleton<IEnumerable<IEstrategiaAlerta>>(sp => new IEstrategiaAlerta[]
{
    sp.GetRequiredService<EstrategiaCargaPadrao>(),
    sp.GetRequiredService<EstrategiaCargaRefrigerada>(),
    sp.GetRequiredService<EstrategiaCargaFragil>()
});
builder.Services.AddSingleton<ResolvedorEstrategia>();
builder.Services.AddSingleton<CircuitoHistorico>();
builder.Services.AddSingleton<IProvedorHistoricoPrimario>(sp =>
    new HttpProvedorHistorico(new HttpClient
    {
        BaseAddress = new Uri(rastreamentoBase),
        Timeout = TimeSpan.FromSeconds(3)
    }));
builder.Services.AddSingleton<IFallbackHistorico>(sp =>
    new RedisFallbackHistorico(redisConnection,
        sp.GetRequiredService<ILogger<RedisFallbackHistorico>>()));
builder.Services.AddSingleton<IProvedorHistorico, ProvedorHistoricoResiliente>();
builder.Services.AddSingleton<RabbitMqPublicadorRoteamento>();
builder.Services.AddSingleton(sp => new NotificadorEntrega(
    new HttpClient { BaseAddress = new Uri(entregaBase), Timeout = TimeSpan.FromSeconds(3) },
    sp.GetRequiredService<ILogger<NotificadorEntrega>>()));
builder.Services.AddSingleton<ServicoRoteamento>();
builder.Services.AddHostedService<ConsumidorPosicoesWorker>();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Processo no ar."))
    .AddCheck("rastreamento", new RastreamentoHttpHealthCheck(
        new HttpClient { BaseAddress = new Uri(rastreamentoBase), Timeout = TimeSpan.FromSeconds(3) }))
    .AddCheck("rabbitmq", new RabbitMqHealthCheck(rabbitHost, rabbitPort, rabbitUser, rabbitPass))
    .AddCheck("redis", new RedisHealthCheck(redisConnection));
// Dev-only: libera o frontend Angular local (ver ADR-028).
builder.Services.AddCors(o => o.AddPolicy("frontend-dev", p => p
    .WithOrigins("http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors("frontend-dev");
app.UseCorrelationId();
app.UseSerilogRequestLogging();
app.UseHttpMetrics();
app.MapHealthChecks("/health", RespostaHealthJson.Opcoes);
app.MapHealthChecks("/health/live", RespostaHealthJson.OpcoesApenas("self"));
app.MapMetrics();
app.MapRoteamento();

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

// Necessário para WebApplicationFactory<Program> nos testes de API.
public partial class Program { }
