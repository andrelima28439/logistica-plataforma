# Troubleshooting — guia de sustentação

> Cenários **simulados de verdade** (não hipotéticos): cada um foi executado
> contra o ambiente local, com as evidências indicadas. O objetivo é mostrar
> o raciocínio de quem sustenta o sistema em produção: sintoma → onde olhar
> (logs/métricas/health) → comportamento automático → recuperação.

Pré-requisitos dos cenários: `docker compose up -d` + as 4 APIs rodando
(`dotnet run`, portas 5001–5004).

---

## Cenário 1 — RabbitMQ fora do ar por alguns minutos

**Simulação:** `docker stop logistica-rabbitmq` (≈2 min), com o simulador
GPS ligado e tráfego normal; depois `docker start logistica-rabbitmq`.

**O que acontece no sistema (observado):**

| Camada | Comportamento |
|---|---|
| `POST /rastreamento/{id}/posicao` | Continua **201** — posição vai ao histórico + Redis; só o evento se perde |
| `GET /health` (Rastreamento/Alerta/Roteamento) | Vira **503** (check AMQP real falha) |
| Consumers (Roteamento/Alerta) | Loop de retry a cada 5s, sem derrubar a API |
| Métrica `rastreamento_publicacao_falhas_total` | Sobe a cada posição não publicada (observado: 6 em ~12s) |

**Como identificar:**

1. Seq: `@Message like '%RabbitMQ%'` → 59 logs na simulação
   (`RabbitMqPublicadorPosicao`: "evento não publicado";
   `DefaultHealthCheckService`: checks Unhealthy), todos com
   `Servico=rastreamento-api` e o `CorrelationId` quando houver.
2. Prometheus: `rastreamento_publicacao_falhas_total` subindo +
   `rate(logistica publish)` zerado; `/health` dos 3 serviços = Unhealthy.
3. Grafana: painel "Posições/min" continua (ingestão viva), "Alertas por
   tipo" zera (nada chega ao consumer).

**Como se recupera sozinho:** ao subir o broker, os publishers reconectam
(`AutomaticRecoveryEnabled` + reconexão preguiçosa) e os consumers saem do
retry — `/health` volta a `Healthy` sem restart (observado). O fluxo
recomeça (`publish_in` voltou a subir).

**Limitação honesta:** eventos publicados *durante* a queda são perdidos
(fire-and-forget, sem outbox). Posições não se perdem (histórico + cache),
mas a análise daquele instante não acontece — tradeoff documentado em
ADR-009/ADR-026. Em produção: outbox transacional ou redelivery configurado.

---

## Cenário 2 — Rastreamento.Api fora do ar (circuit breaker)

**Simulação:** processo da Rastreamento.Api morto (`Stop-Process`), 3×
`POST /roteamento/entregas/{id}/analisar`, depois restart + simulação
religada (janela do breaker: 15s).

**O que acontece no sistema (observado):**

| Passo | Evidência |
|---|---|
| Normal | `circuit-breaker: Closed`, análise com `fonte=rastreamento` |
| Queda | Após 3 falhas consecutivas: `Open`; análise segue com `fonte=fallback-cache` (Redis direto, não HTTP) |
| `GET /health` do Roteamento | **503** (check `rastreamento` real falha) — sinal para o dashboard |
| Volta | Após a janela: `HalfOpen` → trial OK → `Closed` sozinho |

**Como identificar:**

1. Métrica **`roteamento_circuito_estado`** (gauge 0/1/2): foi 0 → **1**
   (aberto) → **0** (fechado sozinho) — painel "Circuit breaker" do Grafana.
2. Seq: "Circuit breaker ABERTO após 3 falhas..." (Warning) e depois
   "Circuit breaker FECHADO" (Information), com `Servico=roteamento-api`.
3. Resposta do `/analisar` traz `fonteHistorico`: `rastreamento` →
   `fallback-cache` → `rastreamento` (a fonte conta a história sozinha).

**Como se recupera sozinho:** meio-aberto após 15s; se a trial passar,
fecha sem intervenção (observado). O fallback garante análise degradada
(com 1 posição do cache) enquanto isso.

---

## Apêndice — defeitos reais encontrados e corrigidos

Matéria-prima de troubleshooting genuíno (cada um virou decisão documentada):

1. **Seq 2026 exige auth no primeiro boot** — crash em loop (Autofac);
   diagnóstico via `docker logs`, correção com `SEQ_FIRSTRUN_NOAUTHENTICATION`
   só para dev (ver ADR-001).
2. **Healthcheck do Seq com `wget`** — imagem só tem `curl`
   (`/bin/sh: wget: not found`); prova via `docker run --entrypoint sh`
   com `curl -f http://localhost/health` (ver ADR-001).
3. **POST 400 com "São Paulo"** — era o cliente (PowerShell 5.1 sem
   `charset=utf-8`), não a API; confirmado reenviando como UTF-8 com o header correto.
4. **Race no teste de cadência do GPS** — `DateTimeOffset.UtcNow` capturado antes
   do `ProximaEm` do construtor; falha era do teste, não do worker (ver ADR-008).
5. **Overload genérico do Polly 7 não resolve** — assinatura real conferida
   no pacote; usado o breaker não-genérico `AsyncCircuitBreakerPolicy` (ver ADR-015).
6. **Busca de histórico 2× por análise** — dobrava a contagem de falhas do
   breaker; achado *após* a demo passar, corrigido para 1× com `AnaliseConcluida` (ver ADR-016).
7. **`dotnet run` morre sob contenção de build** — `dotnet test` paralelo
   travou o `apphost.exe` (MSB3027) e uma instância do `run` morreu;
   padrão adotado: `dotnet build` + `run --no-build` para demos paralelas.
8. **Logs Debug não aparecem com minimumLevel Information** — o registro de
   posição (Debug) não ia ao Seq; `UseSerilogRequestLogging` cobriu a
   trilha HTTP com correlation (ver ADR-021, ADR-022).
