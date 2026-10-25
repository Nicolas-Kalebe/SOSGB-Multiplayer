# INSTALAÇÃO E COMO JOGAR (v0.1 — teste)

> 🤖 **AUTODOCUMENTAÇÃO OBRIGATÓRIA.** Este projeto se documenta sozinho: registre toda decisão com contexto em [`DECISOES.md`](../DECISOES.md), mantenha um `CONTEXTO.md` em cada pasta e abra todo `.md` com este aviso. Regras completas em [`AGENTS.md`](../AGENTS.md).

> ⚠️ **Versão de teste.** Vocês se veem andando, compartilham o mundo do host, o relógio e o sono. **O que vocês fazem no mundo (plantar, colher, comprar, dinheiro) ainda NÃO é sincronizado** (Fases 4–5 do [`ROTEIRO.md`](../ROTEIRO.md)).

## O que cada um precisa

- O jogo **STORY OF SEASONS: Grand Bazaar** (PC), **na mesma versão**.
- O pacote do mod: `BepInEx` (6.0.0-be.791, IL2CPP) + `BepInEx/plugins/SOSGB.Multiplayer/` (`SOSGB.Multiplayer.dll` + `LiteNetLib.dll`). O pacote **não contém nenhum arquivo do jogo**.
- Estar na **mesma rede** ou numa **LAN virtual** (Radmin VPN, ZeroTier ou Tailscale) se estiverem em casas diferentes.

## Instalação (os dois jogadores)

1. Abra a pasta do jogo (Steam → botão direito no jogo → Gerenciar → Procurar arquivos locais). É a pasta com o `SOSGrandBazaar.exe`.
2. **Backup dos saves:** `Win + R` → `%APPDATA%\STORY OF SEASONS Grand Bazaar` → copie a pasta inteira para outro lugar.
3. Copie o conteúdo do pacote (`BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`) para dentro da pasta do jogo.
4. Abra o jogo. Abre junto um console preto, que é do BepInEx. **Na primeira vez demora alguns minutos** para chegar ao menu, porque o BepInEx gera os arquivos de interop.
   - Se ficar numa tela preta, clique na janela do jogo ou dê Alt+Tab (ver [D21](../DECISOES.md)).

## Configuração

Arquivo: `<jogo>\BepInEx\config\com.nicolaskalebe.sosgb.multiplayer.cfg` (criado na primeira execução).

| Opção (`[Net]`) | Quem hospeda | Quem entra |
|---|---|---|
| `AutoStart` | `host` | `join` |
| `JoinAddress` | — | IP do host (ver abaixo) |
| `PlayerName` | seu nome (opcional) | seu nome (opcional) |
| `Port` | `47900` | `47900` |

**IP do host:**
- Mesma casa: no PC do host, rode `ipconfig` e use o "Endereço IPv4" da placa de rede (ex.: `192.168.100.3`).
- Casas diferentes: os dois instalam o **Radmin VPN** (grátis) e entram na mesma rede. Use o IP que o Radmin mostra para o host (começa com `26.`).

**Firewall (só quem hospeda):** na primeira vez que o jogo hospedar, o Windows pode perguntar se permite o acesso à rede. Marque **Permitir**, inclusive em **redes privadas e públicas**. Se ninguém conseguir entrar, confira em "Firewall do Windows → Permitir um aplicativo" se o `SOSGrandBazaar.exe` está liberado no perfil da rede em uso.

## Como jogar

1. **O host** abre o jogo e carrega o save dele. A janelinha "SOSGB Multiplayer" aparece no canto inferior esquerdo (F8 esconde).
2. **O convidado** abre o jogo e carrega **qualquer save dele**. O jogo recarrega sozinho e ele entra no mundo do host.
3. Para dormir: cada um vai para a cama. Quem dorme espera os outros ("Dormindo X/Y"). Quando todos dormem, o dia vira para todo mundo.

**Regras desta versão:**
- **Só o host salva, e só quando todos dormem** ([D23](../DECISOES.md)). Tentar salvar no meio do dia mostra "Não foi possível salvar". É esperado.
- Na **primeira vez**, o convidado entra com a mochila, as ferramentas e os corações **do próprio save** ([D24](../DECISOES.md), provisório).
- Os corações, a mochila e as ferramentas do convidado são dele e ficam guardados no PC do host (`%APPDATA%\STORY OF SEASONS Grand Bazaar\SOSGB-MP\`).
- Se alguém abrir um menu, o tempo para para todos ([D9](../DECISOES.md)).

## Limitações conhecidas (v0.1)

- Plantar, colher, comprar e mudar o dinheiro **não sincroniza**. O que o convidado fizer no mundo volta ao estado do host na virada do dia.
- Os NPCs andam de forma independente em cada jogo.
- O avatar do outro jogador tem a **aparência do seu personagem** (aparência própria ainda não implementada).
- Eventos de casado e filhos não funcionam para convidados ([D12a](../DECISOES.md)).

## Desinstalar / jogar sem o mod

- Rápido: apague ou renomeie `winhttp.dll` na pasta do jogo.
- Completo: apague também `BepInEx`, `dotnet`, `doorstop_config.ini` e `.doorstop_version`.

## Problemas?

Mande o arquivo `<jogo>\BepInEx\LogOutput.log` de quem teve o problema.

---

### Para desenvolvedores: montar o pacote

Pacote = da pasta do jogo do desenvolvedor: `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `dotnet/`, `BepInEx/core/`, `BepInEx/patchers/`, `BepInEx/plugins/SOSGB.Multiplayer/` + um `.cfg` com `AutoStart = join` + `LEIA-ME.txt`.
**Nunca** incluir `BepInEx/interop/`, `BepInEx/unity-libs/` (gerados a partir do jogo), logs, `savedump/`, screenshots, nem um `.cfg` com `[DevBridge] Enabled = true`.
