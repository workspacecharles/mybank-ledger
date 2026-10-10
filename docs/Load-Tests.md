# Teste de carga

Evidência do requisito **50 requisições por segundo (req/s) com no máximo 5% de perda**, da meta proposta de p95 ≤ 500 ms e de **zero lançamento confirmado perdido ou duplicado**.

Como ler as medidas de tempo: **p50**, **p95** e **p99** (percentis) são os tempos em que 50%, 95% e 99% das respostas ficaram prontas. Exemplo: p95 = 24 ms significa que 95 de cada 100 respostas levaram até 24 milissegundos. **máx** é a resposta mais lenta.

## 1. Como "5% de perda" foi interpretado

A demanda não define "perda". Adotou-se:

- **Taxa de erro da aplicação** = (erros 5xx do servidor + timeouts, ou seja, sem resposta em 10 s + falhas de conexão + respostas 4xx inesperadas) ÷ total de respostas na janela. Limite: **< 5%**.
- `422 insufficient_funds` (saldo insuficiente) é uma resposta correta de negócio e **não** conta como erro.
- **Lançamento confirmado perdido ou duplicado: tolerância zero**, conferida no banco ao final de cada execução.

Essa interpretação precisa ser validada com o negócio.

## 2. Método

Ferramenta própria em [tools/MyBank.LoadTests](../tools/MyBank.LoadTests), em C#, que roda só com o SDK (kit de desenvolvimento) do .NET:

- **Taxa constante:** uma requisição a cada 20 ms, independentemente do tempo de resposta das anteriores. Se a API ficar lenta, a carga oferecida não diminui, como acontece em um pico real.
- **Cenários:**
  - `mixed` (misto): 50% lançamentos em 20 contas, 30% saldo atual, 20% consolidado diário.
  - `hot-account` (conta concorrida): 100% lançamentos na **mesma conta**, o pior caso para a trava de linha (`FOR UPDATE`), que faz os lançamentos da mesma conta esperarem em fila.
  - `daily-balance`: 100% consultas ao consolidado diário, que é o que as diretrizes pedem a 50 req/s.
- **Idempotência sob carga:** 5% dos lançamentos são reenviados com a mesma `Idempotency-Key` (número do pedido) e o mesmo corpo, e a ferramenta exige receber o **mesmo** lançamento de volta.
- **Verificação final:**
  1. Saldo de cada conta (via API) = saldo inicial + lançamentos aceitos.
  2. No banco: todo lançamento confirmado ao cliente existe, e não há nenhum além deles.
  3. O consolidado fica em dia e o último fechamento é igual ao saldo.

Para reproduzir (com API e worker rodando):

```powershell
dotnet run --project tools/MyBank.LoadTests -c Release -- --scenario mixed --duration 60 --output docs/load-tests/mixed.md
```

Opções: `--scenario`, `--rate` (req/s), `--duration` (segundos), `--accounts` (contas), `--duplicates` (fração de repetições), `--worker running|stopped` (com `stopped`, a ferramenta confere que a consolidação ficou parada durante a carga e avisa quando iniciar o worker), `--db <connection string | none>` (banco para conferir o razão), `--base-url` (endereço da API). O programa termina com código `0` quando todas as verificações passam.

**Cada relatório traz o próprio passo a passo** (seção "Como reproduzir", gerada pela ferramenta): o comando exato daquela execução e a versão do código (commit). Os relatórios são o registro de uma execução, não uma prova por si só: a prova é repetir o passo a passo e obter as mesmas verificações aprovadas.

## 3. Ambiente

Uma única máquina executando **tudo ao mesmo tempo**: gerador de carga, API e worker (compilação Release, uma instância de cada, sem proxy) e PostgreSQL 18 no Docker Desktop/WSL2 (Linux dentro do Windows). Windows 11, 6 CPUs lógicas, .NET 10.0.12. Sem cache.

## 4. Resultados (60 s por cenário)

Relatórios completos em [docs/load-tests/](./load-tests/).

| Cenário | Vazão | Erros | p50 | p95 | p99 | Perdidos | Duplicados | Consolidado em dia |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| [`mixed`](./load-tests/mixed.md) | 50,0 req/s | 0 (0,00%) | 8,9 ms | 24,0 ms | 30,8 ms | 0 | 0 | 0,7 s após a carga |
| [`hot-account`](./load-tests/hot-account.md) | 50,0 req/s | 0 (0,00%) | 13,1 ms | 16,9 ms | 26,5 ms | 0 | 0 | imediato |
| [`daily-balance`](./load-tests/daily-balance.md) | 50,0 req/s | 0 (0,00%) | 8,7 ms | 12,2 ms | 13,1 ms | 0 | 0 | 0,1 s |
| [`mixed` com **worker parado**](./load-tests/mixed-worker-stopped.md) | 50,0 req/s | 0 (0,00%) | 8,7 ms | 22,1 ms | 28,7 ms | 0 | 0 | ver abaixo |

