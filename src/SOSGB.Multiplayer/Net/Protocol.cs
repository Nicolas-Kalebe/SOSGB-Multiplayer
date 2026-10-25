// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e docs/PROTOCOLO.md.
namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Constantes do protocolo de rede. Toda mudança incompatível incrementa <see cref="Version"/>.
/// Formato de cada pacote: [byte MsgType][corpo] (ver docs/PROTOCOLO.md).
/// </summary>
public static class Protocol
{
    /// <summary>Versão do protocolo. Host e convidado precisam ter a mesma.</summary>
    public const int Version = 3;

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
    /// <summary>Convidado → host, logo após conectar: [int protocolo][string modVersion][string nome]</summary>
    Hello = 1,

    /// <summary>Host → convidado: [byte seuId][byte qtdJogadores]{[byte id][string nome]}…</summary>
    Welcome = 2,

    /// <summary>Host → convidado, antes de desconectar: [string motivo]</summary>
    Reject = 3,

    /// <summary>Host → todos: [byte id][string nome] (entrou) </summary>
    PlayerJoined = 4,

    /// <summary>Host → todos: [byte id] (saiu)</summary>
    PlayerLeft = 5,

    /// <summary>Qualquer → host → outros, 10×/s: [byte id][uint fieldId][float x,y,z][float rotY][int animHash][float animNorm] (Fase 2.4)</summary>
    PlayerState = 10,

    /// <summary>Host → todos, 2×/s: [long ticks][bool rodando] — relógio do host (3.2) e pausa efetiva (3.3)</summary>
    ClockState = 11,

    // ---- 20+: ponto a ponto com o host (NÃO são repassadas aos outros convidados) ----

    /// <summary>Convidado → host: pede o mundo atual (sem corpo). Fase 3.1</summary>
    WorldRequest = 20,

    /// <summary>Host → convidado: [int tamanho][bytes do .sav] — snapshot do mundo (WorldSnapshot). Fase 3.1</summary>
    WorldData = 21,

    /// <summary>Convidado → host: [bool pausadoLocalmente] — menu/evento aberto no convidado (3.3, D9)</summary>
    PauseReport = 22,
}
