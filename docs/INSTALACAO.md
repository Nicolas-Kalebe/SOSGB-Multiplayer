# INSTALAÇÃO E COMO JOGAR (v0.4 — teste)

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../AGENTS.md).

> ⚠️ **Versão de teste.** Já é compartilhado: o mundo do host, o relógio, o sono, o **dinheiro**, o **armazém da casa**, o **correio**, a **fazenda** (terra, plantas, árvores, pedras, minérios, flores, bichinhos), os **animais e celeiros** (ovos, ração, afeição), os **pets e o cavalo**, as **ampliações da casa**, o **deck do Bazaar**, os **eventos já vistos** e o progresso do mundo (missões, lojas, receitas, peixes...). Cada um tem a própria mochila, ferramentas, corações e aparência. Ainda falta testar a **venda no Bazaar** e os **festivais** em grupo (ver [`ROTEIRO.md`](../ROTEIRO.md)).
> **Todos precisam da mesma versão do mod** (o host recusa versões diferentes com uma mensagem).

## O que cada um precisa

- O jogo **STORY OF SEASONS: Grand Bazaar** (PC), **na mesma versão**.
- O pacote do mod: `BepInEx` (6.0.0-be.791, IL2CPP) + `BepInEx/plugins/SOSGB.Multiplayer/` (`SOSGB.Multiplayer.dll` + `LiteNetLib.dll`). O pacote **não contém nenhum arquivo do jogo**.
- Estar na **mesma rede** ou numa **LAN virtual** (Radmin VPN, ZeroTier ou Tailscale) se estiverem em casas diferentes.

## Instalação (os dois jogadores)

1. Abra a pasta do jogo (Steam → botão direito no jogo → Gerenciar → Procurar arquivos locais). É a pasta com o `SOSGrandBazaar.exe`.
2. **Backup dos saves:** `Win + R` → `%APPDATA%\STORY OF SEASONS Grand Bazaar` → copie a pasta inteira para outro lugar.
3. Copie o conteúdo do pacote (`BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`) para dentro da pasta do jogo.
4. Abra o jogo. Abre junto um console preto, que é do BepInEx. **Na primeira vez demora alguns minutos** para chegar ao menu, porque o BepInEx gera os arquivos de interop.
   - O próprio mod traz a janela para frente se ela ficar presa na tela inicial ([D25](../DECISOES.md)). Se mesmo assim ficar numa tela preta, clique na janela do jogo.

## Configuração

Arquivo: `<jogo>\BepInEx\config\com.nicolaskalebe.sosgb.multiplayer.cfg` (criado na primeira execução).

| Opção (`[Net]`) | Quem hospeda | Quem entra |
|---|---|---|
| `AutoStart` | `host` | `join` |
| `JoinAddress` | — | IP do host (ver abaixo) |
| `PlayerName` | seu nome (opcional) | seu nome (opcional) |
| `Port` | `47900` | `47900` |

Em `[Game]`, `SkipIntroLogos = true` (padrão) pula as logos de abertura e vai direto para a tela inicial ([D53](../DECISOES.md)).

**IP do host:**
- Mesma casa: no PC do host, rode `ipconfig` e use o "Endereço IPv4" da placa de rede (ex.: `192.168.100.3`).
- Casas diferentes: os dois instalam o **Radmin VPN** (grátis) e entram na mesma rede. Use o IP que o Radmin mostra para o host (começa com `26.`).

**Firewall (só quem hospeda):** na primeira vez que o jogo hospedar, o Windows pode perguntar se permite o acesso à rede. Marque **Permitir**, inclusive em **redes privadas e públicas**. Se ninguém conseguir entrar, confira em "Firewall do Windows → Permitir um aplicativo" se o `SOSGrandBazaar.exe` está liberado no perfil da rede em uso.

## Interface (no jogo)

- **Tela de título:** painel no canto inferior direito. Escolha *Sozinho*, *Hospedar* ou *Entrar*, digite seu nome e (para entrar) o IP do host. Quem hospeda vê ali os IPs para passar aos amigos (inclusive o do Radmin VPN).
- **Dentro do jogo:** lista de jogadores com onde cada um está. **F9** abre o menu (hospedar/entrar agora, desconectar); **F10** esconde a lista.
- Avisos aparecem no sistema de avisos do próprio jogo: quem entrou/saiu, quem foi dormir, desmaiou ou entrou num evento.

