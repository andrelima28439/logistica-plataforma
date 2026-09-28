namespace Alerta.Api.Domain;

public enum TipoEventoAlerta
{
    DESVIO,
    ATRASO
}

// Alerta persistido: consumido pelo dashboard via GET /alertas.
public sealed class Alerta
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EntregaId { get; init; }
    public TipoEventoAlerta Tipo { get; init; }
    public string Severidade { get; init; } = "MEDIA";
    public string Mensagem { get; init; } = string.Empty;
    public List<string> CanaisAcionados { get; init; } = new();
    public string CorrelationId { get; init; } = string.Empty;
    public DateTimeOffset CriadoEm { get; init; } = DateTimeOffset.UtcNow;
}
