using Alerta.Api.Domain;

namespace Alerta.Api.Canais;

// Outboxes tipadas (evitam colisão de dois List<string> no DI).
public sealed record OutboxEmail(List<string> Itens);
public sealed record OutboxSms(List<string> Itens);

// Factory Pattern: um canal por meio de notificação; a fábrica
// decide a COMBINAÇÃO conforme tipo de evento + severidade.
public interface ICanalAlerta
{
    string Nome { get; }
    Task EnviarAsync(Domain.Alerta alerta, CancellationToken ct = default);
}

// Painel operacional: o "envio" é a própria persistência/visibilidade.
public sealed class AlertaPainel(
    Action<Domain.Alerta>? observador = null) : ICanalAlerta
{
    public string Nome => "painel";
    public Task EnviarAsync(Domain.Alerta alerta, CancellationToken ct = default)
    {
        observador?.Invoke(alerta);
        return Task.CompletedTask;
    }
}

// Mock de e-mail: registra em outbox em memória (visível para demo/teste).
public sealed class AlertaEmailMock(OutboxEmail outbox) : ICanalAlerta
{
    public string Nome => "email";
    public Task EnviarAsync(Domain.Alerta alerta, CancellationToken ct = default)
    {
        outbox.Itens.Add(
            $"[EMAIL-MOCK] {alerta.Tipo}/{alerta.Severidade} entrega {alerta.EntregaId}: {alerta.Mensagem}");
        return Task.CompletedTask;
    }
}

// Mock de SMS: idem.
public sealed class AlertaSmsMock(OutboxSms outbox) : ICanalAlerta
{
    public string Nome => "sms";
    public Task EnviarAsync(Domain.Alerta alerta, CancellationToken ct = default)
    {
        outbox.Itens.Add(
            $"[SMS-MOCK] {alerta.Tipo}/{alerta.Severidade} entrega {alerta.EntregaId}: {alerta.Mensagem}");
        return Task.CompletedTask;
    }
}

public sealed class FabricaAlerta(AlertaPainel painel,
    AlertaEmailMock email,
    AlertaSmsMock sms)
{
    // Matriz tipo × severidade (desvio em refrigerada
    // — que chega como CRITICA — dispara e-mail + painel; atraso leve, só painel).
    public IReadOnlyList<ICanalAlerta> Criar(TipoEventoAlerta tipo, string severidade) =>
        (tipo, severidade.ToUpperInvariant()) switch
        {
            (TipoEventoAlerta.DESVIO, "CRITICA") => new ICanalAlerta[] { email, sms, painel },
            (TipoEventoAlerta.DESVIO, "ALTA") => new ICanalAlerta[] { email, painel },
            (TipoEventoAlerta.DESVIO, _) => new ICanalAlerta[] { painel },
            (TipoEventoAlerta.ATRASO, "CRITICA") => new ICanalAlerta[] { email, painel },
            (TipoEventoAlerta.ATRASO, "ALTA") => new ICanalAlerta[] { email, painel },
            (TipoEventoAlerta.ATRASO, _) => new ICanalAlerta[] { painel },
            _ => new ICanalAlerta[] { painel }
        };
}
