# UX do WinTune (telas e textos)

Lacuna 9 da seção 10.2. Princípios: dizer o que muda **antes** de mudar, nunca prometer ganho, deixar claro o que é
reversível e o que não é, e funcionar 100% por teclado.

## Estrutura

```
┌ WinTune ─────────────────────────────────────────────────────────────┐
│ Diagnóstico e ajustes seguros e reversíveis para o Windows 11        │
├──────────────────────────────────────────────────────────────────────┤
│ [Otimizar] [Diagnóstico] [Jogos] [Telemetria] [Inicialização]        │
│ [Limpeza] [Benchmark] [Serviços] [Perfis e modo jogo] [Histórico]    │
│ [Registro]                                                           │
├───────────────┬──────────────────────────────────────────────────────┤
│ Foco          │ [Analisar] [Aplicar selecionados] [Desfazer tudo]    │
│ ( ) Trabalho  │ Ajustes recomendados                                  │
│ (•) Jogos     │ [x] Desligar gravação em segundo plano · Seguro ·     │
│ ( ) Os dois   │     Não aplicado                                      │
│ Jogos         │     descrição… / Onde muda: HKCU\System\…             │
│ [x] Siege     │ [ ] HAGS · Exige reinício · Não aplicado              │
│ [ ] CS2       │     "Experimental: compare antes/depois…"             │
│ Cenários      │                                                       │
│ [x] Travadas  │                                                       │
│ …             │                                                       │
├───────────────┴──────────────────────────────────────────────────────┤
│ Status: Análise concluída…                [■■■■■■   ] [Cancelar]     │
└──────────────────────────────────────────────────────────────────────┘
```

- **Jogos** só aparece quando o foco é Jogos ou Os dois.
- **Analisar** nunca altera nada. **Aplicar selecionados** mostra a prévia exata (valor atual → novo) e pede confirmação.
- **Desfazer tudo** pergunta antes e, se outro programa mudou um valor depois do WinTune, pergunta de novo por item.
- Barra de status com texto ao vivo (leitor de tela: `LiveSetting=Polite`), progresso e Cancelar (Esc).

## Textos-chave

| Situação | Texto |
|----------|-------|
| Item já aplicado | "Já aplicado; nada a fazer aqui." |
| Experimental | "Experimental: compare antes/depois com o benchmark antes de manter." |
| Confirmação | "O WinTune vai alterar o seguinte (tudo reversível em "Desfazer tudo"): …" |
| Ponto de restauração em 24 h | "O Windows já criou um ponto de restauração nas últimas 24 h; um novo não foi criado. O backup do WinTune continua valendo." |
| Conflito | "Outro programa ou o usuário alterou estes valores depois do WinTune; nada foi sobrescrito." |
| Limpeza | "A limpeza apaga arquivos e NÃO pode ser desfeita." |
| Privacidade | "Tudo fica neste PC: o WinTune não envia dados pela rede." |

## Acessibilidade

- Atalhos de teclado (sublinhados): Analisar (Alt+A), Aplicar selecionados (Alt+S), Desfazer tudo (Alt+D), F5 = Analisar, Esc = Cancelar.
- Todos os controles interativos têm `AutomationProperties.Name`.
- Temas: claro e escuro seguem "Modo de aplicativo" do Windows; com Alto contraste, o app usa as cores do sistema.
- Contraste dos temas próprios ≥ 7:1 para texto principal (valores nos arquivos `Themes/*.xaml`).
- Validação completa de WCAG exige teste manual com leitor de tela (Narrador/NVDA) e revisão especializada.

## Mudanças visuais permitidas (definição de "descaracterizar", lacuna 8)

Única mudança visual que o catálogo pode fazer: `transparency-off` (desmarcado por padrão). Nada de temas, ícones,
animações, menu Iniciar, barra de tarefas ou comportamento de periféricos.
