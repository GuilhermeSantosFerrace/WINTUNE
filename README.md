# WinTune

Aplicativo desktop para **Windows 11** que diagnostica gargalos de desempenho e aplica ajustes **seguros e reversíveis**,
sem descaracterizar o sistema. Feito para um PC de jogos/trabalho (calibrado para Ryzen 7 5700X3D + RTX 4070 Super + 16 GB),
mas funciona em qualquer PC com Windows 11.

- Cada ajuste mostra **o que muda, onde, por quê, o risco e se exige reinício**, e guarda o estado original.
- **Diagnóstico antes de mudança:** problemas de hardware/BIOS (EXPO/XMP, dual channel, Resizable BAR, monitor em 60 Hz…) são
  apontados, não "consertados" com placebo.
- **Sem promessas:** o Benchmark compara antes × depois e diz "sem diferença relevante" quando for o caso.
- **Nada sai do seu PC:** sem rede, sem telemetria própria.
- **Anti-cheat:** o WinTune nunca toca no processo nem nos arquivos do Rainbow Six Siege ou do CS2; sensores só em modo usuário.

Plano completo: [`WINTUNE_BUILD_SPEC.md`](WINTUNE_BUILD_SPEC.md) · Catálogo: [`docs/TWEAKS.md`](docs/TWEAKS.md) ·
Segurança e recuperação: [`docs/SAFETY.md`](docs/SAFETY.md) · Decisões: [`docs/DECISIONS.md`](docs/DECISIONS.md) ·
Arquitetura: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) · Telas: [`docs/UX.md`](docs/UX.md)

## Instalar

1. Baixe `WinTune-Setup-<versão>.exe` (ou a versão portátil `WinTune-<versão>-portable.exe`) na página
   [Releases](https://github.com/GuilhermeSantosFerrace/WINTUNE/releases). Confira o hash em `SHA256SUMS.txt`.
2. Execute o instalador. Como o executável **não é assinado digitalmente**, o SmartScreen pode avisar
   ("O Windows protegeu o computador"): clique em **Mais informações > Executar assim mesmo** se o hash conferir.
3. O WinTune pede permissão de administrador (UAC) ao abrir, porque altera HKLM e planos de energia.

## Usar

1. **Otimizar:** escolha o foco (Trabalho, Jogos, Os dois ou Jogos + live), os jogos (Siege/CS2) e os cenários com problema; clique em **Analisar**.
2. Revise a lista. Itens experimentais (HAGS, flip model) e cosméticos vêm desmarcados; "Já aplicado" significa nada a fazer.
3. **Aplicar selecionados** mostra a prévia exata (valor atual → novo), cria um ponto de restauração e aplica.
4. **Diagnóstico / Jogos:** avisos de hardware e guias por jogo (o app não altera BIOS nem arquivos de jogo).
5. **Live** (foco Jogos + live): rede, espaço para gravar, codificador e checklist do OBS; nada é alterado no OBS.
6. **Telemetria:** monitor ao vivo e "sessão de diagnóstico" que diz qual componente limita (GPU, CPU, RAM, disco, térmico).
7. **Benchmark:** grave ≥ 3 rodadas antes e ≥ 3 depois (mesmo mapa/cenário), opcionalmente com o CSV do
   [PresentMon](https://github.com/GameTechDev/PresentMon), e compare.
8. **Inicialização / Serviços / Limpeza:** conservadores; itens da Microsoft e de segurança ficam protegidos; a limpeza
   tem prévia obrigatória e **não é reversível**.
9. **Perfis e modo jogo:** salve seleções; o modo jogo automático (opcional) aplica ajustes ao abrir o jogo e restaura ao fechar.
10. **Desfazer tudo** restaura o estado original de tudo que o WinTune alterou. **Histórico** mostra cada operação e exporta
   um relatório local (Markdown/JSON) sem dados pessoais.

## Recuperação

- Dentro do app: **Desfazer tudo**.
- Pelo desinstalador: ele oferece "Desfazer tudo" antes de remover.
- Sem o app: siga [`docs/SAFETY.md`](docs/SAFETY.md) (comandos `reg`, `powercfg`, `sc`, `schtasks`); os valores originais
  estão em `%ProgramData%\WinTune\backup.json`. Último recurso: `rstrui.exe` (ponto de restauração).
- Linha de comando: `WinTune.exe --revert-all` (adicione `--quiet` para não mostrar a janela de resumo).

## Desenvolver

O guia completo (pré-requisitos, executar na máquina, compilar, testar, publicar, editor, CI/release e problemas comuns) está
em **[`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md)**. Resumo:

```powershell
git clone https://github.com/GuilhermeSantosFerrace/WINTUNE.git
cd WINTUNE
.\build.ps1 build        # compila (analisadores como erro)
.\build.ps1 run          # compila e abre o app (UAC)
.\build.ps1 test         # testes de unidade (os mesmos do CI)
.\build.ps1 publish      # .\publish\WinTune.exe (arquivo único, sem precisar de .NET instalado)
```

Requer Windows 11 x64 e o SDK .NET 8 (versão em `global.json`).

### Estrutura

```
src/
  WinTune.Platform.Abstractions/  interfaces para tudo que toca o Windows
  WinTune.Platform/               implementações reais (registro, processos, WMI, SCM, PDH/NVML, Agendador)
  WinTune.Core/                   domínio, ajustes, backup, diagnóstico, telemetria, benchmark, ...
  WinTune.Presentation/           ViewModels (MVVM), testáveis sem janela
  WinTune.App/                    WPF (XAML, temas, bandeja)
tests/                            Core, Platform, Presentation, App (XAML) e TestDoubles (fakes)
installer/                        Inno Setup
docs/                             DECISIONS, TWEAKS, SAFETY, ARCHITECTURE, UX
```

## Perguntas frequentes

**Vou ganhar quantos FPS?** Não há número prometido. Em Siege e CS2 o que mais pesa costuma ser memória (EXPO/XMP, dual
channel, capacidade) e apps em segundo plano. Meça com o Benchmark.

**O app pode me banir no Siege/CS2?** O WinTune não abre, lê nem altera o processo ou os arquivos dos jogos e não carrega
drivers de kernel. Ainda assim, nenhum fabricante de anti-cheat certifica ferramentas de terceiros; a política do Siege pode mudar.

**Por que o HAGS vem desmarcado?** O ganho varia por driver/jogo e pode piorar travadas; teste com o Benchmark.

**Desativar a Segurança baseada em virtualização dá FPS?** Às vezes alguns %, mas é proteção de segurança: o WinTune só informa.

**A limpeza pode ser desfeita?** Não. Por isso há prévia obrigatória e os caches de shader vêm desmarcados.

**Funciona no Windows 10?** Não é suportado; o app avisa ao abrir em builds anteriores ao Windows 11.
