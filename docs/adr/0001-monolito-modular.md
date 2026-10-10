# ADR-0001: Monólito modular com o worker em processo separado

**Status:** Aceito · **Data:** 2026-10-09

## Contexto

O sistema atual do banco é instável em horários de pico e difícil de manter. A demanda pede que o registro de lançamentos continue funcionando **mesmo com a consolidação diária fora do ar**, e que a consolidação suporte picos de 50 requisições por segundo. A equipe e o domínio são pequenos: cinco endpoints, um único contexto de negócio (contas e lançamentos).

## Decisão

Um **monólito modular** (um único sistema e um único banco, dividido em partes bem separadas), com dois processos:

- **API** (`MyBank.Api`): recebe os pedidos HTTP e registra os lançamentos.
- **Worker** (`MyBank.Worker`): processo separado que monta a consolidação diária em segundo plano.

Os dois usam o mesmo banco PostgreSQL e o mesmo código de negócio (`MyBank.Core` e `MyBank.Domain`), mas **não conversam entre si**: a API deixa eventos na outbox ([ADR-0003](./0003-outbox-transacional-sem-broker.md)) e o worker os consome.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Um único processo (consolidar na própria requisição) | Uma falha na consolidação derrubaria o lançamento, o que contraria o requisito principal |
| Microsserviços (um serviço para lançamentos, outro para consolidação, cada um com seu banco) | Exige broker, consumidor que trata mensagens repetidas, contrato de evento versionado e rastreamento distribuído. Nenhuma necessidade medida justifica esse custo: 50 req/s cabem com folga em um processo |

## Consequências

- **Positivas:** o worker pode parar, reiniciar ou ter várias instâncias sem afetar a API. Uma transação local garante a consistência, sem coordenação entre bancos. Um único deploy e um único banco para operar.
- **Negativas:** API e worker compartilham o banco, então uma carga pesada na consolidação disputa recursos com os lançamentos. O deploy do código de negócio é conjunto.
- **Evidência:** no teste de carga com o worker parado, os lançamentos seguiram a 50 req/s com 0% de erro ([Load-Tests.md](../Load-Tests.md)).

## Reavaliar quando

Equipes diferentes precisarem publicar em ritmos diferentes, ou a consolidação precisar de escala ou banco próprios. A primeira fronteira de separação é o worker; os ganhos e custos estão em [Architecture.md, seção 8](../Architecture.md#worker-como-serviço-independente).
