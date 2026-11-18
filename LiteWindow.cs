using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MoonlightLauncher;

/// <summary>Item simples das listas da interface leve (o texto aparece, o valor fica guardado).</summary>
public sealed record LiteItem(string Text, object? Value)
{
    public override string ToString() => Text;
}

/// <summary>
/// Interface LEVE: uma janelinha simples (conta, versão, Jogar e quatro botões) num cartão de vidro,
/// com o fundo da lua à noite. Sem animações, sem estrelas, sem 3D, sem amigos na tela: quase não usa memória.
/// Toda a lógica (contas, versões, instalar e abrir o jogo) continua na MainWindow; esta janela só mostra e pede.
/// </summary>
public sealed class LiteWindow : Window
{
    private readonly MainWindow _main;
    private readonly ComboBox _accountBox = new();
    private readonly ComboBox _loaderBox = new();
    private readonly ComboBox _versionBox = new();
    private readonly CheckBox _onlyInstalled = new();
    private readonly CheckBox _hideOnPlay = new();
    private readonly Button _playBtn = new();
    private readonly ProgressBar _bar = new();
    private readonly TextBlock _status = new();
    private readonly DispatcherTimer _timer;

    private bool _updating;
    private bool _wasBusy;
    private string _lastStatus = "";

    /// <summary>A MainWindow liga isto antes de fechar esta janela de propósito (troca de modo): o launcher não fecha.</summary>
    internal bool Switching { get; set; }

    public LiteWindow(MainWindow main)
    {
        _main = main;

        Title = "Moonlight Launcher " + Updater.CurrentText + " [Leve]";
        Width = 960;
        Height = 600;
        MinWidth = 640;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x07, 0x09, 0x16));
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/icon.ico")); }
        catch { /* sem ícone: não é crítico */ }

        var root = new Grid();
        var bg = LoadBackground();
        if (bg != null)
        {
            var img = new Image { Source = bg, Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.LowQuality);
            root.Children.Add(img);
        }
        // Um véu que escurece de leve o céu e mais embaixo, só para o cartão e o texto sempre ficarem legíveis.
        root.Children.Add(new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(0x18, 0, 0, 0), Color.FromArgb(0x80, 0x05, 0x07, 0x14), 90),
            IsHitTestVisible = false
        });
        root.Children.Add(BuildPanel());
        Content = root;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => Tick();

        Loaded += (_, _) =>
        {
            RefreshAccounts();
            RefreshVersions();
            Tick();
            _timer.Start();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _timer.Start(); else _timer.Stop();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (!Switching) _main.Close(); // fechar a janelinha fecha o launcher
        };
    }

    // ---------- Visual ----------

    private static BitmapImage? LoadImage(string uri, int decodeWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(uri);
            bmp.DecodePixelWidth = decodeWidth;     // não guarda mais pixels do que precisa
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Interface leve: imagem não carregou (" + uri + ")", ex);
            return null;
        }
    }

    private static BitmapImage? LoadBackground() => LoadImage("pack://application:,,,/Assets/lite/bg.jpg", 1280);

    private static Style ParseStyle(string xaml, params (string Key, string Value)[] map)
    {
        foreach (var (k, v) in map) xaml = xaml.Replace(k, v);
        return (Style)XamlReader.Parse(xaml);
    }

    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    // Botão chapado (usado nas janelinhas): fundo, fundo ao passar o mouse e ao apertar.
    private const string FlatButtonXaml = @"
