// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e docs/PROTOCOLO.md.
using LiteNetLib.Utils;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Blocos de bytes com o tamanho em int (revisão do núcleo). O PutBytesWithLength do LiteNetLib guarda o tamanho em
/// ushort e CORTA, sem avisar, o que passar de 64 KB (estado comprimido de um mapa inteiro, correio grande...).
/// </summary>
public static class NetBlob
{
    public static void PutBlob(this NetDataWriter w, byte[] data)
    {
        w.Put(data.Length);
        w.Put(data);
    }

    public static byte[] GetBlob(this NetDataReader r)
    {
        var n = r.GetInt();
        var b = new byte[n];
        r.GetBytes(b, n);
        return b;
    }
}

/// <summary>
/// Constantes do protocolo de rede. Toda mudança incompatível incrementa <see cref="Version"/>.
/// Formato de cada pacote: [byte MsgType][corpo] (ver docs/PROTOCOLO.md).
/// </summary>
public static class Protocol
{
    /// <summary>Versão do protocolo. Host e convidado precisam ter a mesma.</summary>
    public const int Version = 26;

    /// <summary>Mensagens de jogo abaixo deste número são repassadas pelo host a todos; a partir dele, são só entre convidado e host.</summary>
    public const byte FirstDirectMsg = 20;

    /// <summary>Chave de conexão do LiteNetLib (filtra conexões de outros programas).</summary>
    public const string ConnectionKey = "SOSGB-MP";

    public const int DefaultPort = 47900;
    public const int MaxPlayers = 4; // D6: host + 3

    /// <summary>ID de jogador do host. Convidados recebem 1..3.</summary>
    public const byte HostPlayerId = 0;
}

public enum MsgType : byte
{
    /// <summary>Convidado → host, logo após conectar: [int protocolo][string modVersion][string nome][string guid]</summary>
    Hello = 1,

    /// <summary>Host → convidado: [byte seuId][byte qtdJogadores]{[byte id][string nome]}…</summary>
    Welcome = 2,

    /// <summary>Host → convidado, antes de desconectar: [string motivo]</summary>
    Reject = 3,

    /// <summary>Host → todos: [byte id][string nome] (entrou) </summary>
    PlayerJoined = 4,

    /// <summary>Host → todos: [byte id] (saiu)</summary>
    PlayerLeft = 5,

    /// <summary>Qualquer → host → outros, 10×/s: [byte id][uint fieldId][float x,y,z][float rotY][byte camadas]{[int hash][float norm][float peso]}…[byte params]{[int hash][byte tipo][float valor]}… (Fase 2.4)</summary>
    PlayerState = 10,

    /// <summary>Host → todos, 2×/s: [long ticks][bool rodando] — relógio do host (3.2) e pausa efetiva (3.3)</summary>
    ClockState = 11,

    /// <summary>Host → todos: [byte dormindo][byte total][bool virada começou] — contador "dormindo X/Y" (3.4, D12; o bool é da D63)</summary>
    SleepStatus = 12,

    /// <summary>Qualquer → todos: [byte id][byte código] — dormiu/desmaiou/entrou em evento (avisos, D26)</summary>
    PlayerEvent = 13,

    /// <summary>Qualquer → todos: [byte id][string aparência "cat:masterId,…"] (D28)</summary>
    PlayerAppearance = 14,

    /// <summary>Host → todos: [long total] — carteira compartilhada oficial (4.1, D36). Só vale vindo do host.</summary>
    MoneyState = 15,

    /// <summary>Dono da barraca → todos, 10×/s: [byte dono][uint mapa][bool aberta][byte n]{[uint cliente][float x,y,z][float rotY][int hashAnim][float norm][float Speed][byte balão]} — clientes do Bazaar de quem está vendendo (D55)</summary>
    BazaarVisitors = 18,

    /// <summary>Dono da barraca → todos, quando muda: [byte dono][int prateleira][byte n]{[uint item][int qtd][int qualidade][int frescor]} — o que está em cada espaço da barraca (D55)</summary>
    BazaarShelves = 19,

    /// <summary>Host → todos: [string "charaId=nome;…"] — NPCs noivos/casados e com quem (romance exclusivo, 6.4, D44)</summary>
    RomanceClaims = 17,

    /// <summary>Host → todos: [byte armazém][byte id ou 255] — quem está com cada armazém aberto (0 casa, 1 deck, 2 peças do Bazaar; D37/D46). Só vale vindo do host.</summary>
    StorageHolder = 16,

    // ---- 20+: ponto a ponto com o host (NÃO são repassadas aos outros convidados) ----

