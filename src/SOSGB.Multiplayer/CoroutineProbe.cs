// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md#d29.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SOSGB.Multiplayer;

/// <summary>
/// Executa uma corrotina IL2CPP do jogo "na mão", descendo nas corrotinas aninhadas, com TEMPO LIMITE de verdade.
///
/// Por que existe: com <c>yield return ie.Current</c>, a Unity roda as corrotinas aninhadas por conta própria e,
/// se uma delas nunca termina, quem chamou fica preso para sempre (o tempo limite nunca dispara — D29).
/// Aqui cada nível (IEnumerator, WaitUntil/WaitWhile, CoroutineWaiter…) é avançado por nós, quadro a quadro.
/// Só WaitForSeconds/fim de quadro/AsyncOperation são devolvidos à Unity (eles sempre terminam).
///
/// Ao estourar o tempo, <see cref="Result.Stack"/> diz ONDE travou: a pilha de tipos (ex.: a máquina de estados
/// "&lt;CreatePlayerModel&gt;d__127" esperando um "WaitUntil") com o estado e os campos de cada nível.
/// </summary>
public static class CoroutineProbe
{
    public sealed class Result
    {
        public bool Finished;
        public bool TimedOut;
        public float Seconds;
        public int Steps;
        public string Stack = "";
        public readonly List<string> Trail = new(); // mudanças de pilha (para entender o caminho)
    }

    public static IEnumerator Run(Il2CppSystem.Collections.IEnumerator root, float timeout, Result result, string label = "probe")
    {
        var stack = new List<Il2CppSystem.Collections.IEnumerator> { root };
        var t0 = Time.realtimeSinceStartup;
        var lastShape = "";
        while (stack.Count > 0)
        {
            if (Time.realtimeSinceStartup - t0 > timeout)
            {
                result.TimedOut = true;
                result.Stack = Describe(stack, true);
                Plugin.Logger.LogWarning($"[{label}] tempo limite ({timeout:0}s). Travado em:\n{result.Stack}");
                break;
            }
            var top = stack[stack.Count - 1];
            bool more;
            try { more = top.MoveNext(); }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"[{label}] exceção em {TypeName(top)}: {e.Message}");
                stack.RemoveAt(stack.Count - 1);
                continue;
            }
            result.Steps++;
            if (!more) { stack.RemoveAt(stack.Count - 1); continue; }

            var cur = top.Current;
            var shape = Describe(stack, false);
            if (shape != lastShape) { lastShape = shape; if (result.Trail.Count < 200) result.Trail.Add($"{Time.realtimeSinceStartup - t0:0.00}s {shape}"); }

            if (cur == null) { yield return null; continue; }
            var nested = cur.TryCast<Il2CppSystem.Collections.IEnumerator>();
            if (nested != null)
            {
                // WaitUntil/WaitWhile (CustomYieldInstruction) e corrotinas aninhadas: descemos nelas.
                stack.Add(nested);
                continue;
            }
            // WaitForSeconds, WaitForEndOfFrame, AsyncOperation, Coroutine: a Unity resolve.
            // (Coroutine = uma corrotina já iniciada à parte; se ela travar, travamos junto — fica no Trail.)
            if (cur.TryCast<Coroutine>() != null && result.Trail.Count < 200) result.Trail.Add($"  ↳ Coroutine externa em {TypeName(top)}");
            yield return cur;
        }
        result.Finished = !result.TimedOut;
        result.Seconds = Time.realtimeSinceStartup - t0;
    }

    private static string TypeName(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase b)
    {
        try { return b.TryCast<Il2CppSystem.Object>().GetIl2CppType().FullName; } catch { return "?"; }
    }

    private static string Describe(List<Il2CppSystem.Collections.IEnumerator> stack, bool withFields)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < stack.Count; i++)
        {
            var o = stack[i];
            sb.Append(withFields ? $"  [{i}] " : (i == 0 ? "" : " > "));
            sb.Append(Short(TypeName(o)));
            var state = Field(o, "<>1__state");
            if (state != null) sb.Append($"#{state}");
            if (withFields) { sb.Append('\n'); sb.Append(Fields(o)); }
        }
        return sb.ToString();
    }

    private static string Short(string fullName)
    {
        var i = fullName.LastIndexOf('+');
        return i >= 0 ? fullName.Substring(i + 1) : fullName;
    }

    private static string Field(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase b, string name)
    {
        try
        {
            var o = b.TryCast<Il2CppSystem.Object>();
            var f = o.GetIl2CppType().GetField(name, Il2CppSystem.Reflection.BindingFlags.Instance
                | Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(o)?.ToString();
        }
        catch { return null; }
    }

    /// <summary>Campos do nível (estado, contadores, flags, objetos) — mostram o que ele espera.</summary>
    private static string Fields(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase b)
    {
        var sb = new StringBuilder();
        try
        {
            var o = b.TryCast<Il2CppSystem.Object>();
            var fields = o.GetIl2CppType().GetFields(Il2CppSystem.Reflection.BindingFlags.Instance
                | Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic);
            foreach (var f in fields)
            {
                string v;
                try { v = f.GetValue(o)?.ToString() ?? "null"; } catch { v = "?"; }
                if (v.Length > 80) v = v.Substring(0, 80) + "…";
                sb.Append($"       {f.Name} = {v}\n");
            }
        }
        catch (Exception e) { sb.Append($"       (campos: {e.Message})\n"); }
        return sb.ToString();
    }
}
