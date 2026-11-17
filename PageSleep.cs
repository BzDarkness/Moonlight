using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace MoonlightLauncher;

/// <summary>
/// Abas que dormem: só a aba aberta fica ativa. Ao sair de uma aba ela "dorme":
///  • as listas dela soltam o conteúdo (os cartões, ícones e miniaturas saem da memória);
///  • os bonecos 3D dela soltam as imagens de desenho;
///  • o editor de skin para;
///  • depois de um instante o launcher devolve a memória livre ao Windows.
/// Ao voltar para a aba ela "acorda" e remonta tudo do jeito que estava (lista, seleção, busca).
/// Os dados em si (contas, amigos, mensagens, rede) continuam vivos: só a parte visual dorme,
/// então nada de mensagem ou chamada se perde.
/// </summary>
public partial class MainWindow
{
    private static readonly string[] AllTabs =
        { "Play", "Mods", "Accounts", "Skins", "Folders", "Settings", "Golden", "Friends" };

    private readonly HashSet<string> _sleepingTabs = new();
    private readonly Dictionary<ItemsControl, (IEnumerable Source, object? Selected)> _sleepStash = new();
    private DispatcherTimer? _trimTimer;

    /// <summary>Deixa só a aba <paramref name="tag"/> ativa: todas as outras dormem.</summary>
    private void SetActiveTab(string tag)
    {
        var slept = false;

        foreach (var t in AllTabs)
        {
            if (t == tag || !_sleepingTabs.Add(t)) continue;
            try { SleepTab(t); slept = true; }
            catch (Exception ex) { LauncherLog.Write($"Aba {t}: erro ao dormir", ex); }
        }

        if (_sleepingTabs.Remove(tag))
        {
            try { WakeTab(tag); }
            catch (Exception ex) { LauncherLog.Write($"Aba {tag}: erro ao acordar", ex); }
        }

        if (slept) ScheduleTrim();
    }

    private void SleepTab(string tag)
    {
        switch (tag)
        {
            case "Play":
                VersionCardsPanel.Children.Clear();   // cartões com banner da visão detalhada
                _versionCards.Clear();
                _heroFor = null;
                HomeSkinView.ReleaseMemory();
                break;

            case "Mods":
                StashItems(ModsList, ModsCards, InstalledList);
                break;

            case "Skins":
                StashItems(SkinLibList);
                SkinView.ReleaseMemory();
                SkinEditorView.Paused = true;
                break;

            case "Friends":
                StashItems(ChatItems, SearchList);
                break;
        }
    }

    private void WakeTab(string tag)
    {
        switch (tag)
        {
            case "Play":
                RebuildVersionCards();                // refaz os cartões (e o destaque) da versão escolhida
                break;

            case "Mods":
                RestoreItems(ModsList, ModsCards, InstalledList);
                UpdateInstalledSummary();
                break;

            case "Skins":
                RestoreItems(SkinLibList);
                SkinEditorView.Paused = _renderPaused;
                break;

            case "Friends":
                RestoreItems(ChatItems, SearchList);
                Dispatcher.BeginInvoke(() => ChatScroll.ScrollToEnd(), DispatcherPriority.Loaded);
                break;
        }
    }

    // Guarda a fonte de itens (e a seleção) de uma lista e esvazia a parte visual dela.
    private void StashItems(params ItemsControl[] lists)
    {
        foreach (var l in lists)
        {
            if (l.ItemsSource is not IEnumerable src) continue;
            _sleepStash[l] = (src, (l as Selector)?.SelectedItem);
            l.ItemsSource = null;
        }
    }

    // Devolve a fonte guardada. Se algo já colocou itens novos enquanto a aba dormia (ex.: uma busca
    // que terminou), os itens novos ficam e o guardado é descartado.
    private void RestoreItems(params ItemsControl[] lists)
    {
        foreach (var l in lists)
        {
            if (!_sleepStash.Remove(l, out var saved)) continue;
            if (l.ItemsSource != null) continue;

            l.ItemsSource = saved.Source;
            if (saved.Selected != null && l is Selector sel)
            {
                try { sel.SelectedItem = saved.Selected; }
                catch { /* o item não existe mais: fica sem seleção */ }
            }
        }
    }

    // Devolve a memória ao Windows só depois que o jogador parou de trocar de aba (sem engasgar a troca).
    private void ScheduleTrim()
    {
        _trimTimer ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(1.5) };
        _trimTimer.Tick -= TrimTick;
        _trimTimer.Tick += TrimTick;
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    private void TrimTick(object? sender, EventArgs e)
    {
        _trimTimer?.Stop();
        Perf.TrimMemory();
    }
}
