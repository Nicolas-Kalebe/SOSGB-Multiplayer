// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d90.
using System;
using System.Collections.Generic;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Moradores no mesmo lugar para todos (D90, pedido do usuário: "parece que nem minimamente sincronizado tá").
/// Antes cada jogo simulava os próprios moradores pela agenda e pelo relógio; bastava o relógio de um jogo parar
/// (menu, conversa, o host preso numa fala) ou o mapa carregar em outro momento para eles irem a lugares diferentes
/// (medido: o mesmo morador a 15 m de distância nos dois jogos, no mesmo segundo).
///
///  - REFERÊNCIA do mapa = o jogador de menor número que está nele (o host, se estiver). Só ela simula os moradores
///    daquele mapa; 10×/s manda cada um: número, posição, giro e o estado do Animator, MAIS a hora do relógio dela
///    (pedido do usuário), para quem recebe saber a que hora a posição se refere e ver se os relógios divergem.
///  - Os OUTROS no mesmo mapa: cada morador vira cópia, como os clientes do Bazaar (D55): a agenda daqui continua
///    rodando por baixo (ver Drive), o NavMeshAgent deixa de mover o corpo e o mod o leva até a posição da referência,
///    com o Animator dela. Moradores que a referência não tem (ex.: entraram numa casa lá) ficam escondidos, com a agenda daqui rodando.
///  - Conversa: o morador com quem ESTE jogador conversa volta ao controle do jogo daqui durante a conversa (fala,
///    giro, animações); na referência ele fica parado pela D68 e, ao terminar, a cópia volta a seguir a referência.
///  - Sem pacote por 1,5 s (a referência saiu do mapa, caiu) ou trocando de mapa: tudo é devolvido e a agenda volta.
/// </summary>
public class NpcPosSync : MonoBehaviour
{
    public NpcPosSync(IntPtr ptr) : base(ptr) { }

    private const float SendInterval = 0.1f;
    private const float StaleSeconds = 1.5f;
    private const int MaxNpcs = 48;                    // ~16 bytes cada: cabe num pacote sem garantia (~1000 bytes)
    private const long ClockWarnTicks = 10L * 60 * 10_000_000; // 10 min de jogo

    private struct State
    {
        public uint Id;
        public Vector3 Pos;
        public float RotY;
        public int Hash;
        public float Norm;
        public float Speed;
        public bool Visible;
    }

    private sealed class Puppet
    {
        public NonPlayerCharacter Npc;
        public Animator Anim;
        public UnityEngine.AI.NavMeshAgent Agent;
        public bool Driven, AgentUpdatePos, AgentUpdateRot, RootMotion, Hidden;
        public Vector3 Target;
        public float RotY;
        public int Hash;
        public bool Seen;
    }

    private static readonly int SpeedParam = Animator.StringToHash("Speed");
    private static readonly Dictionary<uint, Puppet> Puppets = new();
    private static readonly Dictionary<IntPtr, bool> HasSpeed = new();
    private static readonly Dictionary<IntPtr, bool> IsVisitorCache = new();
    private static readonly Dictionary<uint, float> PopTried = new();
    private const float PopRetry = 10f;

    /// <summary>Já existe (ou está sendo criado) um visitante com este número na lista do Bazaar? O PopVisitor demora alguns
    /// quadros até o morador aparecer ativo; pedir de novo nesse meio tempo criava DOIS iguais (medido: 5 duplicados).</summary>
    private static bool VisitorExists(uint id)
    {
        try
        {
            var l = BazaarManager.Instance.NpcAgentList;
            if (l != null) foreach (var a in l) if (a != null && a.VisitorMaster != null && a.VisitorMaster.Id == id) return true;
            // Também os INATIVOS (no pool do jogo, fora da lista): o jogo os reativa sozinho, e criar outro dava dois iguais
            // (medido: 4 duplicados mesmo conferindo a lista).
            foreach (var npc in UnityEngine.Object.FindObjectsOfType<NonPlayerCharacter>(true))
                if (npc != null && npc.CharacterId == id) return true;
        }
        catch { }
        return false;
    }

