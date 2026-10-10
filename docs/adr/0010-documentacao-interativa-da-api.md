# ADR-0010: Documentação interativa da API com Scalar e Swagger UI

**Status:** Aceito · **Data:** 2026-10-10

## Contexto

Quem avalia ou evolui a API precisa ver as rotas e testá-las sem montar requisições à mão. A API já gerava o contrato OpenAPI (descrição padrão dos endereços da API) com o pacote nativo do ASP.NET Core, em `/openapi/v1.json`, mas não havia interface para lê-lo no navegador. Desde o .NET 9, os modelos de projeto da Microsoft não trazem mais o Swashbuckle (pacote que gerava o contrato e o Swagger UI): o contrato passou a ser do framework, e a interface ficou a critério de cada projeto. As rotas de conta exigem token JWT, então a interface precisa permitir informá-lo.

## Decisão

- Manter o contrato gerado pelo **OpenAPI nativo** (`Microsoft.AspNetCore.OpenApi`), que é a fonte única.
- Publicar **duas interfaces** que leem esse mesmo contrato:
  - **Scalar** em `/scalar` (pacote `Scalar.AspNetCore`): a interface citada na documentação atual do .NET para o OpenAPI nativo.
  - **Swagger UI** em `/swagger` (pacote `Swashbuckle.AspNetCore.SwaggerUI`, só a interface, sem o gerador): a mais conhecida.
- Declarar no contrato a autenticação **Bearer** (token no header `Authorization`), aplicada só às rotas que exigem autorização. As interfaces mostram o cadeado nessas rotas e o campo para informar o token.
- Contrato e interfaces existem **somente em Development**, como o `POST /dev/token`.

O código está em [`OpenApiExtensions`](../../src/MyBank.Api/Extensions/OpenApiExtensions.cs), chamado pelo `Program.cs`.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Só o contrato JSON, sem interface | Obriga quem avalia a usar outra ferramenta para ler e testar a API |
| Swashbuckle completo (gerador + interface) | Seriam dois geradores de contrato. O nativo já gera e é o caminho atual da Microsoft |
| Só uma das interfaces | Funcionaria. As duas custam pouco (dois pacotes e poucas linhas, lendo o mesmo contrato), e cada pessoa usa a que já conhece |
| Publicar também em produção | Expõe o mapa da API sem necessidade. Em produção, o contrato pode ser distribuído pelo pipeline de deploy, se preciso |

## Consequências

- **Positivas:** a API pode ser explorada e testada no navegador sem instalar nada. O contrato continua único, então as duas interfaces nunca divergem. O cadeado mostra quais rotas exigem token.
- **Negativas:** duas dependências a mais, só usadas em Development. O transformador do esquema Bearer usa a API do pacote OpenAPI do .NET 10 e pode precisar de ajuste em atualizações futuras.
- **Evidência:** `/openapi/v1.json`, `/scalar` e `/swagger` respondem em Development. O contrato declara o esquema Bearer nas 5 rotas de conta, e `/dev/token` e `/health` aparecem sem cadeado.

## Reavaliar quando

A API passar a ter consumidores externos que precisem do contrato fora de Development, ou se uma das interfaces deixar de ser mantida.
