// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d90.
using System;
using System.Collections.Generic;
using BokuMono;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Segurar UM morador parado sem a pausa geral do jogo (D90). Medido offline (comando npchold da ponte, 20 moradores na
/// cidade, 4 s): PauseObject.Pause(Idle ou All) sozinho deixava 15–18 andando: a animação congelava, mas o script da
/// agenda (NPCScheduleAgent / NPCBazaarVisitorAgent) continuava rodando e religava o NavMeshAgent a cada quadro (o
/// morador "escorregava tentando andar", visto pelo usuário). Parar só o agente também não: a agenda o religa.
/// Pausa Idle + script da agenda desligado + agente parado = 0 de 20, e ao soltar todos voltam à agenda.
/// A pausa geral (Pauser) também segura, mas trava o jogador e vale para o mundo inteiro.
///
/// Vários motivos podem segurar o mesmo morador (conversa de outro jogador, tempo do grupo parado, cópia da NpcPosSync):
/// cada um segura com a sua chave e o morador só volta quando nenhum segura mais.
/// </summary>
public static class NpcHold
{
    private sealed class Held
    {
        public NonPlayerCharacter Npc;
        public readonly HashSet<string> Keys = new();
        public bool PausedByUs;
        public bool FrozenAnim;
        public bool AgentSaved, AgentUpdatePos, AgentUpdateRot;
    }

    private static readonly Dictionary<IntPtr, Held> All = new();

    public static bool IsHeld(NonPlayerCharacter npc) => npc != null && All.ContainsKey(npc.Pointer);

    public static bool IsHeldBy(NonPlayerCharacter npc, string key) =>
        npc != null && All.TryGetValue(npc.Pointer, out var h) && h.Keys.Contains(key);

    /// <summary>Segura o morador. freezeAnim: congela também a animação (menu de pausa, como o Esc do jogo sozinho).</summary>
    public static void Hold(NonPlayerCharacter npc, string key, bool freezeAnim = false)
    {
        if (npc == null) return;
        if (!All.TryGetValue(npc.Pointer, out var h))
        {
            h = new Held { Npc = npc };
            All[npc.Pointer] = h;
            try
            {
                var po = npc.PauseObject;
                if (po != null && !po.IsPaused) { po.Pause(PauseType.Idle); h.PausedByUs = true; }
            }
            catch { }
        }
        h.Keys.Add(key);
        if (freezeAnim) h.FrozenAnim = true;
        Apply(h);
    }

    /// <summary>Reaplica a cada tick: o jogo religa partes (o agente ao trocar de rota, a pausa ao fechar um menu).</summary>
    private static void Apply(Held h)
    {
        var npc = h.Npc;
        try
        {
            var sch = npc.NPCSchedule;
            if (sch != null && sch.enabled) sch.enabled = false;
            var ag = sch != null ? sch.Agent : null;
            if (ag != null)
            {
                // Mesmo com a agenda desligada, algo religava o agente entre um tick e outro (medido: 4 de 25 andando a
                // ~0,5 m/s com o menu de pausa da convidada). Sem updatePosition o agente não move mais o corpo.
                if (!h.AgentSaved) { h.AgentSaved = true; h.AgentUpdatePos = ag.updatePosition; h.AgentUpdateRot = ag.updateRotation; }
                ag.updatePosition = false;
                ag.updateRotation = false;
                if (ag.enabled && ag.isOnNavMesh) { ag.isStopped = true; ag.velocity = Vector3.zero; }
            }
            var po = npc.PauseObject;
            if (h.PausedByUs && po != null && !po.IsPaused) po.Pause(PauseType.Idle);
            if (h.FrozenAnim)
            {
                var an = npc.GetComponentInChildren<Animator>();
                if (an != null) an.speed = 0f;
            }
        }
        catch { /* o morador pode ter sido destruído na troca de mapa */ }
    }

    public static void Release(NonPlayerCharacter npc, string key)
    {
        if (npc == null || !All.TryGetValue(npc.Pointer, out var h)) return;
        h.Keys.Remove(key);
        if (key == KeyPause) h.FrozenAnim = false;
        if (h.Keys.Count > 0) { Apply(h); return; }
        All.Remove(npc.Pointer);
        try
        {
            var sch = npc.NPCSchedule;
            var ag = sch != null ? sch.Agent : null;
            if (ag != null)
            {
                // O agente pode ter ficado noutro lugar: traz para onde o corpo está antes de devolver o movimento.
                // nextPosition, e não Warp: o Warp apaga a rota, e o morador ficava parado até a agenda mandar o próximo
                // destino, depois corria para recuperar o atraso (visto pelo usuário; no host os que andavam caíram de 7
                // para 0 depois de algumas pausas).
                if (ag.enabled && ag.isOnNavMesh) { ag.nextPosition = npc.transform.position; ag.isStopped = false; }
                if (h.AgentSaved) { ag.updatePosition = h.AgentUpdatePos; ag.updateRotation = h.AgentUpdateRot; }
            }
            if (sch != null) sch.enabled = true;
            var an = npc.GetComponentInChildren<Animator>();
            if (an != null && an.speed == 0f) an.speed = 1f;
            var po = npc.PauseObject;
            if (h.PausedByUs && po != null && po.IsPaused) po.Resume();
        }
        catch { }
    }

    /// <summary>Solta tudo o que esta chave segura (ex.: o menu de pausa fechou).</summary>
    public static void ReleaseAll(string key)
    {
        var list = new List<NonPlayerCharacter>();
        foreach (var h in All.Values) if (h.Keys.Contains(key)) list.Add(h.Npc);
        foreach (var npc in list) Release(npc, key);
    }

    /// <summary>A cada tick: reaplica e esquece moradores destruídos.</summary>
    public static void Tick()
    {
        var dead = new List<IntPtr>();
        foreach (var kv in All)
        {
            if (kv.Value.Npc == null) { dead.Add(kv.Key); continue; }
            Apply(kv.Value);
        }
        foreach (var p in dead) All.Remove(p);
    }

    public static int Count => All.Count;

    public const string KeyPause = "pausa";     // menu de pausa de alguém (D89)
    public const string KeyFestival = "festival"; // cena do festival de outro jogador (D83)
    public const string KeyTalk = "conversa";   // conversa de outro jogador com o morador (D68)
    public const string KeyPuppet = "copia";    // cópia da referência do mapa (NpcPosSync)
}
