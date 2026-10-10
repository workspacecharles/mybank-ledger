# ADR-0009: Autenticação JWT/OIDC e autorização por titular da conta

**Status:** Aceito · **Data:** 2026-10-09

## Contexto

A demanda exige autenticação e **autorização por conta**: um cliente só pode ver e movimentar as próprias contas. O sistema é uma API consumida por aplicações (app do banco, outros sistemas internos), sem tela de login própria. Em ambiente local, é preciso testar sem um provedor de identidade real.

## Decisão

- **Autenticação:** JWT Bearer (token assinado enviado no header `Authorization`), validando emissor, audiência, expiração e algoritmo.
  - **Produção:** tokens emitidos por um provedor OIDC (OpenID Connect, padrão de login como "entrar com Google/Microsoft"), configurado em `Auth:Authority`. A API só valida.
  - **Local:** chave HMAC (assinatura com segredo compartilhado) e `POST /dev/token`, que só existe em Development.
- **Autorização por conta:** cada conta tem um titular (`owner_id`), comparado com a claim `sub` (identidade do usuário no token). Conta de outro titular: `403`, em leitura e escrita.
- **Abertura de conta:** exige o papel `accounts-admin` (operação administrativa).

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| API key por cliente | Não identifica o usuário final, então não permite autorização por titular |
| Login próprio (usuários e senhas no MyBank) | Fora do escopo; guardar credenciais aumenta a superfície de ataque. Identidade é responsabilidade de um provedor dedicado |
| Responder `404` para conta alheia (em vez de `403`) | Esconderia a existência da conta. Escolha provisória: `403`, mais claro para depuração; a ser confirmada com segurança e negócio |

## Consequências

- **Positivas:** padrão de mercado, sem estado de sessão (escala horizontal simples). A troca do provedor de identidade é só configuração.
- **Negativas:** o vínculo titular ↔ conta é um campo simples; não há contas conjuntas nem delegação de acesso. A chave HMAC local está versionada (somente para Development; documentado).
- **Evidência:** `SecurityTests` (401 sem token, 403 conta alheia, papel admin, campos proibidos no corpo).

## Reavaliar quando

Surgirem contas conjuntas, procurações (acesso delegado) ou escopos finos por operação (ex.: um token que só consulta).
