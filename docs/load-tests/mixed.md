### Cenário `mixed` — APROVADO

- Execução: 2026-10-09 22:27 UTC; Microsoft Windows 10.0.26200; 6 CPUs lógicas; .NET 10.0.12
- Carga: 50 req/s alvo por 60 s; 3000 requisições despachadas em 60.0 s (**50.0 req/s** alcançados)
- Contas: 20; repetições idempotentes: 5% dos lançamentos; retries após timeout: 0
- Respostas: 3000; erros da aplicação (5xx, timeout, falha de transporte, 4xx inesperado): 0 (0.00%); rejeições de negócio (422, saldo insuficiente): 0

| Operação | Requisições | p50 | p95 | p99 | máx | Erros |
|---|---:|---:|---:|---:|---:|---:|
| balance | 908 | 2.8 ms | 5.5 ms | 7.5 ms | 280.2 ms | 0 |
| daily-balance | 601 | 6.4 ms | 12.6 ms | 15.2 ms | 267.7 ms | 0 |
| entry | 1418 | 13.4 ms | 26.8 ms | 87.2 ms | 418.7 ms | 0 |
| entry-replay | 73 | 5.3 ms | 11.6 ms | 13.5 ms | 13.5 ms | 0 |
| **total** | 3000 | 8.9 ms | 24.0 ms | 30.8 ms | 418.7 ms | 0 |

| Verificação | Resultado | Detalhe |
|---|---|---|
| Taxa de erro da aplicação < 5% | OK | 0.00% |
| Latência p95 ≤ 500 ms (todas as operações) | OK | 24.0 ms |
| Saldo atual = saldo inicial + lançamentos aceitos | OK | 0 divergências |
| Zero lançamento confirmado perdido | OK | 0 perdidos |
| Zero lançamento duplicado | OK | razão = lançamentos aceitos (1418/1418); 73 repetições devolveram o lançamento original |
| Consolidação diária converge para o razão | OK | em 0.7 s após o fim da carga |
| Sem respostas inesperadas | OK | 0 anomalias |

Razão: 1418 lançamentos confirmados ao cliente; 1418 no banco; 0 confirmados sem resposta recebida pelo cliente.

#### Como reproduzir

> Seção incluída depois desta execução, que foi feita antes de a ferramenta gerar o passo a passo e registrar o commit. Os resultados acima não foram alterados. A execução usou o PostgreSQL 18 de um container local na porta 5432; o `docker compose` do projeto usa o PostgreSQL 17.

As latências variam com a máquina; as verificações da tabela acima devem passar em qualquer ambiente. Na raiz do repositório, com .NET SDK 10 e Docker em execução:

1. Suba o PostgreSQL: `docker compose up -d`.
2. Terminal 1, API: `dotnet run --project src/MyBank.Api -c Release`.
3. Terminal 2, Worker: `dotnet run --project src/MyBank.Worker -c Release`.
4. Terminal 3, carga: `dotnet run --project tools/MyBank.LoadTests -c Release -- --scenario mixed --rate 50 --duration 60 --accounts 20 --duplicates 0.05 --output docs/load-tests/mixed.md`.
5. A ferramenta termina com código 0 se todas as verificações passarem (1 se alguma falhar) e grava um relatório neste mesmo formato no arquivo do `--output`.
