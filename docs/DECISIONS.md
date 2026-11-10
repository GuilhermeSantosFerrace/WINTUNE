# Registro de decisões (WinTune)

Decisões técnicas e ambiguidades resolvidas durante a construção. Regra do spec (seção 0, item 4):
na dúvida, escolher a opção mais conservadora e registrar aqui.

Formato: `D-NNN — título`, com fase, contexto, decisão e consequências.

---

## D-001 — Script de build: `build.ps1` em vez de `Makefile`

- **Fase:** 0
- **Contexto:** o spec aceita `Makefile` ou `build.ps1`. O alvo é exclusivamente Windows 11.
- **Decisão:** usar `build.ps1` (PowerShell já vem no Windows; `make` não).
- **Consequências:** alvos `build`, `test`, `run`, `publish`, `clean`. O CI chama `dotnet` diretamente.

## D-002 — Versão do SDK fixada em 8.0.428 com `rollForward: latestPatch`

- **Fase:** 0
- **Contexto:** o spec pede `global.json` fixando o SDK .NET 8.
- **Decisão:** `8.0.428` (última feature band 8.0.4xx no momento) com `latestPatch`, aceitando só correções de patch
  da mesma feature band. Pré-releases desabilitados.
- **Consequências:** o CI usa `actions/setup-dotnet` lendo o mesmo `global.json`.

## D-003 — Gerenciamento central de pacotes com versão exata

- **Fase:** 0
- **Contexto:** regra 7 da seção 0 (versões exatas, sem ranges).
- **Decisão:** `Directory.Packages.props` com versões na notação `[x.y.z]` (correspondência exata no NuGet).
- **Consequências:** atualizar dependência é uma mudança explícita em um único arquivo.

## D-004 — FluentAssertions 7.2.0 (e não 8.x)

- **Fase:** 0
- **Contexto:** a partir da 8.0 o FluentAssertions passou a ter licença comercial (Xceed); a 7.x continua Apache-2.0.
- **Decisão:** fixar `FluentAssertions` em `7.2.0` para evitar dúvida de licenciamento.
- **Consequências:** revisar na Fase 14 (revisão de licenças).

## D-005 — `SupportedOSPlatformVersion` não fixado no projeto

- **Fase:** 0
- **Contexto:** com `TargetFramework=net8.0-windows` a `TargetPlatformVersion` padrão é 7.0, e o SDK recusa
  `SupportedOSPlatformVersion=10.0.22000.0` (NETSDK1135). Usar `net8.0-windows10.0.22000.0` traria as projeções
  WinRT (CsWinRT), dependência que o projeto não precisa agora.
- **Decisão:** manter `net8.0-windows` sem `SupportedOSPlatformVersion`. A exigência de Windows 11 será verificada em
  tempo de execução (build do SO) e/ou no manifest do aplicativo nas fases seguintes.
- **Consequências:** o analisador de compatibilidade de plataforma considera "Windows" genérico.

## D-006 — Camadas e frameworks-alvo

- **Fase:** 0
- **Decisão:** `WinTune.Core` em `net8.0` (sem dependência de Windows ou UI, testável em qualquer ambiente);
  `WinTune.Platform` e `WinTune.App` em `net8.0-windows`. Um teste garante que o Core não referencia Platform nem WPF.

## D-007 — Elevação adiada para a Fase 6

- **Fase:** 0
- **Contexto:** o manifest `requireAdministrator` é entregável da Fase 6, e a seção 2 sugere UI sem elevação + helper elevado.
- **Decisão:** na Fase 0 o app roda sem manifest de elevação (janela placeholder, não altera nada).
