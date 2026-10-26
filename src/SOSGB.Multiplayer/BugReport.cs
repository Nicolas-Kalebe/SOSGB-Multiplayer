// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md#d71.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace SOSGB.Multiplayer;

/// <summary>
/// Relatório para mandar ao desenvolvedor quando algo der errado (D71, pedido do usuário: "faça um logdump depois pra gente
/// poder te enviar").
///  - Log de cada sessão: o BepInEx apaga o LogOutput.log toda vez que o jogo abre, então travar e abrir de novo perdia o log
///    do erro. Cada sessão também vai, linha a linha, para SOSGB-MP\logs\sessao-*.log (ficam as últimas 8).
///  - "Gerar log para enviar" (painel do título e F9): um .zip na Área de Trabalho com os logs das últimas sessões, o
///    LogOutput.log atual, o Player.log da Unity, o .cfg do mod, o estado de cada parte do mod e os perfis dos jogadores.
/// </summary>
public static class BugReport
{
    private const int KeepSessions = 8;
    private const long MaxDataFile = 5 * 1024 * 1024;
    private static SessionListener _listener;

    /// <summary>Pasta de dados do mod (perfis, recuperação, logs). No modo de teste, a subpasta "dev".</summary>
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "STORY OF SEASONS Grand Bazaar", "SOSGB-MP");
            return Plugin.DevMode ? Path.Combine(dir, "dev") : dir;
        }
    }

    public static string LogsDir => Path.Combine(DataDir, "logs");

    /// <summary>Começa o log desta sessão (chamado ao carregar o plugin) e apaga os mais antigos.</summary>
    public static void StartSessionLog()
    {
        try
        {
            Directory.CreateDirectory(LogsDir);
            var path = Path.Combine(LogsDir, $"sessao-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}{Plugin.InstanceTag}.log");
            _listener = new SessionListener(path);
            BepInEx.Logging.Logger.Listeners.Add(_listener);
            foreach (var old in Directory.GetFiles(LogsDir, "sessao-*.log").OrderByDescending(f => f).Skip(KeepSessions))
                try { File.Delete(old); } catch { /* em uso por outra instância */ }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BugReport] log da sessão: {e.Message}"); }
    }

    /// <summary>Monta o .zip na Área de Trabalho e abre a pasta com ele selecionado. Devolve o caminho.</summary>
    public static string Create()
    {
        var who = new string((string.IsNullOrWhiteSpace(Plugin.PlayerName.Value) ? Environment.UserName : Plugin.PlayerName.Value)
            .Where(char.IsLetterOrDigit).ToArray());
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop)) desktop = DataDir;
        var zipPath = Path.Combine(desktop, $"SOSGB-log-{who}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip");
        Plugin.Logger.LogInfo($"[BugReport] gerando {zipPath}");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddText(zip, "LEIA.txt", Header());
            AddText(zip, "estado.json", Safe(DevBridge.StatusJson));
            foreach (var f in Directory.GetFiles(Paths.BepInExRootPath, "LogOutput*.log")) AddFile(zip, f, Path.GetFileName(f));
            if (Directory.Exists(LogsDir))
                foreach (var f in Directory.GetFiles(LogsDir, "sessao-*.log").OrderByDescending(f => f).Take(KeepSessions))
                    AddFile(zip, f, "sessoes/" + Path.GetFileName(f));
            var unityLog = Safe(() => Application.consoleLogPath);
            if (!string.IsNullOrEmpty(unityLog))
            {
                AddFile(zip, unityLog, "Player.log");
                AddFile(zip, Path.Combine(Path.GetDirectoryName(unityLog) ?? "", "Player-prev.log"), "Player-prev.log");
            }
            AddFile(zip, Plugin.PlayerName.ConfigFile.ConfigFilePath, "config.cfg");
            if (Directory.Exists(DataDir))
            {
                foreach (var f in Directory.GetFiles(DataDir, "*.json")) AddFile(zip, f, "dados/" + Path.GetFileName(f));
                var recovery = Directory.GetFiles(DataDir, "recovery_*.sav").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (recovery != null) AddFile(zip, recovery, "dados/" + Path.GetFileName(recovery));
            }
        }
        Plugin.Logger.LogInfo($"[BugReport] pronto: {zipPath} ({new FileInfo(zipPath).Length / 1024} KB)");
        try { Process.Start("explorer.exe", $"/select,\"{zipPath}\""); } catch { /* sem Explorer: o aviso diz onde está */ }
        return zipPath;
    }

    private static string Header()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Relatório do SOSGB Multiplayer — mande este .zip para quem cuida do mod.");
        sb.AppendLine($"Gerado em: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Versão do mod: {Plugin.Version} (protocolo {Net.Protocol.Version})");
        sb.AppendLine($"Jogador: {Net.NetSession.LocalName}");
        sb.AppendLine($"Modo: {Net.NetSession.Current}");
        sb.AppendLine($"Jogadores: {Safe(() => string.Join(", ", Net.NetSession.Players.Values.Select(p => p.Name)))}");
        sb.AppendLine($"Cena/estado: {Safe(() => string.Join("; ", GameState.Snapshot().Select(kv => $"{kv.Key}={Show(kv.Value)}")))}");
        sb.AppendLine();
        sb.AppendLine("Conteúdo: LogOutput*.log (sessão atual), sessoes/ (logs das últimas sessões, inclusive as que travaram),");
        sb.AppendLine("Player.log (log da Unity), config.cfg (configuração do mod), estado.json (estado do mod agora), dados/ (perfis).");
        return sb.ToString();
    }

    private static string Show(object v) => v is System.Collections.IEnumerable e && v is not string
        ? "[" + string.Join(", ", e.Cast<object>()) + "]" : v?.ToString();

    private static string Safe(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return "(erro: " + e.Message + ")"; }
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        w.Write(text ?? "");
    }

    /// <summary>Copia um arquivo para o .zip mesmo que esteja aberto para escrita (o log da sessão atual está).</summary>
    private static void AddFile(ZipArchive zip, string path, string name)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || new FileInfo(path).Length > MaxDataFile && name.StartsWith("dados/")) return;
            using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = zip.CreateEntry(name).Open();
            src.CopyTo(dst);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BugReport] {name}: {e.Message}"); }
    }

    /// <summary>Copia cada linha do log do BepInEx (mod, jogo e Unity) para o arquivo da sessão, gravando na hora.</summary>
    private sealed class SessionListener : ILogListener
    {
        private readonly StreamWriter _w;

        public SessionListener(string path) =>
            _w = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };

        public LogLevel LogLevelFilter => LogLevel.Fatal | LogLevel.Error | LogLevel.Warning | LogLevel.Message | LogLevel.Info;

        public void LogEvent(object sender, LogEventArgs e)
        {
            lock (_w)
            {
                try { _w.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{e.Level,-7}:{e.Source?.SourceName}] {e.Data}"); }
                catch { /* disco cheio ou arquivo fechado: o log do BepInEx continua */ }
            }
        }

        public void Dispose()
        {
            lock (_w) _w.Dispose();
        }
    }
}
