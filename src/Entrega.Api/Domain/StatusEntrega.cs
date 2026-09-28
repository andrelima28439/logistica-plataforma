namespace Entrega.Api.Domain;

// Ver nota sobre maiúsculas em TipoCarga.cs (ADR-005).
public enum StatusEntrega
{
    CRIADA,
    EM_TRANSITO,
    ENTREGUE,
    ATRASADA,
    DESVIO_DETECTADO
}
