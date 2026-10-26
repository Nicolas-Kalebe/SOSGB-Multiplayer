// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d63.
using System;
using System.Collections.Generic;
using LiteNetLib.Utils;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Mensagens ao vivo que chegaram quando o módulo não estava pronto (carregando o mundo, trocando de mapa, virada do
/// dia no convidado): guardadas em ordem e aplicadas quando ele ficar pronto (D63). Antes eram descartadas, e o que
/// outro jogador fazia nesse meio-tempo se perdia. Ex.: o host colhia de manhã enquanto o convidado ainda carregava o
/// mundo; para o convidado a colheita continuava no pé e podia ser colhida de novo (duplicação).
///
/// As mensagens guardadas são estados (de um ladrilho, de um animal, do correio...): reaplicar em ordem deixa o estado
/// final igual ao do host. No HOST, durante a virada do dia, nada é guardado: o dia novo substitui o mundo inteiro e
/// estados do dia velho aplicados depois desfariam o processamento do dia.
/// </summary>
public sealed class HeldMessages
{
    private const int Max = 2000;
    private readonly string _name;
    private readonly Queue<(MsgType type, byte from, byte[] data)> _queue = new();
    private bool _overflowLogged;

    public HeldMessages(string name)
    {
        _name = name;
        NetSession.OnHosting += Clear;
        NetSession.OnJoined += Clear;
    }

    public int Count => _queue.Count;

    public void Clear()
    {
        _queue.Clear();
        _overflowLogged = false;
    }

    /// <summary>Guarda a mensagem (se for o caso). Chamar ANTES de ler qualquer campo do leitor.</summary>
    public void HoldIfAllowed(MsgType type, byte from, NetDataReader r)
    {
        if (NetSession.Current == NetSession.Mode.Offline) return;
        if (NetSession.Current == NetSession.Mode.Host && DaySync.InDayChange) return;
        if (_queue.Count >= Max)
        {
            _queue.Dequeue();
            if (!_overflowLogged) { _overflowLogged = true; Plugin.Logger.LogWarning($"[{_name}] fila de mensagens cheia; descartando as mais antigas"); }
        }
        _queue.Enqueue((type, from, r.GetRemainingBytes()));
    }

    /// <summary>Aplica, em ordem, o que estava guardado (só o que já estava na fila quando começou).</summary>
    public void Drain(Action<MsgType, byte, NetDataReader> handler)
    {
        var n = _queue.Count;
        if (n == 0) return;
        Plugin.Logger.LogInfo($"[{_name}] aplicando {n} mensagem(ns) que chegaram enquanto não estava pronto");
        while (n-- > 0 && _queue.Count > 0)
        {
            var (type, from, data) = _queue.Dequeue();
            try { handler(type, from, new NetDataReader(data)); }
            catch (Exception e) { Plugin.Logger.LogWarning($"[{_name}] mensagem guardada: {e.Message}"); }
        }
    }
}
