# Decisões técnicas (ADRs)

Registro das decisões de arquitetura e seus motivos.

## Infraestrutura

### ADR-001: Seq em vez de Elasticsearch + Kibana para logs centralizados
- **Contexto:** Elasticsearch + Kibana exigem 3+ serviços e ~2-4 GB RAM só para o Elasticsearch; o ambiente local precisa subir com `docker compose up -d` em máquina modesta.
- **Decisão:** usar **Seq** (`datalust/seq`) como coletor de logs estruturados (Serilog) no ambiente local via Docker Compose.
- **Motivo:** container único e leve, integração nativa com Serilog (`Serilog.Sinks.Seq`) e `correlation-id` pesquisável; em produção troca-se por Elasticsearch/OpenSearch ou Azure App Insights sem mudar o código de log (apenas o sink).
- **Notas:** o Seq 2026 exige auth no primeiro boot; no Compose local usa-se `SEQ_FIRSTRUN_NOAUTHENTICATION=true` (somente dev — em produção, `SEQ_FIRSTRUN_ADMINPASSWORD` via secret). Healthcheck do Seq usa `curl` (a imagem não traz `wget`).

### ADR-002: SQL Server 2022 Express no Compose
- **Contexto:** o banco transacional precisa ser o mais comum no ecossistema .NET corporativo para não gerar custo de decisão futura quando o EF entrar; as alternativas seriam SQLite ou Postgres.
- **Decisão:** **SQL Server 2022 Express** (`mcr.microsoft.com/mssql/server:2022-latest`) no Compose, com volume `sqlserver-data` e healthcheck via `sqlcmd SELECT 1`.
- **Motivo:** é o banco padrão em ambientes .NET corporativos; custo zero de migração quando o EF Core entrar. Credenciais de dev via `.env.example` (`Logistica@2026!Dev`).

### ADR-003: Repositórios em memória atrás de interfaces, EF adiado
- **Contexto:** persistência real com EF Core exigiria migrations e mapeamentos desde o primeiro commit, atrasando o feedback das regras de negócio e da mensageria.
- **Decisão:** repositórios **em memória** (`ConcurrentDictionary`) atrás de interfaces (`IEntregaRepositorio`, etc.); a troca por EF Core não muda contratos nem testes. O `/health` já valida o SQL de verdade (`SELECT 1`), então a dependência é real e monitorada desde já.
- **Motivo:** isola a regra de negócio da persistência; permite evoluir para EF Core + migrations + outbox transacional sem quebrar a API.

### ADR-004: Healthchecks com dependências reais + Compose saudável
- **Contexto:** startup fora de ordem (API no ar antes de SQL/Rabbit/Redis/Seq) gera falhas intermitentes na demo.
- **Decisão:** healthchecks reais em todos os serviços do Compose (`sqlcmd`, `rabbitmq-diagnostics ping`, `redis-cli ping`, `curl /health`, `wget /-/healthy`) + resposta JSON padronizada (`Shared.Kernel.Observabilidade.RespostaHealthJson`).
- **Motivo:** `docker compose ps` mostra tudo `healthy` sem intervenção manual; as probes do Kubernetes reutilizam o mesmo `/health` (ver ADR-033).

## Entrega.Api

### ADR-005: Enums em maiúsculas para JSON estável
- **Contexto:** a convenção PascalCase do C# geraria `Padrao`/`Criada` no JSON, quebrando o contrato esperado pelos consumidores (`PADRAO`, `CRIADA`).
- **Decisão:** enums `TipoCarga`/`StatusEntrega` em **maiúsculas** (`PADRAO`, `CRIADA`, …), serializados via `JsonStringEnumConverter`.
- **Motivo:** o JSON expõe exatamente os valores do contrato, sem conversor customizado por campo.

### ADR-006: GET /entregas/{id} com `posicoes: []` estável
- **Contexto:** o frontend precisa de um contrato estável para o detalhe da entrega antes do Rastreamento preencher o histórico.
- **Decisão:** `GET /entregas/{id}` já retorna `posicoes: []` + `eventos` desde a primeira versão.
- **Motivo:** o frontend compila contra um contrato fixo; o Rastreamento passa a preencher sem breaking change.

## Shared.Kernel