    /// <summary>Cliente/figurante do Bazaar (NPCBazaarVisitorAgent): em dia de venda a BazaarViewSync cuida deles (D100).</summary>
    private static bool IsVisitor(NonPlayerCharacter npc)
    {
        if (IsVisitorCache.TryGetValue(npc.Pointer, out var v)) return v;
        try { v = npc.NPCSchedule != null && npc.NPCSchedule.TryCast<NPCBazaarVisitorAgent>() != null; } catch { v = false; }
        if (IsVisitorCache.Count > 512) IsVisitorCache.Clear();
        IsVisitorCache[npc.Pointer] = v;
        return v;
    }

    /// <summary>Figurantes são números de visitante (300–399) e o próprio jogo os cria por PopVisitor (como na D55).</summary>
    private static bool IsVisitorId(uint id) => id >= 300 && id < 400;

    private static List<State> _states;
    private static bool _fresh;
    private static byte _ref = NetSession.UnknownId;
    private static uint _refField;
    private static long _refTicks;
    private static float _lastAt = -100;
    private static bool _active, _clockWarned;
    private static bool _subscribed;
    private float _nextSend;
    public static int Sent, Received, Popped;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += ReleaseAll;
        NetSession.OnPlayerLeft += _ => { if (_active) ReleaseAll(); };
    }

    private static bool Enabled => Plugin.SyncNpcPositions == null || Plugin.SyncNpcPositions.Value;

    private void Update()
    {
        try
        {
            if (NetSession.Current == NetSession.Mode.Offline || !Enabled || !FieldManager.IsInstance || !DateManager.IsInstance)
            {
                if (_active) ReleaseAll();
                return;
            }
            var field = FieldManager.Instance.CurrentFieldId;
            var reference = ReferenceOf(field);
            if (reference == NetSession.LocalId)
            {
                if (_active) ReleaseAll();
                if (Time.unscaledTime >= _nextSend && OthersIn(field))
                {
                    _nextSend = Time.unscaledTime + SendInterval;
                    Send(field);
                }
                return;
            }
            ViewerTick(field);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[NpcPos] {e.Message}"); }
    }

    // ---------- quem é a referência ----------

    /// <summary>Menor número de jogador no mapa (o host primeiro). Sem posição conhecida do outro, ele não conta.</summary>
    private static byte ReferenceOf(uint field)
    {
        var best = NetSession.LocalId;
        foreach (var id in NetSession.Players.Keys)
            if (id != NetSession.LocalId && id < best && PlayerSync.RemoteField(id) == field) best = id;
        return best;
    }

    private static bool OthersIn(uint field)
    {
        foreach (var id in NetSession.Players.Keys)
            if (id != NetSession.LocalId && PlayerSync.RemoteField(id) == field) return true;
        return false;
    }

    // ---------- referência: manda ----------

    private static Animator AnimOf(NonPlayerCharacter npc) => npc.GetComponentInChildren<Animator>();

    private static bool AnimHasSpeed(Animator an)
    {
        if (an == null) return false;
        if (HasSpeed.TryGetValue(an.Pointer, out var has)) return has;
        has = false;
        try
        {
            foreach (var p in an.parameters)
                if (p.nameHash == SpeedParam && p.type == AnimatorControllerParameterType.Float) { has = true; break; }
        }
        catch { }
        HasSpeed[an.Pointer] = has;
        return has;
    }

    private static void Send(uint field)
    {
        var list = new List<State>();
        foreach (var npc in UnityEngine.Object.FindObjectsOfType<NonPlayerCharacter>())
        {
            if (npc == null || !npc.gameObject.activeInHierarchy || npc.CharacterId == 0) continue;
            if (BazaarViewSync.Governing && IsVisitor(npc)) continue; // D100: clientes da venda são da BazaarViewSync
            var s = new State { Id = npc.CharacterId, Pos = npc.transform.position, RotY = npc.transform.eulerAngles.y, Visible = true };
            try { s.Visible = !npc.IsGraphicCulling; } catch { }
            var an = AnimOf(npc);
            if (an != null && an.isActiveAndEnabled)
            {
                var st = an.GetCurrentAnimatorStateInfo(0);
                s.Hash = st.fullPathHash;
                s.Norm = st.normalizedTime;
                if (AnimHasSpeed(an)) s.Speed = an.GetFloat(SpeedParam);
            }
            list.Add(s);
            if (list.Count >= MaxNpcs) break;
        }
        var me = NetSession.LocalId;
        var ticks = DateManager.Instance.Now.Ticks;
        NetSession.SendGame(MsgType.NpcPositions, w => Write(w, me, field, ticks, list), DeliveryMethod.Sequenced);
        Sent++;
    }

    private static short Cm(float metros) => (short)Mathf.Clamp(Mathf.RoundToInt(metros * 100f), short.MinValue, short.MaxValue);

    private static void Write(NetDataWriter w, byte me, uint field, long ticks, List<State> list)
    {
        w.Put(me); w.Put(field); w.Put(ticks); w.Put((byte)list.Count);
        foreach (var s in list)
        {
            w.Put((ushort)s.Id);
            w.Put(Cm(s.Pos.x)); w.Put(Cm(s.Pos.y)); w.Put(Cm(s.Pos.z));
            w.Put((byte)(Mathf.Repeat(s.RotY, 360f) / 360f * 255f));
            w.Put(s.Hash);
            w.Put((byte)(Mathf.Repeat(s.Norm, 1f) * 255f));
            w.Put((byte)Mathf.Clamp(s.Speed * 50f, 0, 255));
        }
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.NpcPositions) return;
        var sender = r.GetByte();
        if (sender == NetSession.LocalId) return;
        if (NetSession.Current == NetSession.Mode.Host && sender != from) return;
        var field = r.GetUInt();
        var ticks = r.GetLong();
        var n = r.GetByte();
        var list = new List<State>(n);
        for (var i = 0; i < n; i++)
            list.Add(new State
            {
                Id = r.GetUShort(),
                Pos = new Vector3(r.GetShort() / 100f, r.GetShort() / 100f, r.GetShort() / 100f),
                RotY = r.GetByte() / 255f * 360f,
                Hash = r.GetInt(),
                Norm = r.GetByte() / 255f,
                Speed = r.GetByte() / 50f,
            });
        if (NetSession.Current == NetSession.Mode.Host)
        {
            // As mensagens 20+ só chegam ao host: ele repassa a quem estiver no mesmo mapa (os outros não usam).
            foreach (var id in NetSession.Players.Keys)
                if (id != NetSession.LocalId && id != from && PlayerSync.RemoteField(id) == field)
                    NetSession.SendTo(id, MsgType.NpcPositions, w => Write(w, sender, field, ticks, list), DeliveryMethod.Sequenced);
        }
        // Só vale a da referência deste mapa (dois jogadores podem mandar por um instante ao trocar de mapa).
        if (!FieldManager.IsInstance || field != FieldManager.Instance.CurrentFieldId || sender != ReferenceOf(field)) return;
        _ref = sender;
        _refField = field;
        _refTicks = ticks;
        _states = list;
        _fresh = true;
        _lastAt = Time.unscaledTime;
        Received++;
    }

    // ---------- os outros: cópias ----------

    private static void ViewerTick(uint field)
    {
        var want = _states != null && _refField == field && Time.unscaledTime - _lastAt < StaleSeconds && !DaySync.InDayChange;
        if (!want)
        {
            if (_active) ReleaseAll();
            return;
        }
        if (!_active)
        {
            Plugin.Logger.LogInfo($"[NpcPos] moradores seguem #{_ref} no mapa {field}");
            _clockWarned = false;
        }
        _active = true;

        if (_fresh)
        {
            _fresh = false;
            Apply();
            // O relógio da referência vem junto (pedido do usuário): relógios muito diferentes explicam moradores em
            // lugares diferentes da agenda; avisa no log uma vez.
            var diff = DateManager.Instance.Now.Ticks - _refTicks;
            if (!_clockWarned && Math.Abs(diff) > ClockWarnTicks)
            {
                _clockWarned = true;
                Plugin.Logger.LogWarning($"[NpcPos] relógio daqui {DateManager.Instance.Now} × referência #{_ref} {new BokuMonoDateTime(_refTicks)}: {diff / 600_000_000L} min de diferença");
            }
        }

        // Interpolação entre os pacotes (10/s), como os avatares e os clientes do Bazaar.
        var k = Mathf.Clamp01(Time.deltaTime * 12f);
        foreach (var p in Puppets.Values)
        {
            if (p.Npc == null || p.Hidden || !p.Driven) continue;
            try
            {
                var tr = p.Npc.transform;
                if ((tr.position - p.Target).sqrMagnitude > 2.25f) tr.position = p.Target;
                else tr.position = Vector3.Lerp(tr.position, p.Target, k);
                tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.Euler(0, p.RotY, 0), k);
            }
            catch { /* o morador pode ter sido destruído na troca de mapa */ }
        }
    }

    private static NonPlayerCharacter Find(uint id)
    {
        foreach (var npc in UnityEngine.Object.FindObjectsOfType<NonPlayerCharacter>())
            if (npc != null && npc.CharacterId == id && npc.gameObject.activeInHierarchy) return npc;
        return null;
    }

    private static void Apply()
    {
        foreach (var p in Puppets.Values) p.Seen = false;
        var localTalk = NpcSync.LocalTalk;
        foreach (var s in _states)
        {
            // Conversando com este jogador: o jogo daqui cuida dele (fala, giro); a referência o segura pela D68.
            if (s.Id == localTalk)
            {
                if (Puppets.TryGetValue(s.Id, out var tp)) { Release(tp); Puppets.Remove(s.Id); }
                continue;
            }
            if (!Puppets.TryGetValue(s.Id, out var p) || p.Npc == null)
            {
                var npc = Find(s.Id);
                if (npc == null)
                {
                    // D100: figurante (300–399) que só a referência criou: pede ao próprio jogo (PopVisitor, como os clientes
                    // do Bazaar na D55); aparece na lista nos quadros seguintes e vira cópia. Moradores de verdade só a
                    // agenda daqui põe no mapa (se ela não pôs, ele está em outro lugar aqui: fica como está).
                    if (IsVisitorId(s.Id) && !BazaarViewSync.Governing && BazaarManager.IsInstance && !VisitorExists(s.Id)
                        && (!PopTried.TryGetValue(s.Id, out var t) || Time.unscaledTime - t > PopRetry))
                    {
                        PopTried[s.Id] = Time.unscaledTime;
                        try { BazaarManager.Instance.PopVisitor(s.Id, (Il2CppSystem.Action<NPCBazaarVisitorAgent>)null); Popped++; PoppedIds.Add(s.Id); }
                        catch (Exception e) { Plugin.Logger.LogWarning($"[NpcPos] criar figurante {s.Id}: {e.Message}"); }
                    }
                    continue;
                }
                if (BazaarViewSync.Governing && IsVisitor(npc)) continue;
                p = new Puppet { Npc = npc, Anim = AnimOf(npc) };
                Puppets[s.Id] = p;
            }
            if (!p.Driven) { Drive(p); p.Npc.transform.position = s.Pos; }
            p.Seen = true;
            if (p.Hidden) Show(p, true);
            p.Target = s.Pos;
            p.RotY = s.RotY;
            // Conversando com outro jogador (D68): a animação da conversa de lá não existe igual aqui (o morador afundava
            // no chão e depois não aceitava conversa, visto pelo usuário); fica com a pose parada daqui.
            if (p.Anim != null && !NpcHold.IsHeldBy(p.Npc, NpcHold.KeyTalk))
            {
                try
                {
                    if (s.Hash != 0 && s.Hash != p.Hash && p.Anim.HasState(0, s.Hash)) { p.Anim.Play(s.Hash, 0, s.Norm % 1f); p.Hash = s.Hash; }
                    if (AnimHasSpeed(p.Anim)) p.Anim.SetFloat(SpeedParam, s.Speed);
                }
                catch { }
            }
        }
        // Moradores daqui que a referência não tem neste mapa: escondidos enquanto a cópia vale.
        foreach (var npc in UnityEngine.Object.FindObjectsOfType<NonPlayerCharacter>())
        {
            if (npc == null || !npc.gameObject.activeInHierarchy || npc.CharacterId == 0 || npc.CharacterId == localTalk) continue;
            if (BazaarViewSync.Governing && IsVisitor(npc))
            {
                // D100: durante a venda os clientes são da BazaarViewSync: devolve e não esconde.
                if (Puppets.TryGetValue(npc.CharacterId, out var vp) && vp.Npc != null) { Release(vp); Puppets.Remove(npc.CharacterId); }
                continue;
            }
            if (Puppets.TryGetValue(npc.CharacterId, out var p) && p.Npc != null)
            {
                if (p.Npc.Pointer == npc.Pointer)
                {
                    if (!p.Seen)
                    {
                        if (p.Driven) Release(p); // escondido: o agente volta a mover o corpo (a agenda daqui segue)
                        Show(p, false);           // a cada pacote: o jogo daqui às vezes volta a mostrar
                    }
                    continue;
                }
                // D100: SEGUNDO morador com o mesmo número (a agenda daqui criou o figurante que o PopVisitor já tinha criado;
                // medido: até 7 duplicados andando soltos): fica escondido como extra enquanto a cópia vale.
                HideExtra(npc);
                continue;
            }
            // Só esconde: a agenda daqui continua rodando (congelada, ela divergia da referência sobre quem está na rua, e
            // ao soltar os moradores saíam correndo atrasados, visto pelo usuário).
            var np = new Puppet { Npc = npc, Anim = AnimOf(npc) };
            Puppets[npc.CharacterId] = np;
            Show(np, false);
        }
        var gone = new List<uint>();
        foreach (var kv in Puppets) if (kv.Value.Npc == null) gone.Add(kv.Key);
        foreach (var id in gone) Puppets.Remove(id);
    }

    private static readonly Dictionary<IntPtr, NonPlayerCharacter> Extras = new();
    private static readonly HashSet<uint> PoppedIds = new();   // figurantes que ESTE jogo criou por PopVisitor (D100)
    private static readonly List<NonPlayerCharacter> Parked = new(); // cópias criadas por nós que ficaram sem uso: escondidas até o mapa mudar

    private static void HideExtra(NonPlayerCharacter npc)
    {
        if (!Extras.ContainsKey(npc.Pointer)) Extras[npc.Pointer] = npc;
        try { npc.SetActiveGraphic(false); npc.SetPlayerPenetrate(true); } catch { }
    }

    private static void ShowExtras()
    {
        foreach (var npc in Extras.Values)
            try { if (npc != null) { npc.SetActiveGraphic(true); npc.SetPlayerPenetrate(false); } } catch { }
        Extras.Clear();
    }

    private static void Show(Puppet p, bool show)
    {
        p.Hidden = !show;
        try { p.Npc.SetActiveGraphic(show); } catch { }
        // Escondido continua andando pela agenda daqui: sem isto o jogador esbarrava num morador invisível (D100).
        try { p.Npc.SetPlayerPenetrate(!show); } catch { }
    }

    /// <summary>
    /// A cópia passa a seguir a referência na tela, mas a agenda DAQUI continua rodando por baixo (D90, 2ª versão): o
    /// agente segue a rota local sem mover o corpo (updatePosition/updateRotation = falso) e o mod põe o corpo onde a
    /// referência diz. Assim a agenda local não atrasa e, ao soltar, o morador continua a rota sem correr. A 1ª versão
    /// parava a agenda (NpcHold) e os dois jogos divergiam sobre quem estava na rua.
    /// </summary>
    private static void Drive(Puppet p)
    {
        p.Driven = true;
        try
        {
            p.Agent = p.Npc.NPCSchedule != null ? p.Npc.NPCSchedule.Agent : null;
            if (p.Agent != null)
            {
                p.AgentUpdatePos = p.Agent.updatePosition;
                p.AgentUpdateRot = p.Agent.updateRotation;
                p.Agent.updatePosition = false;
                p.Agent.updateRotation = false;
            }
            if (p.Anim != null) { p.RootMotion = p.Anim.applyRootMotion; p.Anim.applyRootMotion = false; }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[NpcPos] assumir {p.Npc.CharacterId}: {e.Message}"); }
    }

    private static void Release(Puppet p)
    {
        var npc = p.Npc;
        if (npc == null) return;
        try
        {
            if (p.Hidden) Show(p, true);
            if (!p.Driven) return;
            p.Driven = false;
            if (p.Anim != null) p.Anim.applyRootMotion = p.RootMotion;
            if (p.Agent != null)
            {
                // O corpo volta para onde a rota local está (o agente andou por baixo): sem Warp, que apaga a rota.
                var dist = (p.Agent.nextPosition - npc.transform.position).magnitude;
                if (dist > 0.05f && dist < 3f && p.Agent.enabled && p.Agent.isOnNavMesh) p.Agent.nextPosition = npc.transform.position;
                else if (dist >= 3f) npc.transform.position = p.Agent.nextPosition;
                p.Agent.updatePosition = p.AgentUpdatePos;
                p.Agent.updateRotation = p.AgentUpdateRot;
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[NpcPos] devolver {npc.CharacterId}: {e.Message}"); }
    }

    private static void ReleaseAll()
    {
        // D100: figurante que NÓS criamos e que tem um gêmeo do próprio jogo (extra escondido): ao devolver, mostra o do jogo
        // e deixa o nosso escondido (senão ficavam dois andando). Some na troca de mapa.
        var twins = new HashSet<uint>();
        foreach (var e in Extras.Values) try { if (e != null) twins.Add(e.CharacterId); } catch { }
        foreach (var p in Puppets.Values)
        {
            if (p.Npc != null && PoppedIds.Contains(p.Npc.CharacterId) && twins.Contains(p.Npc.CharacterId))
            {
                try { if (p.Driven) { p.Driven = false; if (p.Anim != null) p.Anim.applyRootMotion = p.RootMotion; } p.Npc.SetActiveGraphic(false); p.Npc.SetPlayerPenetrate(true); Parked.Add(p.Npc); } catch { }
                continue;
            }
            Release(p);
        }
        if (_active) Plugin.Logger.LogInfo($"[NpcPos] moradores devolvidos à agenda ({Puppets.Count})");
        Puppets.Clear();
        ShowExtras();
        PopTried.Clear();
        Parked.RemoveAll(n => n == null);
        _active = false;
        _states = null;
    }

    public static object Status() => new
    {
        active = _active,
        reference = _ref,
        refField = _refField,
        refClock = _refTicks > 0 ? new BokuMonoDateTime(_refTicks).ToString() : "-",
        localClock = DateManager.IsInstance ? DateManager.Instance.Now.ToString() : "-",
        puppets = Puppets.Count,
        hidden = CountHidden(),
        extras = Extras.Count,
        parked = Parked.Count,
        sent = Sent,
        received = Received,
        popped = Popped,
        bazaarGoverning = BazaarViewSync.Governing,
    };

    private static int CountHidden()
    {
        var n = 0;
        foreach (var p in Puppets.Values) if (p.Hidden) n++;
        return n;
    }
}
