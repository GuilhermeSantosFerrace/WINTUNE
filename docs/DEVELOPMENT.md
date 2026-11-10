# Guia de desenvolvimento: executar, compilar, testar e publicar

Este guia explica como rodar o WinTune na sua máquina a partir do código-fonte. Para **usar** o app já pronto, basta baixar
o instalador em [Releases](https://github.com/GuilhermeSantosFerrace/WINTUNE/releases) (veja o [README](../README.md)).

> Mantenha este arquivo atualizado sempre que mudar `build.ps1`, os workflows de CI, `global.json` ou a estrutura de projetos.

---

## 1. Pré-requisitos

| Item | Versão | Observação |
|------|--------|------------|
| Windows | 11 x64 | O app avisa (mas abre) em builds anteriores à 22000. |
| SDK .NET | 8.0.428 ou patch superior da faixa 8.0.4xx | Fixado em `global.json` (`rollForward: latestPatch`). |
| PowerShell | 5.1 (já vem no Windows) ou 7+ | Para o `build.ps1`. |
| Git | qualquer recente | Para clonar o repositório. |
| Inno Setup 6 | 6.x | **Opcional**, só para gerar o instalador. |
| PresentMon | qualquer | **Opcional**, só para medir FPS no Benchmark. |

### Instalar o SDK .NET 8

Opção A (com administrador): instalador oficial em <https://dotnet.microsoft.com/download/dotnet/8.0>.

Opção B (sem administrador, só para o seu usuário):

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile "$env:TEMP\dotnet-install.ps1"
& "$env:TEMP\dotnet-install.ps1" -Channel 8.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
```

O `build.ps1` encontra essa instalação sozinho. Para usar o `dotnet` direto no terminal nessa opção:

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
dotnet --list-sdks   # deve listar 8.0.4xx
```

### Permitir rodar o script

Se o PowerShell bloquear o `build.ps1`, libere só para a sessão atual:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

---

## 2. Obter o código

```powershell
git clone https://github.com/GuilhermeSantosFerrace/WINTUNE.git
cd WINTUNE
```

---

## 3. Comandos do `build.ps1`

Todos rodam a partir da raiz do repositório. A configuração padrão é `Release`; use `-Configuration Debug` para depurar.

| Comando | O que faz |
|---------|-----------|
| `.\build.ps1 build` | Compila a solução. Avisos de analisador contam como erro. |
| `.\build.ps1 run` | Compila e **abre o app**. O Windows mostra o UAC (o app exige administrador). |
| `.\build.ps1 test` | Testes de unidade (os mesmos do CI). Não alteram nada no PC. |
| `.\build.ps1 integration` | Testes de integração **somente leitura** contra o Windows real (ver seção 5). |
| `.\build.ps1 format` | Corrige a formatação (`dotnet format`). Rode antes de commitar. |
| `.\build.ps1 coverage` | Mede a cobertura do `WinTune.Core` e falha abaixo de 80%. |
| `.\build.ps1 publish` | Gera `.\publish\WinTune.exe` (arquivo único, inclui o runtime .NET, ~69 MB). |
| `.\build.ps1 installer -Version 1.2.3` | Publica e gera `.\artifacts\WinTune-Setup-1.2.3.exe` (exige Inno Setup 6). |
| `.\build.ps1 clean` | Apaga `bin/`, `obj/`, `TestResults/` e `publish/`. |

Equivalentes com `dotnet` puro:

```powershell
dotnet build WinTune.sln -c Release
dotnet test WinTune.sln -c Release --filter "Category!=Integration"
dotnet format WinTune.sln --verify-no-changes
dotnet publish src\WinTune.App\WinTune.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

---

## 4. Executar o app na sua máquina

### Pelo código-fonte

```powershell
.\build.ps1 run
```

Aceite o UAC. O executável compilado fica em `src\WinTune.App\bin\Release\net8.0-windows\WinTune.exe` e também pode ser
aberto com duplo clique.

> **Por que não `dotnet run`?** O manifest pede administrador. Num terminal comum, `dotnet run` falha com
> "A operação solicitada requer elevação" (erro 740). O `build.ps1 run` abre o app pelo shell, que mostra o UAC.
> Se preferir `dotnet run`, use um terminal **Executar como administrador**.

### Pela versão publicada

```powershell
.\build.ps1 publish
.\publish\WinTune.exe
```

Esse `.exe` não precisa de .NET instalado e pode ser copiado para outro PC com Windows 11 x64.

### Linha de comando do app

| Comando | Efeito |
|---------|--------|
| `WinTune.exe` | Abre a interface. |
| `WinTune.exe --revert-all` | Desfaz todos os ajustes sem abrir a interface e mostra um resumo. Código de saída: `0` = tudo revertido, `2` = pendências (conflito/falha), `3` = o app já estava aberto. |
| `WinTune.exe --revert-all --quiet` | Igual, sem a janela de resumo (usado pelo desinstalador). |

### Onde o app guarda os dados

| Caminho | Conteúdo |
|---------|----------|
| `%ProgramData%\WinTune\backup.json` | Valores originais de cada ajuste e histórico (necessário para desfazer). |
| `%APPDATA%\WinTune\games.json` | Jogos, nomes de processo e overlays conhecidos (editável). |
| `%APPDATA%\WinTune\profiles\` | Perfis salvos. |
| `%APPDATA%\WinTune\gamemode*.json` | Configuração e estado do modo jogo. |
| `%LOCALAPPDATA%\WinTune\sessions\` | Rodadas do Benchmark. |
| `%LOCALAPPDATA%\WinTune\logs\` | Logs (`wintune.log`, rotativo). |

> **Atenção:** ao desenvolver, o app roda de verdade e altera o seu Windows quando você clica em "Aplicar". Tudo é
> reversível por "Desfazer tudo", mas prefira testar ajustes numa VM. Se algo der errado, veja [SAFETY.md](SAFETY.md).

---

## 5. Testes

| Projeto | O que cobre | Roda no CI? |
|---------|-------------|:-----------:|
| `tests\WinTune.Core.Tests` | Domínio, ajustes, backup, regras, telemetria, benchmark... (com fakes) | sim |
| `tests\WinTune.Presentation.Tests` | ViewModels, sem janela | sim |
| `tests\WinTune.App.Tests` | Carrega a XAML real e falha em erro de binding | sim |
| `tests\WinTune.Platform.Tests` | Fakes + integração com o Windows real | só os de unidade |
| `tests\WinTune.TestDoubles` | Fakes compartilhados (não é projeto de teste) | — |

**Testes de integração** (`[Trait("Category","Integration")]`) leem o registro, WMI, serviços, sensores e arquivos reais,
envolvidos em guardas que **lançam erro se houver tentativa de escrita**. Mesmo assim, rode só na sua máquina:

```powershell
.\build.ps1 integration
# ou um teste específico, mostrando a saída:
dotnet test tests\WinTune.Platform.Tests -c Release --filter "FullyQualifiedName~StaticDiagnostics" --logger "console;verbosity=detailed"
```

O teste de overhead da telemetria (`Sampling_At1Hz_UsesLessThanOnePercentCpu`) leva cerca de 20 s.

Rodar um teste ou uma classe:

```powershell
dotnet test tests\WinTune.Core.Tests --filter "FullyQualifiedName~PowerPlanTests"
```

---

## 6. Abrir no editor

- **Visual Studio 2022** (17.8 ou superior, com a carga ".NET desktop"): abra `WinTune.sln`. Para depurar o app com F5, abra o
  Visual Studio **como administrador** (por causa do manifest).
- **VS Code / Kiro**: extensão C# Dev Kit; abra a pasta do repositório. Para depurar, inicie o editor como administrador ou
  use `.\build.ps1 run` e anexe o depurador ao processo `WinTune.exe`.

Projeto de inicialização: `src\WinTune.App`.

---

## 7. Regras de qualidade (o build falha se não forem seguidas)

- `TreatWarningsAsErrors` em todos os projetos; analisadores .NET no nível `Recommended` em `src/`.
- Formatação conforme `.editorconfig`: rode `.\build.ps1 format` antes de commitar (o CI usa `--verify-no-changes`).
- Cobertura mínima de 80% de linhas no `WinTune.Core`.
- Só `src\WinTune.Platform` pode tocar o registro ou iniciar processos; nenhum código pode usar APIs de rede (testes em
  `tests\WinTune.Core.Tests\ArchitectureTests.cs`).
- Dependências com **versão exata** em `Directory.Packages.props` (nada de `*` ou faixas).
- Código em inglês; documentação e textos da interface em pt-BR (textos da UI em `src\WinTune.App\Resources\Strings.xaml`).
- Commits no padrão Conventional Commits (`feat(core): ...`, `fix(app): ...`).

---

## 8. CI e releases

- **CI** (`.github/workflows/ci.yml`, a cada push e PR, em `windows-latest`): restore → verificação de formatação → build →
  testes → cobertura ≥ 80% → publicação + compilação do instalador (validação).
- **Release** (`.github/workflows/release.yml`): ao enviar uma tag `vX.Y.Z` (ou `vX.Y.Z-rc.N`), roda os testes, publica,
  gera o instalador, calcula `SHA256SUMS.txt` e cria o release no GitHub (tags com `-` viram pré-release).

Para lançar uma versão:

```powershell
# 1. Atualize CHANGELOG.md e, se mudar a versão base, VersionPrefix em Directory.Build.props
# 2. Commit e push na main
git tag -a v1.0.0 -m "WinTune 1.0.0"
git push origin v1.0.0
```

Antes da `v1.0.0` final, execute o roteiro manual em VM de [SAFETY.md](SAFETY.md) (seção 5).

---

## 9. Problemas comuns

| Sintoma | Causa / solução |
|---------|-----------------|
| `A compatible .NET SDK was not found` / `NETSDK1045` | SDK 8.0.4xx ausente. Instale (seção 1) ou, se usou a opção B, rode via `build.ps1` ou ajuste `DOTNET_ROOT`/`PATH`. |
| `dotnet run` → "requer elevação" (740) | Use `.\build.ps1 run` ou um terminal como administrador. |
| Build falha com `IDE...` ou `CA...` | Avisos são erros. Rode `.\build.ps1 format`; para `CA...`, corrija o código (veja D-032 em DECISIONS.md). |
| Build falha com `ENDOFLINE` no CI | Arquivo com final de linha LF. Rode `.\build.ps1 format` e commite. |
| "O WinTune já está aberto" | Instância única: feche a outra janela (ou o ícone na bandeja, se o modo jogo estiver ligado). |
| Aba Telemetria sem GPU/temperatura | Sem driver NVIDIA (NVML) ou sem zona térmica ACPI: o app mostra a mensagem e usa o que estiver disponível. |
| `installer` → "Inno Setup 6 não encontrado" | Instale em <https://jrsoftware.org/isinfo.php>. |
| SmartScreen ao abrir o `.exe` | O executável não é assinado: "Mais informações > Executar assim mesmo" (confira o hash do release). |
