# ADR-0003: Lançamento, saldo e evento na mesma transação (Transactional Outbox, sem broker)

**Status:** Aceito · **Data:** 2026-10-09

## Contexto

A consolidação diária precisa saber de cada lançamento confirmado, mas a demanda exige que ela possa ficar fora do ar sem impedir nem desfazer lançamentos. Gravar o lançamento e depois avisar a consolidação em um segundo passo cria uma janela de falha: o lançamento é confirmado e o aviso se perde (ou o contrário).

## Decisão

Usar o padrão **Transactional Outbox**: na **mesma transação** do lançamento, gravar também uma linha na tabela `outbox_messages` (o "bilhete" do evento). O worker lê os bilhetes pendentes direto do banco:

1. Pega até 500 pendentes com `FOR UPDATE SKIP LOCKED` (trava as linhas; outro worker pula as travadas).
2. Soma os lançamentos ao consolidado diário.
3. Marca os bilhetes como processados.
4. Faz o commit dos passos 2 e 3 juntos.

**Não há broker** (programa intermediário de mensagens, como Kafka ou RabbitMQ).

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Gravar no banco e publicar no broker em seguida (dual write) | Se a publicação falhar depois do commit, o evento se perde; se publicar antes, pode haver evento sem lançamento |
| Outbox + broker | O broker ainda precisaria de um publicador lendo a outbox, mais uma peça para operar. A entrega passaria a ser "pelo menos uma vez" (pode repetir), exigindo deduplicação no consumidor. Sem ganho na carga atual |
| Consolidar na mesma requisição | Falha da consolidação derrubaria o lançamento |

## Consequências

- **Positivas:** não existe lançamento sem evento nem evento sem lançamento. Como aplicar e marcar como processado é um único commit, cada evento é aplicado **exatamente uma vez**, sem tabela de deduplicação. Worker parado só acumula bilhetes. O atraso é medido com um `COUNT` (`/health/projection`). A consolidação pode ser reconstruída do zero a partir da outbox.
- **Negativas:** a leitura da outbox usa o mesmo banco dos lançamentos. A tabela cresce indefinidamente até existir um expurgo (evolução futura). Um único consumidor lógico: outros sistemas não recebem os eventos sem um publicador.
- **Evidência:** teste `C08`; `ProjectionTests` com 4 workers simultâneos; carga com worker parado (1.416 eventos acumulados e processados sem duplicar).

## Reavaliar quando

Surgirem vários consumidores para os mesmos eventos (fan-out) ou a consolidação for para outro banco. Nesse caso, adicionar um publicador outbox → broker; a outbox continua sendo a origem.
