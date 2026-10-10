# ADR-0004: Trava pessimista por conta para débitos concorrentes

**Status:** Aceito · **Data:** 2026-10-09

## Contexto

Dois débitos simultâneos na mesma conta podem ler o mesmo saldo, aprovar os dois e deixar a conta negativa (condição de corrida). A demanda exige que débitos concorrentes sejam serializados (postos em fila) por conta e nunca ultrapassem o saldo. A mesma situação vale para duas repetições simultâneas da mesma chave de idempotência.

## Decisão

Toda escrita em uma conta começa com `SELECT ... FOR UPDATE` na linha da conta, dentro da transação. É uma **trava pessimista** (reserva a linha antes de alterar): pedidos da mesma conta esperam em fila até o commit do anterior; pedidos de contas diferentes não se afetam. O `CHECK balance >= 0` no banco é a última barreira.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Controle otimista (cada linha tem uma versão; quem gravar com versão velha falha e tenta de novo) | Sob disputa na mesma conta, gera falhas e exige retry no cliente ou no servidor. Mais código e mais latência justamente no pior caso |
| Nível de isolamento `SERIALIZABLE` | Gera erros de serialização que também exigem retry, e afeta todas as consultas da transação |
| Trava em memória na aplicação | Não funciona com mais de uma instância da API |
| Advisory lock (trava lógica do PostgreSQL) | Funciona, mas é uma segunda forma de travar o que a linha da conta já representa |

## Consequências

- **Positivas:** correção garantida pelo banco, inclusive com várias instâncias da API. Também resolve repetições simultâneas da mesma chave: a segunda só lê depois que a primeira confirmou ([ADR-0005](./0005-idempotencia-no-lancamento.md)). A ordem dos instantes acompanha a sequência da conta.
- **Negativas:** a vazão de uma única conta é limitada pela duração da transação. Uma transação lenta segura a fila daquela conta.
- **Evidência:** `C05` (50 débitos simultâneos sobre saldo para 20: exatamente 20 aceitos; sem o `FOR UPDATE`, o teste falha). Cenário de carga `hot-account` (todos os lançamentos na mesma conta): p95 de 17 ms.

## Reavaliar quando

Contenção (disputa pela mesma linha) medida em produção se tornar gargalo, por exemplo contas com volume muito alto. Opções: controle otimista com retry, ou subcontas.