### ADR-007: Contratos de integração em Shared.Kernel
- **Contexto:** microsserviços não devem compartilhar modelo de domínio, mas precisam concordar no formato dos eventos RabbitMQ.
- **Decisão:** records `PosicaoAtualizadaEvent`, `DesvioDetectadoEvent`, `AtrasoDetectadoEvent` (com `EntregaId` + `CorrelationId`) em `Shared.Kernel/Eventos.cs`, referenciado pelos três serviços.
- **Motivo:** um único ponto de verdade para o contrato de mensageria; modelos de domínio (`TipoCarga`, `Entrega`) continuam duplicados por bounded context (ver ADR-012).

## Rastreamento.Api

### ADR-008: Worker GPS em processo com sessões por entrega
- **Contexto:** um serviço separado só para simular GPS adicionaria deploy e rede para uma necessidade de demonstração local.
- **Decisão:** `SimuladorGpsWorker : BackgroundService` (tick 1s) no próprio API, com sessões por entrega (`SessaoSimulacao`: random walk em SP + janela de gap com `GapAte`).
- **Motivo:** o mais simples para demonstração local; a lógica de gap é pura e unitariamente testada (`SessaoSimulacaoTests`).

### ADR-009: Degradação graciosa sem broker ou Redis
- **Contexto:** rastreamento é ingestão — perder posição porque o cache ou o broker caiu seria a falha errada.
- **Decisão:** se RabbitMQ ou Redis estiverem fora, o registro da posição **não falha** (warning no log; evento não publicado, cache vazio). `POST /rastreamento/{id}/posicao` continua 201 com histórico + cache quando disponíveis.
- **Motivo:** disponibilidade de escrita acima de completude de evento; a perda de evento é explícita e monitorada (`rastreamento_publicacao_falhas_total`).

### ADR-010: Topologia RabbitMQ `logistica.eventos` / `posicao.atualizada`
- **Contexto:** os eventos de posição precisam chegar ao Roteamento com durabilidade e correlação.
- **Decisão:** exchange `logistica.eventos` (topic, durável), routing key `posicao.atualizada`, mensagem persistente + `CorrelationId` no header AMQP e no corpo.
- **Motivo:** roteamento por tópico permite novos consumidores sem mudar o producer; correlação ponta a ponta no Seq.

### ADR-011: Chave Redis `rastreamento:{id}:ultima` com TTL 24h
- **Contexto:** o dashboard e o fallback do Roteamento precisam da última posição sem bater no histórico toda vez.
- **Decisão:** chave `rastreamento:{entregaId}:ultima` (JSON, TTL 24h) via `RedisCacheUltimaPosicao`.
- **Motivo:** leitura O(1) para o caminho quente; TTL evita lixo de entregas antigas; mesma chave lida pelo fallback (ver ADR-014).

## Roteamento.Api

### ADR-012: Bounded contexts locais (duplicar TipoCarga)
- **Contexto:** referenciar a Entrega.Api para reutilizar `TipoCarga` criaria acoplamento de deploy entre microsserviços.
- **Decisão:** `TipoCarga`/`PontoRota` duplicados no Roteamento; só eventos de integração são compartilhados via `Shared.Kernel`.
- **Motivo:** microsserviços não compartilham modelo de domínio — cada serviço é dono do seu; mudança no domínio da Entrega não quebra o Roteamento.

### ADR-013: Distância ponto–rota simplificada
- **Contexto:** cálculo geodésico preciso (Haversine/projeção) é desnecessário para detectar desvio em demonstração com waypoints esparsos.
- **Decisão:** menor distância euclidiana a waypoints, em graus, com limiares por carga (ex.: REFRIGERADA 0,02°, PADRAO 0,05°).
- **Motivo:** suficiente para demonstração e totalmente testável sem I/O; documentado como simplificação consciente — produção usaria biblioteca geoespacial.

### ADR-014: Fallback lê o Redis direto, não via HTTP
- **Contexto:** se o fallback chamasse a Rastreamento.Api via HTTP, ele falharia exatamente quando mais preciso (com o primário fora do ar).
- **Decisão:** fallback `RedisFallbackHistorico` lê a chave `rastreamento:{id}:ultima` **direto do Redis**, infra independente do API.
- **Motivo:** o fallback funciona precisamente quando o primário cai (infra separada); sem isso o circuit breaker degradaria para "indisponível" sempre.

