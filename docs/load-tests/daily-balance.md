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
