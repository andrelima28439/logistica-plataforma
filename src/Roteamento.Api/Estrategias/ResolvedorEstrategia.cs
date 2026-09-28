using Roteamento.Api.Domain;

namespace Roteamento.Api.Estrategias;

// Resolver/factory: escolhe a estratégia pelo tipo de carga.
public sealed class ResolvedorEstrategia(IEnumerable<IEstrategiaAlerta> estrategias)
{
    private readonly Dictionary<TipoCarga, IEstrategiaAlerta> _porTipo =
        estrategias.ToDictionary(e => e.TipoCarga);

    public IEstrategiaAlerta Resolver(TipoCarga tipoCarga) =>
        _porTipo.TryGetValue(tipoCarga, out var estrategia)
            ? estrategia
            : throw new InvalidOperationException(
                $"Sem estratégia registrada para o tipo de carga {tipoCarga}.");
}