### ADR-015: Circuit breaker Polly não-genérico, 3 falhas / 15s
- **Contexto:** o overload genérico do Polly 7 não resolveu nesse SDK; a chamada síncrona ao histórico precisa de proteção com fallback.
- **Decisão:** `AsyncCircuitBreakerPolicy` não-genérico: **3 falhas consecutivas → abre; 15s de pausa → meio-aberto → fecha sozinho**. Cada transição atualiza o gauge `roteamento_circuito_estado` (0/1/2).
- **Motivo:** cobre o caso sem perda funcional; janela curta o suficiente para demo e longa o suficiente para evitar flapping.

### ADR-016: Uma busca de histórico por análise
- **Contexto:** o endpoint `/analisar` buscava o histórico 2× (para exibir a fonte + analisar), dobrando a contagem de falhas no breaker.
- **Decisão:** `AnaliseConcluida` carrega `Resultado + FonteHistorico` de **1 busca** (`ProvedorHistoricoResiliente.ObterHistoricoAsync`).
- **Motivo:** contagem exata de falhas no breaker; sem a correção, 1 queda real contava como 2 e o breaker abria antes do configurado.

## Alerta.Api

### ADR-017: Matriz tipo × severidade para canais
- **Contexto:** nem todo evento merece e-mail/SMS; o canal precisa refletir criticidade sem espalhar `if` pelo consumer.
- **Decisão:** `FabricaAlerta`: DESVIO/CRITICA → email+sms+painel; DESVIO/ALTA e ATRASO/CRITICA/ALTA → email+painel; demais → só painel.
- **Motivo:** regra explícita e testável em 6 casos (`FabricaAlertaTests`, incluindo case-insensitive); adicionar canal não toca o consumer.

### ADR-018: Outboxes tipadas em vez de dois List<string>
- **Contexto:** registrar dois `List<string>` no DI causa colisão silenciosa (o container não distingue os dois).
- **Decisão:** records `OutboxEmail`/`OutboxSms` (cada um com `List<string> Itens`), injetados separadamente nos mocks.
- **Motivo:** bug pego antes de commitar; tipagem elimina a ambiguidade sem factory manual de coleções.

### ADR-019: DLQ via DLX + nack sem requeue, decisão isolada
- **Contexto:** poison message não pode travar o consumer nem sumir silenciosamente.
- **Decisão:** fila `alerta.eventos` com `x-dead-letter-exchange=logistica.dlx` → `alerta.eventos.dlq`; **nack sem requeue** em qualquer falha (poison, tipo trocado, routing desconhecida, exceção no canal). Decisão ack/nack isolada em `ProcessadorMensagem` (unitariamente testada).
- **Motivo:** DLQ de verdade no broker (mensagem preservada para inspeção); lógica de roteamento testável sem broker.

### ADR-020: Inspeção da DLQ via Management API sem destruir
- **Contexto:** o dashboard e o troubleshooting precisam mostrar a DLQ sem consumir as mensagens.
- **Decisão:** `InspetorDlq` usa a Management API (profundidade + peek com `ack_requeue_true`).
- **Motivo:** inspeção não-destrutiva; alimenta o painel de saúde e o Cenário de poison sem esvaziar a fila.

## Observabilidade

### ADR-021: Serilog JSON para Seq + Console, com Servico
- **Contexto:** logs precisam ser centralizados e filtráveis por serviço sem overhead de ELK local.
- **Decisão:** Serilog JSON → Seq (`:5341`) + Console, `Enrich.FromLogContext` + propriedade `Servico` por API; `UseSerilogRequestLogging` em todas.
- **Motivo:** trilha HTTP com correlation de graça; um sink troca Seq por Elasticsearch/OpenSearch sem mudar código (ver ADR-001).

### ADR-022: Correlation-id compartilhado via middleware
- **Contexto:** sem correlação, um fluxo ponta a ponta (Entrega → Rastreamento → Roteamento → Alerta) é impossível de seguir no log centralizado.
- **Decisão:** middleware em `Shared.Kernel` (`CorrelationIdMiddleware`): lê `X-Correlation-Id` ou gera; entra no `LogContext`, no header de resposta e nos eventos RabbitMQ (header AMQP + corpo). Consumers envolvem o processamento com `LogContext.PushProperty`.
- **Motivo:** mesmo id visível nos 4 serviços no Seq.

