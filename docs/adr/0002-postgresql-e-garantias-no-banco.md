# ADR-0002: PostgreSQL como fonte de verdade, com as garantias no banco

**Status:** Aceito · **Data:** 2026-10-09

## Contexto

Dados financeiros não toleram inconsistência: saldo negativo indevido, lançamento duplicado ou lançamento alterado depois de confirmado geram impacto direto ao cliente. Regras de consistência implementadas só no código dependem de todo caminho do código estar certo, inclusive sob concorrência.

## Decisão

Usar o **PostgreSQL** como única fonte de verdade e colocar as garantias críticas **no próprio banco**, onde o código não consegue contorná-las:

- **Transação** (pacote de gravações que entra inteiro ou não entra nada) para lançamento, saldo e evento.
- **Trava de linha** (`SELECT ... FOR UPDATE`) na conta ([ADR-0004](./0004-trava-pessimista-por-conta.md)).
- **Índices únicos:** `(account_id, sequence)` e `(account_id, idempotency_key)`.
- **Check constraints** (regras de verificação): `balance >= 0`, `amount > 0`, `balance_after >= 0`.
- **Trigger** (gatilho que o banco executa sozinho) que recusa `UPDATE` e `DELETE` em `ledger_entries`: o razão é imutável.
- **Valores em `numeric(19,2)`** (decimal exato), nunca ponto flutuante.
- **Relógio do banco** (`clock_timestamp()`) para o instante do lançamento, nunca o do cliente.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| SQL Server | Também atenderia. O PostgreSQL foi escolhido por ter nativamente `SKIP LOCKED` e `ON CONFLICT` (usados na consolidação), ser código aberto e rodar leve no Docker |
| Banco NoSQL (orientado a documentos) | Transações entre várias entidades, travas de linha e índices únicos compostos são mais fracos ou mais caros. O problema é relacional e transacional |
| Garantias só no código | Uma regra esquecida em um caminho, ou uma condição de corrida (dois pedidos ao mesmo tempo), passaria despercebida |

## Consequências

- **Positivas:** as garantias valem para qualquer código que acesse o banco, inclusive scripts manuais. Todas são testáveis contra um PostgreSQL real (Testcontainers).
- **Negativas:** acoplamento ao PostgreSQL (`FOR UPDATE`, `SKIP LOCKED`, `ON CONFLICT`, trigger em PL/pgSQL). Trocar de banco exigiria reescrever essas partes. O banco é o ponto central: se ele cair, nada é confirmado (a API responde `503` e o cliente repete com a mesma chave).

## Reavaliar quando

Houver requisito de troca de banco ou de escala que um único PostgreSQL (com réplica de leitura e particionamento) não atenda.
