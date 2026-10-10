# ADR-0006: Consolidação diária independente da ordem dos eventos

**Status:** Aceito · **Data:** 2026-10-09

## Contexto

Com vários workers processando lotes em paralelo, e com reprocessamento após falhas, os eventos de uma mesma conta e dia podem ser aplicados em qualquer ordem. Um cálculo que dependa da ordem exigiria controle de sequência, checkpoint (marcador de "até onde já processei") e deduplicação.

## Decisão

Calcular o consolidado só com operações que **não dependem da ordem**:

- **Créditos, débitos e quantidade:** somas.
- **Saldo de abertura:** vem do lançamento de **menor** sequência do dia (`balance_after` menos o efeito dele).
- **Saldo de fechamento:** vem do lançamento de **maior** sequência do dia (`balance_after`).

Cada lote é resumido por conta e dia (`LedgerRules.SummarizeDay`) e somado ao dia existente com um upsert (`INSERT ... ON CONFLICT DO UPDATE`), que mantém a abertura do menor `first_sequence` e o fechamento do maior `last_sequence`. Os upserts de um lote são feitos em ordem fixa (conta, dia) para evitar deadlock (dois processos esperando um pelo outro).

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Processar em ordem estrita por conta (checkpoint de sequência) | Exige controle de ordem, travas por conta no worker e tratamento de lacunas. Era o desenho da primeira versão, bem mais complexo |
| Recalcular o dia inteiro a partir do razão a cada evento | Custo cresce com o volume do dia; lê muito mais do que escreve |

## Consequências

- **Positivas:** qualquer ordem de chegada dá o mesmo resultado. Vários workers em paralelo sem coordenação além do `SKIP LOCKED`. Reconstrução completa possível (zerar o consolidado e marcar a outbox como pendente).
- **Negativas:** a regra de cálculo precisa continuar comutativa (independente de ordem). Uma métrica nova que dependa de ordem (ex.: saldo mínimo do dia) exigiria rever esta decisão.
- **Evidência:** `DailySummaryTests` (mesmo resultado em qualquer ordem); `ProjectionTests` (4 workers simultâneos, reconstrução do zero).

## Reavaliar quando

O consolidado precisar de métricas que dependem da ordem dos lançamentos ao longo do dia.