### ADR-023: Health checks reais em /health JSON
- **Contexto:** health mockado esconde queda de dependência e invalida probes do K8s.
- **Decisão:** `/health` JSON com checks reais: Entrega→SQL (`SELECT 1` via SqlClient); Rastreamento→AMQP + `PING`; Roteamento→`GET /health` do Rastreamento + AMQP + `PING`; Alerta→AMQP. Sem mocks: queda real vira 503.
- **Motivo:** o dashboard e o K8s reagem a estado verdadeiro; observado nos dois incidentes do troubleshooting.

### ADR-024: Métricas customizadas com gauge do breaker
- **Contexto:** métricas padrão de HTTP não contam a história de negócio (análises, alertas, DLQ, breaker).
- **Decisão:** prometheus-net com classes estáticas (registro único, seguro nos testes): `entregas_criadas_total`, `rastreamento_posicoes_total{origem}`, `rastreamento_registro_duracao_seconds`, `rastreamento_publicacao_falhas_total`, `roteamento_analises_total{resultado}`, `roteamento_analise_duracao_seconds`, `roteamento_eventos_publicados_total{routing_key}`, `roteamento_circuito_estado` (gauge 0/1/2 nas transições do Polly), `alerta_processados_total{tipo}`, `alerta_canais_total{canal}`, `alerta_falhas_total`, `alerta_processamento_duracao_seconds`, `alerta_dlq_mensagens` (gauge via poller da Management API a cada 15s).
- **Motivo:** painéis de negócio e resiliência sem query complexa; gauge do breaker é o sinal primário do Cenário 2.

### ADR-025: Grafana 100% provisionado, sem clique manual
- **Contexto:** dashboard montado por clique não é reproduzível nem versionado.
- **Decisão:** datasource `prometheus-local` + provider de dashboards + `logistica-operacional.json` com 8 painéis provisionados; Prometheus scrapeia as APIs via `host.docker.internal`.
- **Motivo:** nenhuma configuração manual; `docker compose up` entrega o painel com dados.

### ADR-026: Eventos durante queda do broker são perdidos (fire-and-forget)
- **Contexto:** garantir entrega durante queda do broker exigiria outbox transacional em todos os publishers.
- **Decisão:** publishers são fire-and-forget com warning; posição nunca se perde (histórico + cache), mas o evento do instante não é redelivered.
- **Motivo:** tradeoff consciente para escopo local; em produção: outbox transacional ou redelivery (ver Cenário 1 do troubleshooting).

## Frontend

### ADR-027: Angular 20 standalone + mapa SVG puro
- **Contexto:** biblioteca de mapas adicionaria chave de API e bundle para um dashboard operacional com pontos em SP.
- **Decisão:** Angular 20 standalone (CLI 20, Node 22), 4 páginas (`dashboard`, `alertas`, `saude`, `simulador`) + `core`; mapa = SVG puro com projeção SP.
- **Motivo:** escopo controlado sem dependência externa; componentização clara de projeto Angular real.

### ADR-028: CORS restrito ao dev local
- **Contexto:** o navegador exige CORS nas 4 APIs para o dashboard em `localhost:4200`.
- **Decisão:** policy `frontend-dev` só para `http://localhost:4200` nas 4 APIs.
- **Motivo:** mínimo para desenvolvimento; em produção restringir às origens reais.

### ADR-029: Roteamento/Alerta notificam a Entrega.Api best-effort
- **Contexto:** sem push, o dashboard só teria polling para status e alertas.
- **Decisão:** `POST /entregas/{id}/transicao` (aplica a regra do domínio + broadcast `StatusAlterado`) e `POST .../notificacoes/alerta` (broadcast `AlertaGerado`), ambos best-effort (nunca quebram o fluxo principal).
- **Motivo:** status e alerta chegam em <1s via WebSocket; falha na notificação não reverte análise nem alerta.

### ADR-030: Passagem automática CRIADA → EM_TRANSITO na transição
- **Contexto:** telemetria chegando para entrega ainda `CRIADA` retornava 400 (transição direta para `DESVIO_DETECTADO`/`ATRASADA` é inválida), então o `StatusAlterado` nunca chegava ao dashboard.
- **Decisão:** o endpoint de transição promove automaticamente `CRIADA→EM_TRANSITO` (com broadcast próprio) antes de aplicar o destino.
- **Motivo:** reflete a realidade (telemetria implica em trânsito); matriz do domínio intacta (testes de domínio inalterados, 400 mantido para transições realmente inválidas).