<Style " + Ns + @" TargetType=""Button"">
  <Setter Property=""Background"" Value=""@BG""/>
  <Setter Property=""Foreground"" Value=""@FG""/>
  <Setter Property=""BorderBrush"" Value=""#40000000""/>
  <Setter Property=""BorderThickness"" Value=""1""/>
  <Setter Property=""Cursor"" Value=""Hand""/>
  <Setter Property=""Focusable"" Value=""False""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""Button"">
        <Border x:Name=""Bd"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}""
                BorderThickness=""{TemplateBinding BorderThickness}"">
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""Bd"" Property=""Background"" Value=""@HOVER""/>
          </Trigger>
          <Trigger Property=""IsPressed"" Value=""True"">
            <Setter TargetName=""Bd"" Property=""Background"" Value=""@PRESS""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter TargetName=""Bd"" Property=""Opacity"" Value=""0.55""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

    internal static Style FlatButton(string bg, string hover, string press, string fg) =>
        ParseStyle(FlatButtonXaml, ("@BG", bg), ("@HOVER", hover), ("@PRESS", press), ("@FG", fg));

    // Botão de vidro: cantos redondos, fundo translúcido e borda fininha.
    private const string GlassButtonXaml = @"
<Style " + Ns + @" TargetType=""Button"">
  <Setter Property=""Foreground"" Value=""#F2F4FF""/>
  <Setter Property=""Cursor"" Value=""Hand""/>
  <Setter Property=""Focusable"" Value=""False""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""Button"">
        <Border x:Name=""Bd"" Background=""#26FFFFFF"" BorderBrush=""#33FFFFFF"" BorderThickness=""1"" CornerRadius=""@R"">
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""Bd"" Property=""Background"" Value=""#3DFFFFFF""/>
            <Setter TargetName=""Bd"" Property=""BorderBrush"" Value=""#66F9A8D4""/>
          </Trigger>
          <Trigger Property=""IsPressed"" Value=""True"">
            <Setter TargetName=""Bd"" Property=""Background"" Value=""#18FFFFFF""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter TargetName=""Bd"" Property=""Opacity"" Value=""0.5""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

    internal static Style GlassButton(string radius = "10") => ParseStyle(GlassButtonXaml, ("@R", radius));

    // Botão principal: degradê rosa → roxo com um brilho suave (um efeito só, parado).
    private const string AccentButtonXaml = @"
<Style " + Ns + @" TargetType=""Button"">
  <Setter Property=""Foreground"" Value=""White""/>
  <Setter Property=""Cursor"" Value=""Hand""/>
  <Setter Property=""Focusable"" Value=""False""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""Button"">
        <Grid>
          <Border x:Name=""Bd"" CornerRadius=""@R"" BorderBrush=""#66FFFFFF"" BorderThickness=""1"">
            <Border.Background>
              <LinearGradientBrush StartPoint=""0,0"" EndPoint=""1,0"">
                <GradientStop Color=""#F0609E"" Offset=""0""/>
                <GradientStop Color=""#9B5CF0"" Offset=""1""/>
              </LinearGradientBrush>
            </Border.Background>
            <Border.Effect>
              <DropShadowEffect Color=""#E0509C"" BlurRadius=""@GLOW"" ShadowDepth=""0"" Opacity=""0.45""/>
            </Border.Effect>
          </Border>
          <Border x:Name=""Hi"" CornerRadius=""@R"" Background=""#22FFFFFF"" Visibility=""Collapsed""/>
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""Hi"" Property=""Visibility"" Value=""Visible""/>
          </Trigger>
          <Trigger Property=""IsPressed"" Value=""True"">
            <Setter TargetName=""Hi"" Property=""Background"" Value=""#22000000""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter Property=""Opacity"" Value=""0.55""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

    internal static Style AccentButton(string radius = "12", string glow = "22") =>
        ParseStyle(AccentButtonXaml, ("@R", radius), ("@GLOW", glow));

    // Lista suspensa escura (Conta / Modo / Versão).
    private const string ComboXaml = @"
