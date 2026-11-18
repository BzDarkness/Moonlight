using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MoonlightLauncher;

/// <summary>Janelinhas da interface leve: avisos, contas e configurações. Tudo simples, escuro e sem animação.</summary>
internal static class LiteDialogs
{
    private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x2C));
    private static readonly Brush Field = new SolidColorBrush(Color.FromRgb(0x20, 0x23, 0x40));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0xB4, 0xB9, 0xD9));

    private static Window NewDialog(Window owner, string title, double width)
    {
        var w = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = Panel,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI"),
            UseLayoutRounding = true
        };

        if (owner.IsVisible && owner.WindowState != WindowState.Minimized)
        {
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return w;
    }

    private static Button MakeButton(string text, bool primary)
    {
        return new Button
        {
            Content = text,
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Style = primary ? LiteWindow.AccentButton("10", "0") : LiteWindow.GlassButton()
        };
    }

    private static TextBlock Label(string text, double size = 13, Brush? color = null, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = color ?? Brushes.White,
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap
    };

    // ---------- Aviso com botões ----------

    public static string Message(Window owner, string title, string message, string? details,
        (string Label, string Key)[] buttons)
    {
        var result = "close";
        var w = NewDialog(owner, title, 460);

        var stack = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
        stack.Children.Add(Label(title, 17, null, FontWeights.Bold));
        var msg = Label(message, 13.5, Muted);
        msg.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(msg);

        if (!string.IsNullOrWhiteSpace(details))
        {
            var box = new TextBox
            {
                Text = details,
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11.5,
                Background = Field,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8),
                MaxHeight = 160,
                Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            stack.Children.Add(box);
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        // O primeiro botão é o principal e fica à direita.
        for (var i = buttons.Length - 1; i >= 0; i--)
        {
            var (label, key) = buttons[i];
            var b = MakeButton(label, i == 0);
            b.Click += (_, _) => { result = key; w.Close(); };
            row.Children.Add(b);
        }
        stack.Children.Add(row);

        w.Content = stack;
        w.ShowDialog();
        return result;
    }

    // ---------- Contas ----------

    public static void Accounts(LiteWindow owner, MainWindow main)
    {
        var w = NewDialog(owner, "Contas", 440);
        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        stack.Children.Add(Label("Contas", 17, null, FontWeights.Bold));
        var hint = Label("Clique numa conta para usar ela no jogo.", 12.5, Muted);
        hint.Margin = new Thickness(0, 4, 0, 10);
        stack.Children.Add(hint);

        var list = new ListBox
        {
            Height = 120,
            Background = Field,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 13.5
        };
        stack.Children.Add(list);

        var status = Label("", 12.5, Muted);
        status.Margin = new Thickness(0, 8, 0, 0);

        var updating = false;
        void Fill()
        {
            updating = true;
            var items = main.LiteAccounts.Select(a => new LiteItem($"{a.Name}   ·   {a.TypeLabel}", a)).ToList();
            list.ItemsSource = items;
            var active = main.LiteActiveAccount;
            list.SelectedItem = items.FirstOrDefault(i => ((Account)i.Value!).Key == active?.Key);
            updating = false;
            owner.RefreshAccounts();
        }

        list.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            if ((list.SelectedItem as LiteItem)?.Value is not Account a) return;
            main.LiteSelectAccount(a);
            owner.RefreshAccounts();
        };

        var remove = MakeButton("Remover a conta escolhida", false);
        remove.Margin = new Thickness(0, 8, 0, 0);
        remove.HorizontalAlignment = HorizontalAlignment.Left;
        remove.Click += (_, _) =>
        {
            if ((list.SelectedItem as LiteItem)?.Value is not Account a) return;
            main.LiteRemoveAccount(a);
            status.Text = $"Conta “{a.Name}” removida.";
            Fill();
        };
        stack.Children.Add(remove);

        // Conta Moonlight (só o nome)
        var nickTitle = Label("Nova conta Moonlight (só o nome)", 13, null, FontWeights.SemiBold);
        nickTitle.Margin = new Thickness(0, 16, 0, 6);
        stack.Children.Add(nickTitle);

        var nickRow = new Grid();
        nickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nick = new TextBox
        {
            Height = 34,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 0, 6, 0),
            MaxLength = 16
        };
        var create = MakeButton("Criar", true);
        Grid.SetColumn(create, 1);
        void DoCreate()
        {
            status.Text = main.LiteAddOffline(nick.Text.Trim(), out var ok);
            if (ok) nick.Clear();
            Fill();
        }
        create.Click += (_, _) => DoCreate();
        nick.KeyDown += (_, e) => { if (e.Key == Key.Enter) DoCreate(); };
        nickRow.Children.Add(nick);
        nickRow.Children.Add(create);
        stack.Children.Add(nickRow);

        // Microsoft
        var ms = MakeButton("Entrar com a conta Microsoft", false);
        ms.Margin = new Thickness(0, 12, 0, 0);
        ms.HorizontalAlignment = HorizontalAlignment.Left;
        ms.Click += async (_, _) =>
        {
            ms.IsEnabled = false;
            status.Text = "Siga as instruções na janela de login da Microsoft…";
            try { status.Text = await main.LiteMicrosoftLoginAsync(); }
            finally { ms.IsEnabled = true; Fill(); }
        };
        stack.Children.Add(ms);
        stack.Children.Add(status);

        var close = MakeButton("Fechar", true);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 14, 0, 0);
        close.Click += (_, _) => w.Close();
        stack.Children.Add(close);

        Fill();
        w.Content = stack;
        w.ShowDialog();
    }

    // ---------- Configurações ----------

    /// <summary>Devolve o modo escolhido ("Full" ou "Auto") se a pessoa quiser sair da interface leve; senão null.</summary>
    public static string? Settings(LiteWindow owner, MainWindow main)
    {
        string? action = null;
        var w = NewDialog(owner, "Configurações", 430);
        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        stack.Children.Add(Label("Configurações", 17, null, FontWeights.Bold));

        // RAM
        var max = Math.Max(main.LiteMaxRam, 1024);
        var ramLabel = Label("", 13.5, null, FontWeights.SemiBold);
        ramLabel.Margin = new Thickness(0, 14, 0, 4);
        stack.Children.Add(ramLabel);

        var slider = new Slider
        {
            Minimum = 1024,
            Maximum = max,
            TickFrequency = 256,
            IsSnapToTickEnabled = true,
            SmallChange = 256,
            LargeChange = 1024,
            Value = Math.Clamp(main.LiteRam, 1024, max)
        };
        void ShowRam() => ramLabel.Text = $"Memória RAM do jogo: {(int)slider.Value} MB  (seu PC aguenta até {max} MB)";
        slider.ValueChanged += (_, _) =>
        {
            ShowRam();
            main.LiteSetRam((int)slider.Value);
        };
        ShowRam();
        stack.Children.Add(slider);

        // Modo
        var modeTitle = Label("Interface", 13.5, null, FontWeights.SemiBold);
        modeTitle.Margin = new Thickness(0, 18, 0, 4);
        stack.Children.Add(modeTitle);
        var modeInfo = Label("Você está no modo Leve: uma janelinha simples, sem animações e com bem pouca memória.", 12.5, Muted);
        stack.Children.Add(modeInfo);

        var full = MakeButton("✨  Voltar para a interface completa", true);
        full.Margin = new Thickness(0, 10, 0, 0);
        full.HorizontalAlignment = HorizontalAlignment.Left;
        full.Click += (_, _) => { action = "Full"; w.Close(); };
        stack.Children.Add(full);

        var auto = MakeButton("🤖  Deixar no automático", false);
        auto.Margin = new Thickness(0, 8, 0, 0);
        auto.HorizontalAlignment = HorizontalAlignment.Left;
        auto.Click += (_, _) => { action = "Auto"; w.Close(); };
        stack.Children.Add(auto);

        var close = MakeButton("Fechar", false);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 16, 0, 0);
        close.Click += (_, _) => w.Close();
        stack.Children.Add(close);

        w.Content = stack;
        w.ShowDialog();
        return action;
    }
}
