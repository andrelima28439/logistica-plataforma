using System.Collections.Concurrent;

namespace Alerta.Api.Application;

public interface IAlertaRepositorio
{
    void Adicionar(Domain.Alerta alerta);
    Domain.Alerta? Obter(Guid id);
    IReadOnlyList<Domain.Alerta> Listar(string? tipo, string? severidade);
}

public sealed class AlertaEmMemoriaRepositorio : IAlertaRepositorio
{
    private readonly ConcurrentDictionary<Guid, Domain.Alerta> _alertas = new();

    public void Adicionar(Domain.Alerta alerta) => _alertas[alerta.Id] = alerta;
    public Domain.Alerta? Obter(Guid id) =>
        _alertas.TryGetValue(id, out var a) ? a : null;

    public IReadOnlyList<Domain.Alerta> Listar(string? tipo, string? severidade) =>
        _alertas.Values
            .Where(a => string.IsNullOrWhiteSpace(tipo)
                || a.Tipo.ToString().Equals(tipo, StringComparison.OrdinalIgnoreCase))
            .Where(a => string.IsNullOrWhiteSpace(severidade)
                || a.Severidade.Equals(severidade, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.CriadoEm)
            .ToList();
}