No `mixed`, o p95 por operação foi: saldo 5,5 ms, consolidado 12,6 ms, lançamento 26,8 ms e repetição idempotente 11,6 ms.

### Leitura

- **50 req/s com 0% de erro em todos os cenários.** O limite de 5% não chegou a ser tocado.
- **Zero perdido e zero duplicado:** o número de lançamentos no banco é exatamente o número confirmado ao cliente, e todas as repetições devolveram o lançamento original.
- **Conta concorrida:** com todos os lançamentos na fila da mesma trava, o p95 foi de 17 ms. Cada transação segura a trava por poucos milissegundos.
- **Worker parado (critério de aceite 8):** (execução anterior à opção `--worker stopped`, com o worker religado à mão) os lançamentos continuaram sendo aceitos com a mesma velocidade e 0% de erro. Ao fim da janela, `/health/projection` estava `Degraded` (degradado: 1.416 eventos pendentes, o mais antigo com 60 s) e `/health/ready` continuava `Healthy` (saudável). Religado, o worker processou o backlog (fila acumulada) em 3 lotes, sem duplicar nada. Os 51 s de convergência registrados no relatório incluem o tempo para religar o worker manualmente com `dotnet run` (compilação e inicialização), não só o processamento.
- **Primeiras requisições:** os máximos de 270–420 ms no `mixed` são do aquecimento logo depois de subir a API: JIT (compilação do código na primeira execução), criação das conexões com o banco e caches frios. Os p99 ficaram abaixo de 90 ms.
- **Sem cache** (cópia rápida em memória): consultas de saldo e de consolidado ficam abaixo de 15 ms no p95. Não há motivo para cache, e o saldo que autoriza débito não pode ser cacheado.

## 5. Janela sustentada de 10 minutos

Cenário `mixed` por 600 s ([relatório](./load-tests/mixed-10min.md)):

| Operação | Requisições | p50 | p95 | p99 | máx | Erros |
|---|---:|---:|---:|---:|---:|---:|
| saldo | 8.969 | 2,8 ms | 5,6 ms | 7,1 ms | 58,6 ms | 0 |
| consolidado | 6.026 | 6,1 ms | 12,7 ms | 14,1 ms | 86,5 ms | 0 |
| lançamento | 14.156 | 13,4 ms | 27,3 ms | 275,8 ms | 2.000,2 ms | 0 |
| repetição idempotente | 849 | 5,5 ms | 11,8 ms | 35,0 ms | 866,0 ms | 0 |
| **total** | **30.000** | 8,8 ms | **23,6 ms** | 55,2 ms | 2.000,2 ms | **0 (0,00%)** |

- 50,0 req/s sustentados por 10 minutos. 14.156 lançamentos confirmados e exatamente 14.156 no banco. As 849 repetições devolveram o lançamento original. O consolidado ficou em dia 0,6 s após o fim.
- **Cauda de latência** (as poucas respostas mais lentas): só as escritas (lançamentos e repetições) tiveram p99 e máximo altos. As leituras ficaram abaixo de 90 ms no máximo. A causa **não foi investigada** nesta execução. A hipótese é pausa de disco na gravação do WAL (o diário de alterações que o PostgreSQL grava no disco a cada commit), já que o disco virtual do Docker Desktop/WSL2 é compartilhado com o resto da máquina, mas isso não foi confirmado. Nenhuma requisição falhou ou chegou ao timeout de 10 s. Em ambiente dedicado, o caminho seria olhar `pg_stat_wal` e `pg_stat_activity` (visões de diagnóstico do PostgreSQL) e as métricas de disco antes de mexer na aplicação.

## 6. Limitações

- Gerador, aplicação e banco dividem a mesma máquina, sem latência de rede real. Os números mostram folga, não capacidade de produção.
- Uma instância de cada processo. Várias instâncias são suportadas pelo desenho (trava de linha, `SKIP LOCKED`), mas a escala horizontal (mais cópias do programa rodando juntas) não foi medida.
- Volume de dados pequeno (milhares de lançamentos). Com milhões, o saldo histórico continua usando o índice `(account_id, recorded_at, sequence)`, mas isso precisa ser medido com volume real.
- O teste de carga não roda no CI (integração contínua), porque precisa de API e worker em execução. É reproduzível localmente com os comandos acima.
