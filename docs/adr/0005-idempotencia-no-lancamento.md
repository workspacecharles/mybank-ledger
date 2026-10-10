# ADR-0005: Idempotência pela chave guardada no próprio lançamento

**Status:** Aceito · **Data:** 2026-10-09

## Contexto

Em caso de timeout (tempo esgotado) ou resposta perdida, o cliente não sabe se o lançamento entrou e precisa poder repetir o pedido com segurança. A demanda exige: mesma chave e mesmo conteúdo produzem no máximo um lançamento; mesma chave com conteúdo diferente é rejeitada sem alterar dados.

## Decisão

- Header `Idempotency-Key` **obrigatório** em `POST /entries` (1 a 128 caracteres).
- A chave é gravada **no próprio lançamento**, com índice único `(account_id, idempotency_key)`.
- Na repetição (já com a conta travada, [ADR-0004](./0004-trava-pessimista-por-conta.md)), o sistema busca o lançamento pela chave e compara tipo, valor e descrição:
  - **igual:** devolve `201` com o lançamento original e o header `Idempotent-Replayed: true`;
  - **diferente:** `409 idempotency_key_reused`, sem gravar nada.
- Só sucessos ficam registrados. Um pedido recusado (ex.: `422` saldo insuficiente) não grava a chave, então pode ser repetido e reavaliado.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Tabela separada de chaves com a resposta guardada | Mais uma tabela e mais uma gravação por pedido. A resposta pode ser reconstruída do próprio lançamento |
| Chave opcional | Um cliente que não envia a chave fica sem proteção contra duplicidade, justamente no cenário que a demanda descreve |
| Chave global (não por conta) | Clientes diferentes poderiam colidir sem querer. Por conta, a chave só precisa ser única dentro da conta |

## Consequências

- **Positivas:** nenhuma tabela extra. A garantia final é do banco (índice único). Repetições simultâneas são resolvidas pela trava da conta.
- **Negativas:** as chaves ficam guardadas para sempre junto do lançamento (não expiram). Repetir um pedido recusado reavalia a regra, então o resultado pode mudar se o saldo mudou entre as tentativas (comportamento intencional: só sucessos são memorizados).
- **Evidência:** `C06` (inclusive 10 repetições simultâneas), `C07`; no teste de carga, 5% dos lançamentos são repetidos e todos devolveram o lançamento original.

## Reavaliar quando

Houver necessidade de expirar chaves (ex.: volume de armazenamento) ou de idempotência em outros endpoints.
