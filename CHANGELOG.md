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
