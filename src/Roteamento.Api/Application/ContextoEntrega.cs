using System.Collections.Concurrent;
using Roteamento.Api.Domain;

namespace Roteamento.Api.Application;

// Contexto operacional de uma entrega para o roteamento: tipo de carga
// (define a estratégia) + rota esperada simplificada (waypoints).
// Cadastrado via POST /roteamento/entregas; em produção viria da Entrega.Api.
public sealed record ContextoEntrega(
    Guid EntregaId,
    TipoCarga TipoCarga,
    IReadOnlyList<PontoRota> RotaEsperada);

public interface IContextoEntregaRepositorio
{
    void Salvar(ContextoEntrega contexto);
    ContextoEntrega? Obter(Guid entregaId);
}

public sealed class ContextoEntregaEmMemoria : IContextoEntregaRepositorio
{
    private readonly ConcurrentDictionary<Guid, ContextoEntrega> _mapa = new();

    public void Salvar(ContextoEntrega contexto) => _mapa[contexto.EntregaId] = contexto;
    public ContextoEntrega? Obter(Guid entregaId) =>
        _mapa.TryGetValue(entregaId, out var c) ? c : null;
}