## Como jogar

1. **O host** abre o jogo e carrega o save dele. A janelinha "SOSGB Multiplayer" aparece no canto inferior esquerdo (F8 esconde).
2. **O convidado** abre o jogo e carrega **qualquer save dele**. O jogo recarrega sozinho e ele entra no mundo do host.
3. Para dormir: cada um vai para a cama. Quem dorme espera os outros ("Dormindo X/Y"), e o tempo continua correndo para quem está acordado. Mudou de ideia? Aperte **Backspace** (ou F9 → "Levantar da cama") para levantar. Quando todos dormem, o dia vira para todo mundo.

**Regras desta versão:**
- **Só o host salva, e só quando todos dormem** ([D23](../DECISOES.md)). No menu de pausa, "Salvar" e "Carregar" mostram um aviso em vez de abrir ([D48](../DECISOES.md)).
- **Armazém da casa e deck do Bazaar: um de cada vez.** Se outro jogador já estiver com a tela aberta, a sua fecha e aparece o aviso "Fulano está usando o armazém" ([D37](../DECISOES.md)/[D46](../DECISOES.md)).
- **Romance exclusivo:** um NPC noivo ou casado com um jogador fica indisponível para os outros ([D44](../DECISOES.md)).
- **Natureza compartilhada:** o que um pega (minério, flor, bichinho, ovo) some para todos.
- Na **primeira vez**, o convidado **começa do zero** no mundo do host, como no Stardew ([D30](../DECISOES.md)): corações zerados, mochila vazia, ferramentas básicas. O que ele jogou sozinho no save dele não conta; do save dele só vem o visual do personagem. Por isso ele ainda precisa ter um save (qualquer um) para entrar.
- Os corações, a mochila e as ferramentas do convidado são dele e ficam guardados no PC do host (`%APPDATA%\STORY OF SEASONS Grand Bazaar\SOSGB-MP\`).
- Se alguém abrir um menu, o tempo para para todos ([D9](../DECISOES.md)).

## Limitações conhecidas

- Os NPCs andam de forma independente em cada jogo (cada um vê o NPC no lugar calculado pelo próprio jogo).
- **Venda no Bazaar e festivais em grupo ainda não foram testados** ([D47](../DECISOES.md)).
- O tempo **só para** quando alguém abre o **menu de pausa** (Esc), e aí para para todos. Em outros menus, diálogos e eventos o tempo continua para os outros; quem estava na tela é adiantado ao sair ([D50](../DECISOES.md)).
- **Bazaar:** uma pessoa vende por vez (a barraca fica bloqueada para os outros enquanto alguém vende) ([D51](../DECISOES.md)).
- Cada jogador aparece com a **própria aparência** (rosto, cabelo, roupa, gênero) e o **nome em cima** ([D32](../DECISOES.md)). Quando alguém entra ou troca de visual, o seu personagem **pisca** com outra aparência por 1 a 2 segundos: é normal. Se der problema, desligue em `[Visual] OwnAppearance = false` (aí todos aparecem com o seu modelo).
- Casamento de convidado: o cônjuge mora na casa só no jogo de quem casou; eventos de casado e filhos não foram testados para convidados ([D45](../DECISOES.md)).

## Desinstalar / jogar sem o mod

- Rápido: apague ou renomeie `winhttp.dll` na pasta do jogo.
- Completo: apague também `BepInEx`, `dotnet`, `doorstop_config.ini` e `.doorstop_version`.

## Problemas?

Mande o arquivo `<jogo>\BepInEx\LogOutput.log` de quem teve o problema.

---

### Para desenvolvedores: montar o pacote

Pacote = da pasta do jogo do desenvolvedor: `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `dotnet/`, `BepInEx/core/`, `BepInEx/patchers/`, `BepInEx/plugins/SOSGB.Multiplayer/` + um `.cfg` com `AutoStart = join` + `LEIA-ME.txt`.
**Nunca** incluir `BepInEx/interop/`, `BepInEx/unity-libs/` (gerados a partir do jogo), logs, `savedump/`, screenshots, nem um `.cfg` com `[DevBridge] Enabled = true`.
