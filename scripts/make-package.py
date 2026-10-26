# 🤖 AUTODOCUMENTAÇÃO: ver scripts/CONTEXTO.md e docs/INSTALACAO.md (pacote para os amigos).
# Monta o pacote do mod para os amigos direto da pasta do jogo: só o carregador (winhttp/doorstop), o runtime
# (dotnet/), o núcleo do BepInEx e o mod. NUNCA interop, unity-libs, logs, capturas, saves de teste nem um .cfg
# com a ponte de teste ligada (D18/D20). Uso: python scripts/make-package.py --version 0.5 --ip <IP do host>
import argparse, os, zipfile

ap = argparse.ArgumentParser(description="Monta o pacote do mod para os amigos")
ap.add_argument("--version", default="0.5")
ap.add_argument("--ip", default="", help="IP do host para o JoinAddress (ex.: o do Radmin, 26.x.x.x)")
ap.add_argument("--game", default=r"L:\Games\STORY OF SEASONS Grand Bazaar")
ap.add_argument("--out", default=os.path.join(os.path.expanduser("~"), "Downloads"))
args = ap.parse_args()

G = args.game
DST = os.path.join(args.out, f"SOSGB-Multiplayer-v{args.version}-amiga.zip")

INCLUDE_FILES = ["winhttp.dll", "doorstop_config.ini", ".doorstop_version"]
INCLUDE_DIRS = ["dotnet", os.path.join("BepInEx", "core"), os.path.join("BepInEx", "patchers"), os.path.join("BepInEx", "plugins", "SOSGB.Multiplayer")]


def forbidden(rel):
    r = rel.lower().replace("\\", "/")
    return (r.startswith("bepinex/interop") or r.startswith("bepinex/unity-libs") or "/savedump" in r or "/screenshots" in r
            or "/devsaves" in r or "sosgb-state" in r or r.endswith(".log"))


CFG = """## Configuração do SOSGB Multiplayer para quem ENTRA no mundo de um amigo.
## O resto das opções é criado sozinho na primeira vez que o jogo abre.

[Net]

## Ao abrir o jogo: off | host | join
AutoStart = join

## IP do host (Radmin VPN: o que começa com 26.)
JoinAddress = {ip}

## Seu nome no multiplayer (vazio = nome de usuário do Windows).
PlayerName =

[DevBridge]

## Ponte de teste (só desenvolvimento). Deixe false.
Enabled = false
"""

LEIA = """SOSGB Multiplayer v{version} (TESTE) — como instalar
===============================================

1) Ache a pasta do jogo: Steam > botão direito no jogo > Gerenciar > Procurar arquivos locais
   (a pasta que tem o SOSGrandBazaar.exe).

2) Se já tinha a versão anterior do mod: só copie por cima (substituir tudo).

3) FAÇA BACKUP dos seus saves:  Win + R  >  %APPDATA%\\STORY OF SEASONS Grand Bazaar  >  copie a pasta.

4) Copie TODO o conteúdo desta pasta para dentro da pasta do jogo (mesclar/substituir).

5) Abra o jogo normalmente. Na primeira vez demora alguns minutos (janela preta junto é normal).
   As logos de abertura são puladas (opção SkipIntroLogos).

6) Na tela de título aparece o painel "Multiplayer" (canto inferior direito):
   - escolha "Entrar", digite seu nome e o IP do host (ele vê o IP no painel dele);
   - depois carregue QUALQUER save seu: o jogo recarrega e você entra no mundo do host.
   Casas diferentes: os dois no Radmin VPN, e use o IP do host que começa com 26.

7) Dentro do jogo: lista de jogadores no canto inferior esquerdo, F9 abre o menu, F10 esconde a lista.

Novidades da v0.5:
- Bazaar em grupo: uma pessoa abre a barraca e vende. Os outros veem os MESMOS clientes e a barraca
  aberta. Quem quiser AJUDAR: vá até a barraca e escolha "Abrir loja" no menu dela:
    * Enter no item onde o cliente está com "!!" = vender (a venda é feita no jogo de quem é dono);
    * Tab num espaço vazio + Enter = repor com um item do deck;
    * Shift = tocar o sino (chama clientes para a barraca do dono).
  No fim do turno o aviso mostra quanto a barraca vendeu.
- Agora também é compartilhado: Happy Energy (como o dinheiro), pedidos dos moradores (o progresso
  de todos soma), tutoriais já lidos, selo BRAND, moinho (o que está processando), cogumelos,
  competições dos festivais e o progresso do Bazaar (nível, vendas, reputação).
- Corrigido: pets aparecendo em dobro.
- O tempo só para quando alguém abre o MENU DE PAUSA (Esc); nos outros menus o tempo segue.
- Host e amigos precisam estar na MESMA versão do mod (v{version}).

Para jogar sem o mod: apague (ou renomeie) o arquivo winhttp.dll da pasta do jogo.
Deu problema? Mande o arquivo BepInEx\\LogOutput.log da pasta do jogo.
"""

count = 0
with zipfile.ZipFile(DST, "w", zipfile.ZIP_DEFLATED) as z:
    for f in INCLUDE_FILES:
        z.write(os.path.join(G, f), f); count += 1
    for d in INCLUDE_DIRS:
        for root, _, files in os.walk(os.path.join(G, d)):
            for f in files:
                full = os.path.join(root, f)
                rel = os.path.relpath(full, G).replace("\\", "/")
                if forbidden(rel):
                    raise SystemExit("PROIBIDO no pacote: " + rel)
                z.write(full, rel); count += 1
    z.writestr("BepInEx/config/com.nicolaskalebe.sosgb.multiplayer.cfg", CFG.replace("{ip}", args.ip).encode("utf-8")); count += 1
    z.writestr("LEIA-ME.txt", LEIA.replace("{version}", args.version).encode("utf-8")); count += 1

with zipfile.ZipFile(DST) as z:
    bad = [n for n in z.namelist() if forbidden(n)]
    cfg = z.read("BepInEx/config/com.nicolaskalebe.sosgb.multiplayer.cfg").decode()
    print(DST, "-", count, "arquivos;", os.path.getsize(DST) // 1024, "KB; proibidos:", bad, "; ponte ligada no cfg:", "Enabled = true" in cfg)
