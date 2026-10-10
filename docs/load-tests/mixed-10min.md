### Cenário `mixed` — APROVADO

- Execução: 2026-10-09 22:42 UTC; Microsoft Windows 10.0.26200; 6 CPUs lógicas; .NET 10.0.12
- Carga: 50 req/s alvo por 600 s; 30000 requisições despachadas em 600.0 s (**50.0 req/s** alcançados)
- Contas: 20; repetições idempotentes: 5% dos lançamentos; retries após timeout: 0
- Respostas: 30000; erros da aplicação (5xx, timeout, falha de transporte, 4xx inesperado): 0 (0.00%); rejeições de negócio (422, saldo insuficiente): 0

| Operação | Requisições | p50 | p95 | p99 | máx | Erros |
|---|---:|---:|---:|---:|---:|---:|
| balance | 8969 | 2.8 ms | 5.6 ms | 7.1 ms | 58.6 ms | 0 |
| daily-balance | 6026 | 6.1 ms | 12.7 ms | 14.1 ms | 86.5 ms | 0 |
| entry | 14156 | 13.4 ms | 27.3 ms | 275.8 ms | 2000.2 ms | 0 |
| entry-replay | 849 | 5.5 ms | 11.8 ms | 35.0 ms | 866.0 ms | 0 |
| **total** | 30000 | 8.8 ms | 23.6 ms | 55.2 ms | 2000.2 ms | 0 |

| Verificação | Resultado | Detalhe |
|---|---|---|
| Taxa de erro da aplicação < 5% | OK | 0.00% |
| Latência p95 ≤ 500 ms (todas as operações) | OK | 23.6 ms |
| Saldo atual = saldo inicial + lançamentos aceitos | OK | 0 divergências |
| Zero lançamento confirmado perdido | OK | 0 perdidos |
| Zero lançamento duplicado | OK | razão = lançamentos aceitos (14156/14156); 849 repetições devolveram o lançamento original |
| Consolidação diária converge para o razão | OK | em 0.6 s após o fim da carga |
| Sem respostas inesperadas | OK | 0 anomalias |

Razão: 14156 lançamentos confirmados ao cliente; 14156 no banco; 0 confirmados sem resposta recebida pelo cliente.
