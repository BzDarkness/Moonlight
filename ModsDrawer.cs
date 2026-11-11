using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MoonlightLauncher;

// Aba Mods:
//  • Gaveta "Mods instalados": abre/fecha pela setinha na borda direita; fechada, sobra mais espaço
//    e aparecem mais mods lado a lado (nos modos Lista e Detalhado).
//  • Barras que somem ao rolar para baixo (título, sub-abas, busca e filtros) e voltam ao rolar para cima,
//    como o cabeçalho de um site. No lugar delas fica só uma mini barra para voltar à busca.
public partial class MainWindow
{
    // ---------- Gaveta dos mods instalados ----------

    private const double DrawerWidth = 290; // 272 do painel + 18 de espaço
    private bool _drawerOpen;
    private double _drawerP;                 // 0 = fechada, 1 = aberta
    private readonly FrameAnim _drawerAnim = new();
    private double? _colsWidthOverride;

    private void DrawerHandle_Click(object sender, RoutedEventArgs e) => SetDrawer(!_drawerOpen);

    private void ApplyDrawer(double p)
    {
        DrawerCol.Width = new GridLength(DrawerWidth * p);
        HandleT.X = -DrawerWidth * p;          // a setinha acompanha a borda da gaveta
        HandleArrowRot.Angle = 180 * p;        // ‹ vira › quando aberta
    }

    private void SetDrawer(bool open)
    {
        _drawerOpen = open;
        var from = _drawerP;
        var to = open ? 1.0 : 0.0;

        if (open)
        {
            DrawerClip.Visibility = Visibility.Visible;
            _ = RefreshInstalledModsAsync(); // pode ter entrado mod novo desde a última vez
        }

        // Já calcula as colunas pela largura que sobrará no final.
        _colsWidthOverride = Math.Max(300, PageMods.ActualWidth - (open ? DrawerWidth : 0));

        if (Perf.Light || !IsLoaded)
        {
            _drawerAnim.Stop();
            _drawerP = to;
            ApplyDrawer(to);
            FinishDrawer();
            return;
        }

        var colsApplied = false;
        var ms = 380 * Math.Max(0.4, Math.Abs(to - from));

        _drawerAnim.Run(ms, t =>
        {
            _drawerP = from + (to - from) * FrameAnim.EaseOutCubic(t);
            ApplyDrawer(_drawerP);

            // Uma leve "piscada" nos resultados esconde a troca de colunas.
            ModsResultsGrid.Opacity = 1 - 0.5 * Math.Sin(Math.PI * t);
            if (!colsApplied && t >= 0.22)
            {
                colsApplied = true;
                UpdateCardColumns();
            }
        }, FinishDrawer);
    }

    private void FinishDrawer()
    {
        ModsResultsGrid.Opacity = 1;
        _colsWidthOverride = null;
        UpdateCardColumns();
        if (!_drawerOpen) DrawerClip.Visibility = Visibility.Collapsed;
    }

    // ---------- Barras que somem ao rolar ----------

    private bool _barsHidden;
    private double _barsRaw = 1;               // 1 = tudo à mostra, 0 = tudo escondido
    private readonly FrameAnim _barsAnim = new();
    private double _scrollLast;
    private double _scrollAccum;
    private DateTime _barsLock = DateTime.MinValue;

    // Cada barra tem a sua janela de tempo: aparecem de cima para baixo e somem de baixo para cima.
    private static double Stage(double raw, int index)
    {
        var t = Math.Clamp((raw - index * 0.17) / 0.66, 0, 1);
        return FrameAnim.EaseOutCubic(t);
    }

    private void ApplyBars(double raw)
    {
        RevealTabs.Progress = Stage(raw, 0);
        RevealFilter.Progress = Stage(raw, 1);
        RevealExtra.Progress = Stage(raw, 2);

        var g = FrameAnim.EaseInOutCubic(raw);
        TitleRow.Height = new GridLength(38 + 18 * g);   // 56 normal, 38 só com os botões da janela
        TitleText.Opacity = g;
        TitleTextT.Y = -12 * (1 - g);
        MiniBar.Opacity = 1 - g;
        MiniBarT.Y = 8 * g;
        MiniBar.IsHitTestVisible = raw < 0.5;
    }

    private void SetModsBars(bool show, bool instant = false)
    {
        if (!instant && show == !_barsHidden && !_barsAnim.Running) return;

        _barsHidden = !show;
        _scrollAccum = 0;
        _barsLock = DateTime.UtcNow.AddMilliseconds(450); // ignora a rolagem causada pela própria animação
        if (!show) UpdateMiniBarText();

        var from = _barsRaw;
        var to = show ? 1.0 : 0.0;

        if (instant || Perf.Light || !IsLoaded)
        {
            _barsAnim.Stop();
            _barsRaw = to;
            ApplyBars(to);
            return;
        }

        _barsAnim.Run(560 * Math.Max(0.4, Math.Abs(to - from)), t =>
        {
            _barsRaw = from + (to - from) * t; // linear: o "easing" está dentro de Stage()
            ApplyBars(_barsRaw);
        });
    }

    private void UpdateMiniBarText()
    {
        var q = ModsSearchBox.Text.Trim();
        var kind = _modsType switch
        {
            "modpack" => "modpacks",
            "resourcepack" => "texturas",
            "shader" => "shaders",
            _ => "mods"
        };
        var left = q.Length > 0 ? $"“{q}”" : $"Buscar {kind}";
        MiniBarText.Text = _modsTotal > 0 ? $"{left}  ·  {_modsTotal:N0} resultados" : left;
    }

    private void MiniBar_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // não deixa virar "arrastar a janela"
        SetModsBars(true);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ModsSearchBox.Focus();
            ModsSearchBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    // Rodinha para cima: as barras voltam na hora, mesmo no meio da lista.
    private void ModsResults_PreviewWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0 && _barsHidden) SetModsBars(true);
    }

    // Rolagem para baixo esconde; para cima mostra; no topo da lista fica sempre tudo à mostra.
    private void ModsResults_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer sv) return;

        var off = sv.VerticalOffset;
        var layoutDriven = e.ViewportHeightChange != 0 || e.ExtentHeightChange != 0;
        var delta = off - _scrollLast;
        _scrollLast = off;

        if (layoutDriven || DateTime.UtcNow < _barsLock) return;

        if (off < 6)
        {
            _scrollAccum = 0;
            if (_barsHidden) SetModsBars(true);
            return;
        }

        if (delta == 0) return;
        _scrollAccum = (delta > 0) == (_scrollAccum > 0) ? _scrollAccum + delta : delta;

        // Só esconde se a lista continuar tendo o que rolar depois que as barras sumirem.
        if (_scrollAccum > 30 && !_barsHidden && sv.ExtentHeight > sv.ViewportHeight + 280)
            SetModsBars(false);
        else if (_scrollAccum < -12 && _barsHidden)
            SetModsBars(true);
    }
}
