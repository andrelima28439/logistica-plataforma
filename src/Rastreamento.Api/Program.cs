using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;
using Rastreamento.Api.Api;
using Rastreamento.Api.Application;
using Rastreamento.Api.Infrastructure;
using Rastreamento.Api.Observabilidade;
using Rastreamento.Api.Simulacao;
using Serilog;
using Serilog.Formatting.Json;
using Shared.Kernel.Observabilidade;

var builder = WebApplication.CreateBuilder(args);

var seqUrl = builder.Configuration["Observabilidade:Seq:Url"] ?? "http://localhost:5341";
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Servico", "rastreamento-api")
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.Seq(seqUrl)
    .CreateLogger();
builder.Host.UseSerilog();

var rabbit = builder.Configuration.GetSection("Rastreamento:RabbitMq");
var rabbitHost = rabbit["Host"] ?? "localhost";
var rabbitPort = int.TryParse(rabbit["Port"], out var porta) ? porta : 5672;
var rabbitUser = rabbit["User"] ?? "logistica";
var rabbitPass = rabbit["Pass"] ?? "logistica-dev-2026";
var rabbitOpcoes = new RabbitMqOpcoes(
    rabbitHost, rabbitPort, rabbitUser, rabbitPass,
    rabbit["Exchange"] ?? "logistica.eventos");
var redisConnection = builder.Configuration["Rastreamento:Redis:ConnectionString"]
    ?? "localhost:6379,abortConnect=false,connectTimeout=2000";

builder.Services.AddSingleton(rabbitOpcoes);
builder.Services.AddSingleton<IPosicaoRepositorio, PosicaoEmMemoriaRepositorio>();
builder.Services.AddSingleton<ICacheUltimaPosicao>(sp =>
    new RedisCacheUltimaPosicao(redisConnection,
        sp.GetRequiredService<ILogger<RedisCacheUltimaPosicao>>()));
builder.Services.AddSingleton<IPublicadorPosicao>(sp =>
    new RabbitMqPublicadorPosicao(
        sp.GetRequiredService<RabbitMqOpcoes>(),
        sp.GetRequiredService<ILogger<RabbitMqPublicadorPosicao>>()));
builder.Services.AddScoped<ServicoRastreamento>();
builder.Services.AddSingleton<SimuladorGpsWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimuladorGpsWorker>());
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Processo no ar."))
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
app.MapRastreamento();

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
