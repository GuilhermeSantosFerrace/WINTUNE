# Documentação do projeto WinTune

- `docs/DEVELOPMENT.md` é o guia de como executar, compilar, testar, publicar e lançar o WinTune. Sempre que uma mudança
  afetar esses passos (alvos do `build.ps1`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, projetos da
  solução, workflows em `.github/workflows`, instalador em `installer/`, argumentos de linha de comando do app ou caminhos
  de dados), atualize `docs/DEVELOPMENT.md` no mesmo commit.
- O resumo de "Desenvolver" no `README.md` deve continuar consistente com `docs/DEVELOPMENT.md`.
- Novas decisões técnicas vão em `docs/DECISIONS.md`; novos ajustes em `docs/TWEAKS.md` (com fonte e rollback manual);
  mudanças relevantes em `CHANGELOG.md` (seção `[Unreleased]`).
- Documentação em português (pt-BR); código em inglês.