    /// <summary>Convidado → host: pede o mundo atual (sem corpo). Fase 3.1</summary>
    WorldRequest = 20,

    /// <summary>Host → convidado: [int tamanho][bytes do .sav] — snapshot do mundo (WorldSnapshot). Fase 3.1</summary>
    WorldData = 21,

    /// <summary>Convidado → host: [bool noMenuDePausa] — o convidado está no menu de pausa do jogo (D50: só ele pausa o grupo)</summary>
    PauseReport = 22,

    /// <summary>Convidado → host: [bool dormindo] — o convidado deitou (3.4, D12)</summary>
    SleepReady = 23,

    /// <summary>Convidado → host: [int n][gzip do json] — perfil individual do convidado (3.6, D24; comprimido na D63)</summary>
    ProfileUpload = 24,

    /// <summary>Host → convidado: [int n][gzip do json ou vazio] — perfil guardado do convidado, mandado ao entrar (3.6; comprimido na D63)</summary>
    ProfileData = 25,

    /// <summary>Convidado → host: [long diferença] — o dinheiro mudou no convidado (compra, venda...). 4.1, D36</summary>
    MoneyDelta = 26,

    /// <summary>Convidado ↔ host: [byte armazém][bytes JSON gzip] — armazém inteiro (StorageManager.SaveData). 4.2, D37/D46</summary>
    StorageData = 27,

    /// <summary>Convidado → host: [byte armazém][bool aberto] — abriu/fechou a tela do armazém (trava, D37/D46)</summary>
    StorageUse = 28,

    /// <summary>Convidado ↔ host: [bytes JSON gzip] — estado do correio (LetterManager.SaveData). 4.6, D39</summary>
    LetterData = 29,

    /// <summary>Convidado ↔ host: [bytes gzip: uint mapa, int n, {byte op, byte tipo, string guid, string json}] — mudanças nos objetos de um mapa residente (fazenda). Fase 5, D40</summary>
    FieldOps = 30,

    /// <summary>Convidado ↔ host: [bytes JSON gzip do FieldSaveData] — mapa não residente inteiro, mandado por quem saiu dele (D40)</summary>
    FieldSnapshot = 31,

    /// <summary>Convidado ↔ host: [bytes gzip: int n, {string guid, string json}] — estado de cuidados dos animais da fazenda que mudaram (5.3, D41)</summary>
    AnimalData = 32,

    /// <summary>Convidado ↔ host: [string parte][bytes JSON gzip] — estado de um gerenciador compartilhado (moinho, apicultura, cogumelos, vasos, pets, cavalo). 5.4, D42</summary>
    SharedState = 33,

    /// <summary>Convidado → host: [long ticks] — o tempo pulou para frente no jogo do convidado; o grupo pula junto (D50)</summary>
    ClockJump = 34,

    /// <summary>Ajudante → host (→ dono da barraca): [byte ajudante][byte tipo 1=atender 2=pôr item][byte balcão][byte espaço][uint item][int qtd][int qualidade][int frescor] — pedido do ajudante no Bazaar; o host repassa ao dono se não for ele (D56)</summary>
    BazaarHelp = 35,

    /// <summary>Convidado → host: [long diferença] — a Happy Energy mudou no convidado (D57, como MoneyDelta)</summary>
    HappyDelta = 36,

    /// <summary>Host → todos: [long total] — Happy Energy compartilhada oficial (D57, como MoneyState). Só vale vindo do host.</summary>
    HappyState = 37,

    /// <summary>Convidado ↔ host: [byte 0=mudanças 1=lista oficial][bool lojaNova][int n]{[uint id][byte estado][int progresso ou diferença][long aceito][long completo][byte 1=por valor]} — pedidos dos moradores (D57; por valor: o host fica com o maior)</summary>
    MissionData = 38,

    /// <summary>Qualquer → host (→ todos): [byte quem][uint morador][bool conversando] — morador ocupado numa conversa: para de andar e não aceita outra conversa nos outros jogos (D68)</summary>
    NpcTalk = 39,

    /// <summary>Qualquer → host (→ todos): [byte quem][int n]{[int caminho do osso][3 float posição][4 float rotação]} — esqueleto de quem está deitado/desmaiado; n = 0 limpa (D69)</summary>
    PlayerPose = 40,

    /// <summary>Host → convidado: [uint mapa][string guid] — item do chão que outro jogador pegou primeiro; o convidado tira da mochila o que pegou junto (D70)</summary>
    ItemTakeBack = 41,
}
