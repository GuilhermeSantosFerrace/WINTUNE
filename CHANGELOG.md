# Changelog

Todas as mudanças relevantes deste projeto são documentadas aqui.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e o projeto adota [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Unreleased]

### Adicionado

- Fase 0: estrutura da solução (`WinTune.Core`, `WinTune.Platform`, `WinTune.App`, projetos de teste).
- `global.json` fixando o SDK .NET 8.0.428; `Directory.Build.props` com `Nullable`, `LangVersion=12` e warnings como erro.
- `Directory.Packages.props` com versões exatas de dependências.
- Script `build.ps1` com alvos `build`, `test`, `run`, `publish` e `clean`.
- Workflow de CI no GitHub Actions (`windows-latest`): restore, build e testes.
- `docs/DECISIONS.md` com as decisões tomadas na Fase 0.
- Fase 1: abstrações de plataforma (`IRegistry`, `IProcessRunner`, `IWmiReader`, `IServiceControl`, `IFileSystem`,
  `IClock`, `IEnvironmentInfo`, `IProcessList`, `IShellLauncher`) e implementações reais para Windows.
- Fase 1: fakes em memória (`tests/WinTune.TestDoubles`) e testes de arquitetura (sem acesso direto ao Windows fora
  de Platform, sem APIs de rede).
- Fase 2: domínio (`Focus`, `Scenario`, `GameTarget`, `Risk`, `Severity`, `Finding`, `TweakResult`), contrato `Tweak`,
  `BackupStore` (escrita atômica, schema 1, journal, intenções de operação, arquivo corrompido movido para `.corrupt`),
  `RestorePointService` e `RegistryTweak` (rollback parcial, reversão segura contra drift, allowlist, modo simulação).
- Fase 3: catálogo v1 (`game-mode`, `hags`, `gamedvr-off`, `startup-delay`, `transparency-off`), `RecommendationEngine`
  (foco × cenários × jogos, prioridade estática, "já aplicado") e `docs/TWEAKS.md` com fonte e rollback manual de cada ajuste.
