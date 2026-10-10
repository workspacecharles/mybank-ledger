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
