namespace Entrega.Api.Domain;

public sealed class TransicaoDeStatusInvalidaException : Exception
{
    public StatusEntrega De { get; }
    public StatusEntrega Para { get; }

    public TransicaoDeStatusInvalidaException(StatusEntrega de, StatusEntrega para)
        : base($"Transição de status inválida: {de} -> {para}.")
    {
        De = de;
        Para = para;
    }
}
