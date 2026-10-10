### Cenário `mixed` — APROVADO

- Execução: 2026-10-09 22:31 UTC; Microsoft Windows 10.0.26200; 6 CPUs lógicas; .NET 10.0.12
- Carga: 50 req/s alvo por 60 s; 3000 requisições despachadas em 60.0 s (**50.0 req/s** alcançados)
- Contas: 20; repetições idempotentes: 5% dos lançamentos; retries após timeout: 0
- Respostas: 3000; erros da aplicação (5xx, timeout, falha de transporte, 4xx inesperado): 0 (0.00%); rejeições de negócio (422, saldo insuficiente): 0

| Operação | Requisições | p50 | p95 | p99 | máx | Erros |
|---|---:|---:|---:|---:|---:|---:|
| balance | 931 | 2.7 ms | 5.4 ms | 8.4 ms | 22.5 ms | 0 |
| daily-balance | 567 | 6.0 ms | 12.4 ms | 15.7 ms | 53.9 ms | 0 |
| entry | 1416 | 13.3 ms | 26.4 ms | 30.4 ms | 61.5 ms | 0 |
| entry-replay | 86 | 6.0 ms | 11.7 ms | 13.7 ms | 13.7 ms | 0 |
| **total** | 3000 | 8.7 ms | 22.1 ms | 28.7 ms | 61.5 ms | 0 |

| Verificação | Resultado | Detalhe |
|---|---|---|
| Taxa de erro da aplicação < 5% | OK | 0.00% |
| Latência p95 ≤ 500 ms (todas as operações) | OK | 22.1 ms |
| Saldo atual = saldo inicial + lançamentos aceitos | OK | 0 divergências |
| Zero lançamento confirmado perdido | OK | 0 perdidos |
| Zero lançamento duplicado | OK | razão = lançamentos aceitos (1416/1416); 86 repetições devolveram o lançamento original |
| Consolidação diária converge para o razão | OK | em 51.0 s após o fim da carga |
| Sem respostas inesperadas | OK | 0 anomalias |

Razão: 1416 lançamentos confirmados ao cliente; 1416 no banco; 0 confirmados sem resposta recebida pelo cliente.

#### Como reproduzir

> Seção incluída depois desta execução, que foi feita antes de a ferramenta gerar o passo a passo e registrar o commit. Os resultados acima não foram alterados. A execução usou o PostgreSQL 18 de um container local na porta 5432; o `docker compose` do projeto usa o PostgreSQL 17.
>
> Esta execução também é anterior à opção `--worker stopped`: o worker ficou desligado durante a carga e foi religado à mão. O comando abaixo usa a opção, que confere sozinha que a consolidação ficou parada e avisa quando religar o worker.

As latências variam com a máquina; as verificações da tabela acima devem passar em qualquer ambiente. Na raiz do repositório, com .NET SDK 10 e Docker em execução:

1. Suba o PostgreSQL: `docker compose up -d`.
2. Terminal 1, API: `dotnet run --project src/MyBank.Api -c Release`.
3. Terminal 2, Worker: **não inicie ainda**. Este cenário mede a API com a consolidação parada.
4. Terminal 3, carga: `dotnet run --project tools/MyBank.LoadTests -c Release -- --scenario mixed --rate 50 --duration 60 --accounts 20 --duplicates 0.05 --worker stopped --output docs/load-tests/mixed-worker-stopped.md`.
5. Quando a ferramenta mostrar "Inicie o worker agora", rode no terminal 2 `dotnet run --project src/MyBank.Worker -c Release`. A ferramenta espera até 5 min a consolidação alcançar o razão.
6. A ferramenta termina com código 0 se todas as verificações passarem (1 se alguma falhar) e grava um relatório neste mesmo formato no arquivo do `--output`.
