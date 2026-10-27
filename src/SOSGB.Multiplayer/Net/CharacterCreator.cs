// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d72.
using System;
using BokuMono;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Criar o personagem sem jogo novo (D72, pedido do usuário: "como que vai fazer pra criar o personagem sem ter que criar um
/// jogo novo e ver as cutscenes pra ela entrar com o boneco dela direto no meu mundo").
///  - A tela de criação COMPLETA do jogo novo (rosto, pele, olhos, cabelo, barba, sinais, tipo físico, voz) abre dentro do
///    mundo com UIAccessor.RequestOpenAvatarMakeup(AvatarMakeCategory.Prologue). Os modos Direct e a chave de tela crua
///    (UILoadKey.AvatarMake) não montam a tela (testado: ficam presos).
///  - No jogo novo essa tela roda dentro de um evento, que clareia a tela depois dela. Fora do evento a tela ficava preta
///    ("carregando") e o relógio parado. Ao fechar, o mod faz esse papel (FadeManager.FadeIn).
///  - Abre sozinha na primeira entrada de quem entrou sem save (pela tela inicial) e pelo botão "Mudar visual" do F9.
///  - O visual escolhido é a aparência do jogador: o AppearanceSync anuncia aos outros e o perfil guarda.
/// </summary>
public class CharacterCreator : MonoBehaviour
{
    public CharacterCreator(IntPtr ptr) : base(ptr) { }

    private static bool _pendingOpen;
    private static float _openAt;
    private static bool _open;
    private static float _openedAt;
    private static float _closedAt = -1;
    private static UIManager _ui;

    /// <summary>A tela de criação está aberta (ou fechando): ninguém mais mexe no modelo do jogador local.</summary>
    public static bool Active => _open;

    private static float _avatarMakeSeenAt = -999f;

    /// <summary>Uma tela de aparência do jogo está aberta agora: avatares dos outros e painéis do mod saem da frente (D75).</summary>
    public static bool OnScreen { get; private set; }

    /// <summary>
    /// Alguma tela de aparência do jogo (criação, guarda-roupa, loja de roupas) está aberta, ou fechou há menos de 3 s
    /// (o jogo ainda está aplicando o visual novo no personagem local). Montar avatares nesse tempo copiava o visual errado:
    /// o avatar do outro jogador saía com a roupa de quem estava no guarda-roupa (D75, visto pelo usuário).
    /// </summary>
    public static bool AvatarMakeBusy => _open || Time.unscaledTime - _avatarMakeSeenAt < 3f;

    /// <summary>Abre assim que der (sem evento, sem tela aberta, mundo pronto).</summary>
    public static void OpenSoon(float delay = 2f)
    {
        _pendingOpen = true;
        _openAt = Time.unscaledTime + delay;
    }

    public static bool CanOpen() =>
        !_open && SceneManager.IsInstance && SceneManager.Instance.CurrentSceneMode == SceneMode.Game
        && GameController.IsInstance && GameController.Instance.GetPlayer() != null && !GameController.Instance.IsPlayingEvent
        && UIAccessor.IsInstance && !DaySync.InDayChange && !DaySync.LocalSleeping && !AppearanceSync.Swapping
        && CurrentUi() == UILoadKey.GameMain;

    public static void Open()
    {
        UIAccessor.Instance.RequestOpenAvatarMakeup(AvatarMakeCategory.Prologue, null);
        _open = true;
        _openedAt = Time.unscaledTime;
        _closedAt = -1;
        Plugin.Logger.LogInfo("[Personagem] tela de criação aberta");
        DevBridge.PushEvent("personagem:aberto");
    }

    private static bool AvatarMakeOnScreen()
    {
        if (CurrentUi() == UILoadKey.AvatarMake) return true;
        // Com um diálogo por cima (ex.: "sair sem salvar?"), a tela atual é a do diálogo e IsMenu fica falso, mas o menu de
        // fundo continua sendo a tela de aparência: ainda está aberta (D79).
        try { return _ui != null && _ui.UIMenuKey == UILoadKey.AvatarMake; } catch { return false; }
    }

    private static UILoadKey CurrentUi()
    {
        try
        {
            if (_ui == null) _ui = FindObjectOfType<UIManager>();
            return _ui != null ? _ui.CurrentUIKey : UILoadKey.None;
        }
        catch { _ui = null; return UILoadKey.None; }
    }

    private void Update()
    {
        try
        {
            OnScreen = AvatarMakeOnScreen();
            if (OnScreen) _avatarMakeSeenAt = Time.unscaledTime;
            if (_pendingOpen && Time.unscaledTime >= _openAt && CanOpen())
            {
                _pendingOpen = false;
                Open();
                GameNotify.Show("Bem-vindo! Crie o seu personagem (depois dá para mudar no F9).");
            }
            if (!_open) return;
            // Fechou: a tela do jogo voltou à principal. Clareia a tela, como o evento do jogo novo faria. (Com a confirmação
            // "Começar com este visual?" aberta, a tela atual é a do diálogo: ainda não fechou.)
            var key = CurrentUi();
            if (key != UILoadKey.GameMain || Time.unscaledTime - _openedAt < 2f) { _closedAt = -1; return; }
            if (_closedAt < 0) { _closedAt = Time.unscaledTime; return; }
            if (Time.unscaledTime - _closedAt < 0.5f) return;
            _open = false;
            if (FadeManager.IsInstance && FadeManager.IsFadeOut) FadeManager.FadeIn(null, null, 0f, 0f);
            Plugin.Logger.LogInfo($"[Personagem] tela de criação fechada (tela atual {key}); aparência {AppearanceSync.Local()}");
            DevBridge.PushEvent("personagem:fechado");
            AppearanceSync.Announce();
            if (NetSession.Current == NetSession.Mode.Client) PlayerProfiles.Upload(); // o visual novo já fica guardado no host
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[Personagem] {e.Message}"); _open = false; }
    }

    public static object Status() => new { open = _open, pending = _pendingOpen, ui = CurrentUi().ToString(), fadeOut = FadeManager.IsInstance && FadeManager.IsFadeOut };
}
