// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e CODIGO.md.
using System;
using System.Collections.Generic;
using BokuMono;

namespace SOSGB.Multiplayer;

/// <summary>
/// Foto do estado do jogo lida dos singletons mapeados em CODIGO.md.
/// Usada pelo DebugOverlay (tela) e pela DevBridge (log/comandos, D20).
/// Cada leitura é isolada: se um singleton ainda não existe, o campo fica null.
/// </summary>
public static class GameState
{
    public static Dictionary<string, object> Snapshot()
    {
        var s = new Dictionary<string, object>
        {
            ["t"] = DateTime.Now.ToString("HH:mm:ss.fff"),
            ["scene"] = Try(() => SceneManager.IsInstance ? SceneName(SceneManager.Instance.CurrentSceneMode) : null),
            ["money"] = Try<object>(() =>
                ExternalData.IsInstance && ExternalData.Instance.UserInfo != null
                    ? ExternalData.Instance.UserInfo.PlayerMoney
                    : null),
        };

        Try(() =>
        {
            if (!DateManager.IsInstance) return 0;
            var dm = DateManager.Instance;
            var n = dm.Now;
            s["date"] = $"{n.Year}/{n.Month:00}/{n.Day:00} {n.Hour:00}:{n.Minute:00}";
            s["clockRunning"] = dm.IsPlay();             // flag bruta (Pause/Resume)
            s["clockUpdateEnabled"] = dm.IsUpdateEnabled; // trava efetiva (inclui menus/eventos?) — tarefa 1.1
            s["clockTimeScale"] = dm.TimeScale;
            return 0;
        });

        // Diagnóstico de foco: a Unity pode congelar o jogo quando a janela perde o foco.
        s["focused"] = Try<object>(() => UnityEngine.Application.isFocused);
        s["runInBackground"] = Try<object>(() => UnityEngine.Application.runInBackground);
        s["timeScale"] = Try<object>(() => UnityEngine.Time.timeScale);
        s["suspended"] = Try<object>(() => AppManager.IsInstance ? AppManager.Instance.IsSuspended : null);

        s["field"] = Try<object>(() => FieldManager.IsInstance ? FieldManager.Instance.CurrentFieldId : null);

        Try(() =>
        {
            if (!GameController.IsInstance) return 0;
            var gc = GameController.Instance;
            s["playingEvent"] = gc.IsPlayingEvent;
            s["sleeping"] = gc.IsSleeping;
            var t = gc.GetPlayerTransform();
            if (t != null)
            {
                var p = t.position;
                s["pos"] = new[] { Round(p.x), Round(p.y), Round(p.z) };
            }
            return 0;
        });

        return s;
    }

    // No interop, as constantes de SceneMode viram campos estáticos (não const), então não cabem em switch.
    public static string SceneName(int mode)
    {
        if (mode == SceneMode.Launcher) return "Launcher";
        if (mode == SceneMode.Title) return "Title";
        if (mode == SceneMode.Game) return "Game";
        if (mode == SceneMode.None) return "None";
        return mode.ToString();
    }

    private static float Round(float v) => (float)Math.Round(v, 2);

    private static T Try<T>(Func<T> f)
    {
        try { return f(); }
        catch { return default; }
    }
}
