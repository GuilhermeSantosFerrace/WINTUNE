# WinTune

Aplicativo desktop para **Windows 11** que diagnostica gargalos de desempenho e aplica ajustes
**seguros e reversíveis**, sem descaracterizar o sistema. Todo ajuste guarda o estado original,
explica o que muda e por quê, e pode ser desfeito.

> Estado atual: **Fase 0 (fundação do repositório)**. Ainda não há ajustes nem diagnóstico.
> O plano completo está em [`WINTUNE_BUILD_SPEC.md`](WINTUNE_BUILD_SPEC.md).

## Estrutura do projeto

```
WinTune/
├── WinTune.sln
├── global.json               # fixa o SDK .NET 8
├── Directory.Build.props     # Nullable, LangVersion 12, warnings como erro
├── Directory.Packages.props  # versões exatas das dependências
├── build.ps1                 # build, test, run, publish, clean
├── docs/
│   └── DECISIONS.md          # decisões e ambiguidades resolvidas
├── src/
│   ├── WinTune.Core/         # domínio, sem UI e sem acesso direto ao Windows
│   ├── WinTune.Platform/     # adaptadores do Windows atrás de interfaces
│   └── WinTune.App/          # interface WPF (MVVM a partir da Fase 6)
├── tests/
│   ├── WinTune.Core.Tests/
│   └── WinTune.Platform.Tests/
└── .github/workflows/ci.yml  # build + testes em windows-latest
```

## Pré-requisitos

- Windows 11 (x64).
- [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) na versão indicada em `global.json` (8.0.428 ou patch superior).
  Sem privilégio de administrador, é possível instalar por usuário:

  ```powershell
  Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
  .\dotnet-install.ps1 -Channel 8.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
  ```

  O `build.ps1` detecta e usa automaticamente essa instalação.

## Instalar

```powershell
git clone https://github.com/GuilhermeSantosFerrace/WINTUNE.git
cd WINTUNE
.\build.ps1 build
```

## Rodar

```powershell
.\build.ps1 run
```

## Testar

```powershell
.\build.ps1 test
```

Testes de integração (que tocam o Windows real) são marcados com `[Trait("Category","Integration")]`
e ficam fora do CI e do alvo `test`. Rode-os apenas em VM ou máquina descartável.

## Publicar

```powershell
.\build.ps1 publish   # gera .\publish\WinTune.exe (single-file, self-contained, win-x64)
```

## Limpar

```powershell
.\build.ps1 clean
```

## Princípios

1. Reversibilidade total.
2. Transparência: cada ajuste mostra o que muda, onde, por que, risco e se exige reinício.
3. Diagnóstico antes de mudança: gargalos de hardware/BIOS são apontados, não "consertados".
4. Nada de desativar Defender, Windows Update, UAC, VBS/HVCI ou telemetria do Windows.
5. Sem promessas de ganho: medir antes e depois.
6. Nenhum contato com o processo ou arquivos dos jogos (anti-cheat).
