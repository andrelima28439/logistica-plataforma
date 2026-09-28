using Microsoft.AspNetCore.SignalR;

namespace Entrega.Api.Api;

// Hub de tempo real consumido pelo dashboard Angular.
// Mapeado em /entregas/stream.
public sealed class EntregasHub : Hub
{
}
