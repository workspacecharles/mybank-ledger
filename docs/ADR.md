# Registros de decisão de arquitetura (ADRs)

Um ADR (Architecture Decision Record, registro de decisão de arquitetura) documenta **uma** decisão importante: o contexto em que foi tomada, o que foi decidido, as alternativas descartadas e as consequências, boas e ruins. Serve para que outra pessoa do time entenda **por que** o sistema é como é, e saiba quando a decisão deve ser revista.

Formato usado em cada arquivo: **Status**, **Contexto**, **Decisão**, **Alternativas consideradas**, **Consequências** e **Reavaliar quando**. Uma decisão aceita não é editada: se mudar, cria-se um ADR novo que a substitui, e o antigo passa a "Substituído por ADR-NNNN".

| ADR | Decisão | Status |
|---|---|---|
| [0001](./adr/0001-monolito-modular.md) | Monólito modular com o worker em processo separado | Aceito |
| [0002](./adr/0002-postgresql-e-garantias-no-banco.md) | PostgreSQL como fonte de verdade, com as garantias no banco | Aceito |
| [0003](./adr/0003-outbox-transacional-sem-broker.md) | Lançamento, saldo e evento na mesma transação (Transactional Outbox, sem broker) | Aceito |
| [0004](./adr/0004-trava-pessimista-por-conta.md) | Trava pessimista por conta para débitos concorrentes | Aceito |
| [0005](./adr/0005-idempotencia-no-lancamento.md) | Idempotência pela chave guardada no próprio lançamento | Aceito |
| [0006](./adr/0006-consolidacao-independente-de-ordem.md) | Consolidação diária independente da ordem dos eventos | Aceito |
| [0007](./adr/0007-saldo-historico-pelo-razao.md) | Saldo histórico lido do razão, não do consolidado | Aceito |
| [0008](./adr/0008-dominio-isolado-sem-repositorio.md) | Domínio isolado, sem interfaces de repositório | Aceito |
| [0009](./adr/0009-autenticacao-jwt-autorizacao-por-titular.md) | Autenticação JWT/OIDC e autorização por titular da conta | Aceito |

A visão completa (fluxos, segurança, operação, requisitos não funcionais) está em [Architecture.md](./Architecture.md).