<Style " + Ns + @" TargetType=""ComboBox"">
  <Setter Property=""Foreground"" Value=""#F2F4FF""/>
  <Setter Property=""Background"" Value=""#2AFFFFFF""/>
  <Setter Property=""BorderBrush"" Value=""#38FFFFFF""/>
  <Setter Property=""BorderThickness"" Value=""1""/>
  <Setter Property=""Padding"" Value=""13,0,34,0""/>
  <Setter Property=""Cursor"" Value=""Hand""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""ComboBox"">
        <Grid>
          <ToggleButton Focusable=""False"" ClickMode=""Press""
                        IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"">
            <ToggleButton.Template>
              <ControlTemplate TargetType=""ToggleButton"">
                <Border x:Name=""Bd"" Background=""#2AFFFFFF"" BorderBrush=""#38FFFFFF"" BorderThickness=""1"" CornerRadius=""10"">
                  <Path HorizontalAlignment=""Right"" VerticalAlignment=""Center"" Margin=""0,0,14,0""
                        Data=""M0,0 L4.5,4.5 L9,0"" Stroke=""#F9A8D4"" StrokeThickness=""1.8""
                        StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round"" StrokeLineJoin=""Round""/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property=""IsMouseOver"" Value=""True"">
                    <Setter TargetName=""Bd"" Property=""Background"" Value=""#3DFFFFFF""/>
                    <Setter TargetName=""Bd"" Property=""BorderBrush"" Value=""#66F9A8D4""/>
                  </Trigger>
                  <Trigger Property=""IsChecked"" Value=""True"">
                    <Setter TargetName=""Bd"" Property=""BorderBrush"" Value=""#AAF472B6""/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </ToggleButton.Template>
          </ToggleButton>
          <ContentPresenter IsHitTestVisible=""False"" Margin=""{TemplateBinding Padding}""
                            VerticalAlignment=""Center"" HorizontalAlignment=""Left""
                            Content=""{TemplateBinding SelectionBoxItem}""
                            ContentTemplate=""{TemplateBinding SelectionBoxItemTemplate}""/>
          <Popup IsOpen=""{TemplateBinding IsDropDownOpen}"" Placement=""Bottom"" Focusable=""False"" PopupAnimation=""None"">
            <Border MinWidth=""{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"" MaxHeight=""260""
                    Margin=""0,4,0,0"" Background=""#F2161A30"" BorderBrush=""#44FFFFFF"" BorderThickness=""1"" Padding=""3"">
              <ScrollViewer><ItemsPresenter/></ScrollViewer>
            </Border>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter Property=""Opacity"" Value=""0.5""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

    private const string ComboItemXaml = @"
<Style " + Ns + @" TargetType=""ComboBoxItem"">
  <Setter Property=""Foreground"" Value=""#F2F4FF""/>
  <Setter Property=""Padding"" Value=""11,8""/>
  <Setter Property=""Cursor"" Value=""Hand""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""ComboBoxItem"">
        <Border x:Name=""Bd"" Background=""Transparent"" Padding=""{TemplateBinding Padding}"">
          <ContentPresenter/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsHighlighted"" Value=""True"">
            <Setter TargetName=""Bd"" Property=""Background"" Value=""#40F472B6""/>
          </Trigger>
          <Trigger Property=""IsSelected"" Value=""True"">
            <Setter TargetName=""Bd"" Property=""Background"" Value=""#559B5CF0""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

    private const string CheckXaml = @"
<Style " + Ns + @" TargetType=""CheckBox"">
  <Setter Property=""Foreground"" Value=""#DCE0F5""/>
  <Setter Property=""Cursor"" Value=""Hand""/>
  <Setter Property=""Focusable"" Value=""False""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""CheckBox"">
        <StackPanel Orientation=""Horizontal"" Background=""Transparent"">
          <Border x:Name=""Box"" Width=""19"" Height=""19"" CornerRadius=""6"" VerticalAlignment=""Center""
                  Background=""#22FFFFFF"" BorderBrush=""#55FFFFFF"" BorderThickness=""1"">
            <Path x:Name=""Mark"" Data=""M3.5,8.5 L7,12 L13.5,4.5"" Stroke=""White"" StrokeThickness=""2""
                  StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round"" StrokeLineJoin=""Round"" Visibility=""Collapsed""/>
          </Border>
          <ContentPresenter Margin=""10,0,0,0"" VerticalAlignment=""Center""/>
        </StackPanel>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsChecked"" Value=""True"">
            <Setter TargetName=""Box"" Property=""Background"" Value=""#B05CF0""/>
            <Setter TargetName=""Box"" Property=""BorderBrush"" Value=""#F9A8D4""/>
            <Setter TargetName=""Mark"" Property=""Visibility"" Value=""Visible""/>
          </Trigger>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""Box"" Property=""BorderBrush"" Value=""#F9A8D4""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

    // Barra de progresso fina e sem animação (parada, quando o tamanho é desconhecido, fica cheia e translúcida).
    private const string BarXaml = @"
<Style " + Ns + @" TargetType=""ProgressBar"">
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""ProgressBar"">
        <Grid>
          <Border x:Name=""PART_Track"" Background=""#33FFFFFF"" CornerRadius=""3""/>
          <Border x:Name=""PART_Indicator"" HorizontalAlignment=""Left"" CornerRadius=""3"">
            <Border.Background>
              <LinearGradientBrush StartPoint=""0,0"" EndPoint=""1,0"">
                <GradientStop Color=""#F0609E"" Offset=""0""/>
                <GradientStop Color=""#9B5CF0"" Offset=""1""/>
              </LinearGradientBrush>
            </Border.Background>
          </Border>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsIndeterminate"" Value=""True"">
            <Setter TargetName=""PART_Track"" Property=""Background"" Value=""#88F0609E""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

    private const string MenuXaml = @"
<Style " + Ns + @" TargetType=""ContextMenu"">
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""ContextMenu"">
        <Border Background=""#F2161A30"" BorderBrush=""#44FFFFFF"" BorderThickness=""1"" Padding=""3"">
          <StackPanel IsItemsHost=""True""/>
        </Border>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

    private const string MenuItemXaml = @"
<Style " + Ns + @" TargetType=""MenuItem"">
  <Setter Property=""Foreground"" Value=""#F2F4FF""/>
  <Setter Property=""Padding"" Value=""12,8""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""MenuItem"">
        <Border x:Name=""Bd"" Background=""Transparent"" Padding=""{TemplateBinding Padding}"">
          <ContentPresenter ContentSource=""Header"" RecognizesAccessKey=""True""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsHighlighted"" Value=""True"">
            <Setter TargetName=""Bd"" Property=""Background"" Value=""#40F472B6""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

    private static SolidColorBrush Rgb(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    // Linha fininha que some nas pontas (separa as partes do cartão).
    private static Border Rule(double top, double bottom) => new()
    {
        Height = 1,
        Margin = new Thickness(0, top, 0, bottom),
        Background = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(0x00, 0xF4, 0x72, 0xB6), 0),
            new GradientStop(Color.FromArgb(0x90, 0xF4, 0x72, 0xB6), 0.5),
            new GradientStop(Color.FromArgb(0x00, 0xF4, 0x72, 0xB6), 1)
        }, 0)
    };

    private static TextBlock FieldLabel(string text, double top) => new()
    {
        Text = text,
        FontSize = 10.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = Rgb("#F472B6"),
        Margin = new Thickness(2, top, 0, 6)
    };

    private void StyleCombo(ComboBox box, string tip)
    {
        box.Style = ParseStyle(ComboXaml);
        box.ItemContainerStyle = ParseStyle(ComboItemXaml);
        box.Height = 38;
        box.FontSize = 14;
        box.ToolTip = tip;
    }

    private UIElement BuildPanel()
    {
        var stack = new StackPanel();

        // Cabeçalho: logo + nome + versão
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var logo = LoadImage("pack://application:,,,/Assets/logo.png", 96);
        if (logo != null)
        {
            var img = new Image { Source = logo, Width = 44, Height = 44, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            head.Children.Add(img);
        }
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = "MOONLIGHT",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        });
        titles.Children.Add(new TextBlock
        {
            Text = "Launcher  ·  " + Updater.CurrentText + "  ·  modo leve",
            FontSize = 11.5,
            Foreground = Rgb("#E58FC3"),
            Margin = new Thickness(1, 1, 0, 0)
        });
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        stack.Children.Add(head);
        stack.Children.Add(Rule(16, 0));

        // Conta
        stack.Children.Add(FieldLabel("CONTA", 14));
        StyleCombo(_accountBox, "Conta");
        _accountBox.SelectionChanged += AccountBox_Changed;
        stack.Children.Add(_accountBox);

        // Modo (Vanilla, Fabric…) e versão, lado a lado
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var modeCol = new StackPanel();
        modeCol.Children.Add(FieldLabel("MODO", 12));
        StyleCombo(_loaderBox, "Modo (Vanilla, Fabric, Forge…)");
        _loaderBox.ItemsSource = Enum.GetValues<Loader>()
            .Select(l => new LiteItem(MainWindow.LiteLoaderTitle(l), l)).ToList();
        _loaderBox.SelectionChanged += LoaderBox_Changed;
        modeCol.Children.Add(_loaderBox);
        Grid.SetColumn(modeCol, 0);
        row.Children.Add(modeCol);

        var verCol = new StackPanel();
        verCol.Children.Add(FieldLabel("VERSÃO", 12));
        StyleCombo(_versionBox, "Versão do Minecraft (✔ = já baixada)");
        _versionBox.SelectionChanged += VersionBox_Changed;
        verCol.Children.Add(_versionBox);
        Grid.SetColumn(verCol, 2);
        row.Children.Add(verCol);
        stack.Children.Add(row);

        // Opções
        var check = ParseStyle(CheckXaml);
        _onlyInstalled.Style = check;
        _onlyInstalled.Content = "Só versões baixadas";
        _onlyInstalled.FontSize = 13;
        _onlyInstalled.Margin = new Thickness(1, 16, 0, 0);
        _onlyInstalled.Click += (_, _) => RefreshVersions();
        stack.Children.Add(_onlyInstalled);

        _hideOnPlay.Style = check;
        _hideOnPlay.Content = "Esconder o launcher ao jogar";
        _hideOnPlay.FontSize = 13;
        _hideOnPlay.Margin = new Thickness(1, 10, 0, 0);
        _hideOnPlay.IsChecked = true;
        stack.Children.Add(_hideOnPlay);

        stack.Children.Add(Rule(16, 0));

        // Jogar
        _playBtn.Content = "JOGAR";
        _playBtn.Height = 50;
        _playBtn.FontSize = 17;
        _playBtn.FontWeight = FontWeights.Bold;
        _playBtn.Margin = new Thickness(0, 16, 0, 0);
        _playBtn.Style = AccentButton();
        _playBtn.Click += Play_Click;
        stack.Children.Add(_playBtn);

        // Quatro botões de vidro: contas, pasta, atualizar e menu
        var icons = new Grid { Height = 40, Margin = new Thickness(0, 12, 0, 0) };
        for (var i = 0; i < 7; i++)
            icons.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(8) });

        void AddIcon(int col, Button b) { Grid.SetColumn(b, col); icons.Children.Add(b); }
        AddIcon(0, IconButton("\uE77B", "#93C5FD", "Contas", () => OpenAccounts()));
        AddIcon(2, IconButton("\uED25", "#FCD34D", "Abrir a pasta do jogo (.minecraft)", () => _main.LiteOpenFolder("")));
        AddIcon(4, IconButton("\uE72C", "#86EFAC", "Atualizar as versões baixadas", () =>
        {
            _main.LiteRescan();
            RefreshVersions();
        }));
        var menuBtn = IconButton("\uE700", "#E9D5FF", "Menu", null);
        menuBtn.Click += (_, _) => ShowMenu(menuBtn);
        AddIcon(6, menuBtn);
        stack.Children.Add(icons);

        // Progresso e mensagem
        _bar.Style = ParseStyle(BarXaml);
        _bar.Height = 4;
        _bar.Margin = new Thickness(0, 14, 0, 0);
        _bar.Visibility = Visibility.Hidden;
        stack.Children.Add(_bar);

        _status.Foreground = Rgb("#C9CDE8");
        _status.FontSize = 12;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.TextAlignment = TextAlignment.Center;
        _status.Margin = new Thickness(0, 8, 0, 0);
        _status.MinHeight = 16;
        stack.Children.Add(_status);

        // Cartão de vidro no meio: um degradê escuro translúcido, borda clara e uma sombra parada.
        return new Border
        {
            Width = 390,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0x70, 0xF4, 0x9A, 0xD0), Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 90),
            Background = new LinearGradientBrush(Color.FromArgb(0xD8, 0x1B, 0x1D, 0x3C), Color.FromArgb(0xD0, 0x10, 0x12, 0x28), 90),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 36, ShadowDepth = 0, Opacity = 0.55 },
            Padding = new Thickness(26, 24, 26, 22),
            Child = stack
        };
    }

    private static Button IconButton(string glyph, string color, string tip, Action? onClick)
    {
        var b = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 17,
                Foreground = Rgb(color)
            },
            ToolTip = tip,
            Style = GlassButton()
        };
        if (onClick != null) b.Click += (_, _) => onClick();
        return b;
    }

    // ---------- Menu ☰ ----------

    private void ShowMenu(Button anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Top, Style = ParseStyle(MenuXaml) };
        var itemStyle = ParseStyle(MenuItemXaml);

        MenuItem Item(string text, Action act)
        {
            var mi = new MenuItem { Header = text, Style = itemStyle };
            mi.Click += (_, _) => act();
            return mi;
        }

        menu.Items.Add(Item("⚙  Configurações…", OpenSettings));
        menu.Items.Add(Item("📦  Pasta de mods", () => _main.LiteOpenFolder("mods")));
        menu.Items.Add(Item("📜  Log do launcher", _main.LiteOpenLog));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("🖥  Usar a interface completa", () => _main.LiteSetPerfMode("Full")));
        menu.IsOpen = true;
    }

    private void OpenSettings()
    {
        var action = LiteDialogs.Settings(this, _main);
        if (action != null) _main.LiteSetPerfMode(action); // pode fechar esta janela: nada vem depois
    }

    // ---------- Contas ----------

    internal void OpenAccounts() => LiteDialogs.Accounts(this, _main);

    internal void RefreshAccounts()
    {
        _updating = true;
        var items = _main.LiteAccounts.Select(a => new LiteItem($"{a.Name}   ·   {a.TypeLabel}", a)).ToList();
        _accountBox.ItemsSource = items;
        var active = _main.LiteActiveAccount;
        if (items.Count == 0)
        {
            // Sem conta: mostra um aviso no lugar (não é uma conta, então não faz nada ao ser escolhido).
            _accountBox.ItemsSource = new[] { new LiteItem("Nenhuma conta: use o primeiro botão de baixo", null) };
            _accountBox.SelectedIndex = 0;
        }
        else _accountBox.SelectedItem = items.FirstOrDefault(i => ((Account)i.Value!).Key == active?.Key);
        _updating = false;
    }

    private void AccountBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if ((_accountBox.SelectedItem as LiteItem)?.Value is Account a) _main.LiteSelectAccount(a);
    }

    // ---------- Modo e versões ----------

    private void LoaderBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if ((_loaderBox.SelectedItem as LiteItem)?.Value is Loader l)
            _main.LiteSetLoader(l); // a MainWindow remonta a lista e chama RefreshVersions
    }

    private void VersionBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if ((_versionBox.SelectedItem as LiteItem)?.Value is string v) _main.LiteSelectVersion(v);
    }

    /// <summary>A MainWindow avisa quando a lista de versões mudou (modo trocado, versões carregadas…).</summary>
    internal void RefreshVersions()
    {
        _updating = true;
        try
        {
            // Modo
            var cur = _main.LiteLoader;
            _loaderBox.SelectedItem = _loaderBox.Items.OfType<LiteItem>().FirstOrDefault(i => i.Value is Loader l && l == cur);

            // Versões
            var wanted = (_versionBox.SelectedItem as LiteItem)?.Value as string ?? _main.LiteSelectedVersion;
            IEnumerable<string> all = _main.LiteVersions;
            if (_onlyInstalled.IsChecked == true) all = all.Where(_main.LiteIsInstalled);

            var items = all.Select(v => new LiteItem(_main.LiteIsInstalled(v) ? v + "   ✔" : v, v)).ToList();
            _versionBox.ItemsSource = items;
            _versionBox.SelectedItem = items.FirstOrDefault(i => (string?)i.Value == wanted) ?? items.FirstOrDefault();
        }
        finally { _updating = false; }

        if ((_versionBox.SelectedItem as LiteItem)?.Value is string v2) _main.LiteSelectVersion(v2);
        UpdatePlayEnabled(_main.LiteBusy);
    }

    // ---------- Jogar ----------

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_main.LiteBusy) return;

        if (_main.LiteActiveAccount == null)
        {
            LiteDialogs.Message(this, "Escolha uma conta",
                "Para jogar você precisa de uma conta. Crie uma conta Moonlight (só o nome) ou entre com a conta Microsoft.",
                null, new[] { ("Ok", "ok") });
            OpenAccounts();
            if (_main.LiteActiveAccount == null) return;
        }

        if ((_versionBox.SelectedItem as LiteItem)?.Value is not string v) return;

        _main.LiteSelectVersion(v);
        _playBtn.IsEnabled = false;
        _playBtn.Content = "AGUARDE…";
        _main.LitePlay();
        Tick();
    }

    private void UpdatePlayEnabled(bool busy)
    {
        _playBtn.IsEnabled = !busy && _versionBox.Items.Count > 0;
        _playBtn.Content = busy ? "AGUARDE…" : "JOGAR";
    }

    // Lê o andamento que a MainWindow já mantém (mensagem e barra) e mostra aqui.
    private void Tick()
    {
        var busy = _main.LiteBusy;

        var text = _main.LiteStatus;
        if (text != _lastStatus)
        {
            _lastStatus = text;
            _status.Text = text;
        }

        if (busy)
        {
            _bar.Visibility = Visibility.Visible;
            var (indeterminate, max, value) = _main.LiteProgress;
            _bar.IsIndeterminate = indeterminate;
            if (!indeterminate)
            {
                _bar.Maximum = Math.Max(max, 1);
                _bar.Value = value;
            }
        }
        else if (_bar.Visibility != Visibility.Hidden)
        {
            _bar.IsIndeterminate = false;
            _bar.Visibility = Visibility.Hidden;
        }

        if (busy != _wasBusy)
        {
            _wasBusy = busy;
            UpdatePlayEnabled(busy);
            if (!busy) RefreshVersions(); // a versão que acabou de baixar já ganha o ✔
        }
    }

    // ---------- Jogo aberto / fechado ----------

    internal void OnGameStarted()
    {
        if (_hideOnPlay.IsChecked == true) Hide();
        else WindowState = WindowState.Minimized;
    }

    internal void Reappear()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Tick();
    }

    internal string ShowMessage(string title, string message, string? details, (string Label, string Key)[] buttons) =>
        LiteDialogs.Message(this, title, message, details, buttons);
}
