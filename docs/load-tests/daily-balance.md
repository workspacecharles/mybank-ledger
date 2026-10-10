### Cenário `daily-balance` — APROVADO

- Execução: 2026-10-09 22:29 UTC; Microsoft Windows 10.0.26200; 6 CPUs lógicas; .NET 10.0.12
- Carga: 50 req/s alvo por 60 s; 3000 requisições despachadas em 60.0 s (**50.0 req/s** alcançados)
- Contas: 20; repetições idempotentes: 5% dos lançamentos; retries após timeout: 0
- Respostas: 3100; erros da aplicação (5xx, timeout, falha de transporte, 4xx inesperado): 0 (0.00%); rejeições de negócio (422, saldo insuficiente): 0

| Operação | Requisições | p50 | p95 | p99 | máx | Erros |
|---|---:|---:|---:|---:|---:|---:|
| daily-balance | 3000 | 8.6 ms | 12.3 ms | 13.1 ms | 16.3 ms | 0 |
| seed | 100 | 8.7 ms | 10.0 ms | 11.0 ms | 11.3 ms | 0 |
| **total** | 3100 | 8.7 ms | 12.2 ms | 13.1 ms | 16.3 ms | 0 |

| Verificação | Resultado | Detalhe |
|---|---|---|
| Taxa de erro da aplicação < 5% | OK | 0.00% |
| Latência p95 ≤ 500 ms (todas as operações) | OK | 12.2 ms |
| Saldo atual = saldo inicial + lançamentos aceitos | OK | 0 divergências |
| Zero lançamento confirmado perdido | OK | 0 perdidos |
| Zero lançamento duplicado | OK | razão = lançamentos aceitos (100/100); 0 repetições devolveram o lançamento original |
| Consolidação diária converge para o razão | OK | em 0.1 s após o fim da carga |
| Sem respostas inesperadas | OK | 0 anomalias |

Razão: 100 lançamentos confirmados ao cliente; 100 no banco; 0 confirmados sem resposta recebida pelo cliente.

#### Como reproduzir

> Seção incluída depois desta execução, que foi feita antes de a ferramenta gerar o passo a passo e registrar o commit. Os resultados acima não foram alterados. A execução usou o PostgreSQL 18 de um container local na porta 5432; o `docker compose` do projeto usa o PostgreSQL 17.

As latências variam com a máquina; as verificações da tabela acima devem passar em qualquer ambiente. Na raiz do repositório, com .NET SDK 10 e Docker em execução:

1. Suba o PostgreSQL: `docker compose up -d`.
2. Terminal 1, API: `dotnet run --project src/MyBank.Api -c Release`.
3. Terminal 2, Worker: `dotnet run --project src/MyBank.Worker -c Release`.
4. Terminal 3, carga: `dotnet run --project tools/MyBank.LoadTests -c Release -- --scenario daily-balance --rate 50 --duration 60 --accounts 20 --duplicates 0.05 --output docs/load-tests/daily-balance.md`.
5. A ferramenta termina com código 0 se todas as verificações passarem (1 se alguma falhar) e grava um relatório neste mesmo formato no arquivo do `--output`.
