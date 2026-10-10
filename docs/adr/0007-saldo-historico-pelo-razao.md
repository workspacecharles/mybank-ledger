# ADR-0007: Saldo histórico lido do razão, não do consolidado

**Status:** Aceito · **Data:** 2026-10-09

## Contexto

A demanda pede o saldo da conta em um instante passado, refletindo **exatamente** os lançamentos registrados até ali, **independente do atraso da consolidação diária**. O consolidado é eventualmente consistente (pode estar atrasado) e tem granularidade de dia.

## Decisão

Cada lançamento guarda o saldo da conta logo depois dele (`balance_after`). O saldo no instante `T` é o `balance_after` do último lançamento com `recorded_at <= T`, ordenado por `(recorded_at, sequence)`. Sem lançamento até `T`, vale o saldo inicial da conta. O índice `(account_id, recorded_at, sequence)` faz a consulta ler uma única linha. O parâmetro `T` exige fuso explícito (`Z` ou `-03:00`), para não ser ambíguo.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Usar o consolidado diário | Pode estar atrasado e não tem precisão de instante, só de dia |
| Somar todos os lançamentos até `T` | Custo cresce com o histórico da conta |
| Snapshots periódicos de saldo (fotos do saldo de tempos em tempos) | Mais uma estrutura para manter; o `balance_after` já é um snapshot por lançamento |

## Consequências

- **Positivas:** resposta exata e rápida, sem depender do worker. O `balance_after` também serve de auditoria (cada linha do razão mostra o saldo resultante).
- **Negativas:** o `balance_after` só é correto porque os lançamentos da conta são serializados ([ADR-0004](./0004-trava-pessimista-por-conta.md)) e o razão é imutável ([ADR-0002](./0002-postgresql-e-garantias-no-banco.md)). Lançamentos retroativos (com data no passado) não são suportados: o instante é sempre o do registro.
- **Evidência:** `C09` (antes, no instante e depois de cada lançamento).

## Reavaliar quando

O negócio exigir lançamentos com data contábil diferente da data de registro (lançamento retroativo).
