// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e docs/PROTOCOLO.md.
namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Constantes do protocolo de rede. Toda mudança incompatível incrementa <see cref="Version"/>.
/// Formato de cada pacote: [byte MsgType][corpo] (ver docs/PROTOCOLO.md).
/// </summary>
public static class Protocol
{
    /// <summary>Versão do protocolo. Host e convidado precisam ter a mesma.</summary>
    public const int Version = 11;

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

    /// <summary>Host → todos: [byte dormindo][byte total] — contador "dormindo X/Y" (3.4, D12)</summary>
    SleepStatus = 12,

    /// <summary>Qualquer → todos: [byte id][byte código] — dormiu/desmaiou/entrou em evento (avisos, D26)</summary>
    PlayerEvent = 13,

    /// <summary>Qualquer → todos: [byte id][string aparência "cat:masterId,…"] (D28)</summary>
    PlayerAppearance = 14,

    /// <summary>Host → todos: [long total] — carteira compartilhada oficial (4.1, D36). Só vale vindo do host.</summary>
    MoneyState = 15,

    /// <summary>Host → todos: [byte id ou 255] — quem está com o armazém da casa aberto (4.2, D37). Só vale vindo do host.</summary>
    StorageHolder = 16,

    // ---- 20+: ponto a ponto com o host (NÃO são repassadas aos outros convidados) ----

    /// <summary>Convidado → host: pede o mundo atual (sem corpo). Fase 3.1</summary>
    WorldRequest = 20,

    /// <summary>Host → convidado: [int tamanho][bytes do .sav] — snapshot do mundo (WorldSnapshot). Fase 3.1</summary>
    WorldData = 21,

    /// <summary>Convidado → host: [bool pausadoLocalmente] — menu/evento aberto no convidado (3.3, D9)</summary>
    PauseReport = 22,

    /// <summary>Convidado → host: [bool dormindo] — o convidado deitou (3.4, D12)</summary>
    SleepReady = 23,

    /// <summary>Convidado → host: [string json] — perfil individual do convidado (3.6, D24)</summary>
    ProfileUpload = 24,

    /// <summary>Host → convidado: [string json ou vazio] — perfil guardado do convidado, mandado ao entrar (3.6)</summary>
    ProfileData = 25,

    /// <summary>Convidado → host: [long diferença] — o dinheiro mudou no convidado (compra, venda...). 4.1, D36</summary>
    MoneyDelta = 26,

    /// <summary>Convidado ↔ host: [bytes JSON gzip] — armazém da casa inteiro (StorageManager.SaveData). 4.2, D37</summary>
    StorageData = 27,

    /// <summary>Convidado → host: [bool aberto] — abriu/fechou a tela do armazém (trava, D37)</summary>
    StorageUse = 28,
}
