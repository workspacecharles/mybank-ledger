# ADR-0008: Domínio isolado, sem interfaces de repositório

**Status:** Aceito · **Data:** 2026-10-09

## Contexto

Uma primeira versão tinha camadas Domain/Application/Infrastructure, interfaces de repositório e unit of work (abstrações sobre o acesso ao banco) e `Result<T>`. Funcionava, mas eram cerca de 80 arquivos para cinco endpoints, e as camadas só repassavam chamadas. Ao mesmo tempo, as regras de negócio precisam ser testáveis sem banco, e a dependência entre partes do código precisa ser controlada.

## Decisão

- **Separar o domínio:** entidades, enums, regras (`LedgerRules`) e erros de negócio (`LedgerException`) ficam no projeto `MyBank.Domain`, **sem nenhum pacote nem referência**. O compilador impede que uma regra dependa de banco, HTTP ou framework. As regras são funções puras, testadas sem banco.
- **Não criar interfaces de repositório nem de unit of work:** o `LedgerService` (`MyBank.Core`) usa o `MyBankDbContext` do EF Core diretamente.

Dependências entre projetos: `Api` → `Core` → `Domain`; `Worker` → `Core` → `Domain`.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Clean Architecture completa (Application e Infrastructure separadas, com interfaces de repositório) | O `DbContext` já implementa Repository (os `DbSet`) e Unit of Work (o `SaveChanges`); a [documentação da Microsoft](https://learn.microsoft.com/dotnet/api/microsoft.entityframeworkcore.dbcontext) o descreve assim. Uma interface por cima seria uma segunda abstração da mesma coisa |
| Tudo em um projeto só | Nada impediria uma regra de negócio de depender do banco ou do HTTP |

## Consequências

- **Positivas:** o que garante a consistência (`FOR UPDATE`, `clock_timestamp()`, `ON CONFLICT`) fica visível no único lugar em que a transação é montada, em vez de escondido atrás de um nome genérico. Menos arquivos e menos indireção. Regras testadas puras (58 testes unitários, sem Docker) e persistência testada contra PostgreSQL real.
- **Negativas:** o `Core` depende do EF Core e do PostgreSQL. Os casos de uso (`LedgerService`) não podem ser testados sem banco; são cobertos por testes de integração. Um repositório falso não teria trava nem transação de verdade, então testaria justamente o que não importa.

## Reavaliar quando

Surgir um segundo meio de armazenamento, a necessidade de trocar de banco, ou casos de uso com lógica de orquestração que valha testar sem banco. O primeiro passo seria uma interface específica para a operação de lançamento (travar conta, buscar por chave, gravar), não um repositório genérico.