### ADR-031: Saúde via polling 5s, não SignalR
- **Contexto:** empurrar health por WebSocket acoplaria freshness a conexão persistente.
- **Decisão:** página de saúde com polling 5s (`saude.service` agregador); o "sem F5" vem do refresh automático + toasts do Hub para eventos de negócio.
- **Motivo:** health é pull por natureza; SignalR fica reservado a `EntregaCriada`/`StatusAlterado`/`AlertaGerado`.

## Kubernetes

### ADR-032: Infra pesada fora do cluster, DNS interno entre serviços
- **Contexto:** subir SQL Server/RabbitMQ/Redis/Seq como StatefulSets no Minikube local adiciona fragilidade sem valor para demonstração.
- **Decisão:** SQL, RabbitMQ, Redis e Seq continuam no Docker Compose do host; pods alcançam via `host.minikube.internal`. Serviço→serviço usa DNS do cluster (`http://rastreamento-api:8080`).
- **Motivo:** separa infra stateful (host) de workloads stateless (cluster); mesma imagem roda local e no cluster só trocando host.

### ADR-033: Liveness /health/live vs readiness /health
- **Contexto:** a primeira versão usava `/health` (com dependências) nos dois probes — com a dependência fora, o pod entrava em restart loop.
- **Decisão:** liveness → `/health/live` (só processo); readiness → `/health` (com dependências reais).
- **Motivo:** padrão correto: dependência fora tira o pod do tráfego sem reiniciá-lo.

### ADR-034: Frontend sem IP fixo, deriva host/porta
- **Contexto:** IP do Minikube muda por máquina e template de ConfigMap com IP exigiria rebuild por ambiente.
- **Decisão:** `api.ts` deriva host/porta de `window.location.hostname` (localhost→500x, outro host→NodePorts 3050x do mesmo host).
- **Motivo:** sem rebuild por ambiente; funciona em `ng serve`, `port-forward` e NodePort direto.

### ADR-035: NodePort + port-forward para acesso local
- **Contexto:** a rede da VM do Minikube pode não ser roteável do host (ex.: `192.168.49.2` sem ping).
- **Decisão:** Services NodePort (30501–30504, 30080) + `kubectl port-forward` documentado como fallback no README.
- **Motivo:** acesso garantido nos dois cenários sem mudar manifests.

### ADR-036: Secret só com valores dev + publish com restore implícito
- **Contexto:** commitar secret real é inaceitável; `dotnet publish` com `--no-restore` em layer isolado quebra com NETSDK1064 nesse SDK.
- **Decisão:** Secret `logistica-secret` com os mesmos valores do `.env.example` (somente dev); Dockerfiles com publish com restore implícito (sem `--no-restore` separado).
- **Motivo:** produção usa Secret do provedor; build funciona sem workaround frágil.

## CI/CD

### ADR-037: Suíte hermética sem infra externa
- **Contexto:** o CI precisa ser rápido e sem flaky de infra; subir SQL/Rabbit/Redis no pipeline adicionaria minutos e instabilidade.
- **Decisão:** testes HTTP com `WebApplicationFactory` e RabbitMQ/Redis trocados por fakes; implementações reais degradam com warning (nunca throw) sem broker.
- **Motivo:** pipeline rápido e determinístico; o comportamento com infra é coberto por smokes manuais (`scripts/smoke-signalr.mjs`) e pelos incidentes do troubleshooting.

### ADR-038: Jobs backend + frontend + docker no CI
- **Contexto:** os dois stacks precisam de verificação própria mais garantia de que as imagens buildam.
- **Decisão:** `.github/workflows/ci.yml` (push/PR): `backend` (build + `dotnet format whitespace --verify-no-changes` + test com Coverlet + upload do XML), `frontend` (`npm ci` + `ng lint` + build produção), `docker` (build das 5 imagens, sem push).
- **Motivo:** quebra de formato, lint ou Dockerfile falha o PR antes do merge; cobertura publicada como artifact.

### ADR-039: Peculiaridades de formato e lint documentadas
- **Contexto:** o `dotnet format` deste SDK exige subcomando explícito e o `ng add angular-eslint` puxa versão incompatível por padrão.
- **Decisão:** `dotnet format <sln> whitespace` (sem `--nologo`); `angular-eslint@20` + `@angular-eslint/schematics@20` fixados.
- **Motivo:** `verify-no-changes` passa local e no CI sem flags manuais; documentado para não regredir no próximo `ng add`.

