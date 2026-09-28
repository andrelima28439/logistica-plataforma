namespace Roteamento.Api.Domain;

// Ponto geográfico (posição observada ou waypoint da rota esperada).
public sealed record PontoRota(double Latitude, double Longitude, DateTimeOffset? Quando = null);

// Contexto avaliado pelas estratégias.
public sealed record ContextoRota(
    Guid EntregaId,
    TipoCarga TipoCarga,
    IReadOnlyList<PontoRota> Posicoes,
    IReadOnlyList<PontoRota> RotaEsperada,
    DateTimeOffset Agora);

public enum TipoResultado
{
    NENHUM,
    DESVIO,
    ATRASO
}

public sealed record ResultadoAnalise(
    TipoResultado Tipo,
    string Severidade,
    string Motivo);
