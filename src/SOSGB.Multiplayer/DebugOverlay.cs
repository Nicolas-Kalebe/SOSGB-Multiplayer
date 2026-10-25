// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md#d19.
using System;
using BokuMono;
using UnityEngine;

namespace SOSGB.Multiplayer;

/// <summary>
/// Janela de diagnóstico (F8 liga/desliga). Lê direto dos singletons do jogo
/// mapeados em CODIGO.md: dinheiro (UserInfo), relógio (DateManager) e mapa (FieldManager).
/// Se isso funcionar, a base do mod (ler e depois controlar o estado) está validada.
/// </summary>
public class DebugOverlay : MonoBehaviour
{
    // Construtor exigido pelo Il2CppInterop para tipos injetados.
    public DebugOverlay(IntPtr ptr) : base(ptr) { }

    private bool _visible = true;
    private bool _legacyInputOk = true;
    private string _text = "aguardando o jogo...";
    private float _nextRefresh;
    private string _lastError;

    private void Update()
    {
        // O jogo usa o Unity.InputSystem; o Input legado pode estar desligado e lançar
        // exceção. Nesse caso o atalho F8 fica desativado e a janela fica sempre visível.
        if (_legacyInputOk)
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F8))
                    _visible = !_visible;
            }
            catch (Exception)
            {
                _legacyInputOk = false;
                Plugin.Logger.LogWarning("Input legado indisponível; atalho F8 desativado.");
            }
        }

        // Ler a cada 0,5 s basta para diagnóstico e evita custo por frame.
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.5f;
        _text = BuildText();
    }

    private string BuildText()
    {
        try
        {
            // Singletons só existem depois que um save é carregado (cena Game).
            var money = ExternalData.IsInstance && ExternalData.Instance.UserInfo != null
                ? ExternalData.Instance.UserInfo.PlayerMoney.ToString("N0") + " G"
                : "-";

            var clock = "-";
            if (DateManager.IsInstance)
            {
                var dm = DateManager.Instance;
                var now = dm.Now;
                clock = $"{now.Year}/{now.Month:00}/{now.Day:00} {now.Hour:00}:{now.Minute:00}  " +
                        (dm.IsPlay() ? "▶ correndo" : "⏸ pausado");
            }

            var field = FieldManager.IsInstance ? FieldManager.Instance.CurrentFieldId.ToString() : "-";

            var player = "-";
            if (GameController.IsInstance)
            {
                var t = GameController.Instance.GetPlayerTransform();
                if (t != null)
                {
                    var p = t.position;
                    player = $"({p.x:0.0}, {p.y:0.0}, {p.z:0.0})";
                }
            }

            _lastError = null;
            return $"Dinheiro: {money}\nRelógio: {clock}\nMapa (fieldId): {field}\nPlayer: {player}";
        }
        catch (Exception e)
        {
            // Loga só quando o erro muda, para não inundar o LogOutput.log.
            if (_lastError != e.Message)
            {
                _lastError = e.Message;
                Plugin.Logger.LogWarning($"DebugOverlay: {e}");
            }
            return "erro lendo o jogo (ver BepInEx/LogOutput.log)";
        }
    }

    private void OnGUI()
    {
        if (!_visible) return;
        // Canto inferior esquerdo: o superior esquerdo é da barra de stamina do jogo (tarefa 1.7).
        var y = Screen.height - 125;
        GUI.Box(new Rect(10, y, 380, 105), $"{Plugin.Name} {Plugin.Version}{Plugin.InstanceTag}  [F8]");
        GUI.Label(new Rect(20, y + 22, 360, 80), _text);
    }
}
