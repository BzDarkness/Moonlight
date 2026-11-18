using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace MoonlightLauncher;

/// <summary>
/// Visual da interface leve: paleta (noite azulada + rosa de cerejeira + violeta da lua) e os estilos dos controles.
/// Tudo estático, sem animação: continua leve, só que bonito.
/// </summary>
internal static class LiteTheme
{
    // Paleta
    public const string Pink = "#F27DBA";       // rosa cerejeira (destaque)
    public const string Violet = "#8B5CF6";     // violeta da lua (identidade do launcher)
    public const string Ink = "#F4F1FF";        // texto principal
    public const string Soft = "#C9C2EE";       // texto secundário
    public const string Label = "#F7A8D3";      // rótulos pequenos

    public static Brush B(string hex) => Freeze((Brush)new BrushConverter().ConvertFromString(hex)!);

    private static Brush Freeze(Brush b) { if (b.CanFreeze) b.Freeze(); return b; }

    public static Style Parse(string xaml) => (Style)XamlReader.Parse(xaml);

    // ---------- Caixa de escolha (conta, modo, versão) ----------
    public static readonly string ComboBoxXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
  <Setter Property="Foreground" Value="#F4F1FF"/>
  <Setter Property="Background" Value="#33FFFFFF"/>
  <Setter Property="BorderBrush" Value="#44FFFFFF"/>
  <Setter Property="BorderThickness" Value="1"/>
  <Setter Property="Padding" Value="12,0,30,0"/>
  <Setter Property="Cursor" Value="Hand"/>
  <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
  <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled"/>
  <Setter Property="ItemContainerStyle">
    <Setter.Value>
      <Style TargetType="ComboBoxItem">
        <Setter Property="Foreground" Value="#F4F1FF"/>
        <Setter Property="Padding" Value="10,7,10,7"/>
        <Setter Property="Cursor" Value="Hand"/>
        <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="ComboBoxItem">
              <Border x:Name="Bd" Background="Transparent" CornerRadius="6" Margin="3,1,3,1"
                      Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
                <ContentPresenter VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsHighlighted" Value="True">
                  <Setter TargetName="Bd" Property="Background" Value="#40F27DBA"/>
                </Trigger>
                <Trigger Property="IsSelected" Value="True">
                  <Setter TargetName="Bd" Property="Background" Value="#66C0529A"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Style>
    </Setter.Value>
  </Setter>
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="ComboBox">
        <Grid>
          <ToggleButton x:Name="Tg" ClickMode="Press" Focusable="False"
                        Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
            <ToggleButton.Template>
              <ControlTemplate TargetType="ToggleButton">
                <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="9" SnapsToDevicePixels="True">
                  <Path x:Name="Arrow" Data="M0,0 L4.5,4.5 L9,0" HorizontalAlignment="Right" VerticalAlignment="Center"
                        Margin="0,0,13,0" Stroke="#F7A8D3" StrokeThickness="2" StrokeStartLineCap="Round"
                        StrokeEndLineCap="Round" StrokeLineJoin="Round"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Bd" Property="Background" Value="#4DFFFFFF"/>
                    <Setter TargetName="Bd" Property="BorderBrush" Value="#99F27DBA"/>
                  </Trigger>
                  <Trigger Property="IsChecked" Value="True">
                    <Setter TargetName="Bd" Property="BorderBrush" Value="#F27DBA"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </ToggleButton.Template>
          </ToggleButton>
          <ContentPresenter IsHitTestVisible="False" Margin="{TemplateBinding Padding}"
                            HorizontalAlignment="Left" VerticalAlignment="Center"
                            Content="{TemplateBinding SelectionBoxItem}"
                            ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                            ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}"/>
          <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom"
                 AllowsTransparency="True" Focusable="False" PopupAnimation="None">
            <Border MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"
                    MaxHeight="{TemplateBinding MaxDropDownHeight}" Margin="0,4,0,8"
                    Background="#F2121738" BorderBrush="#88F27DBA" BorderThickness="1" CornerRadius="9"
                    Padding="0,4,0,4" SnapsToDevicePixels="True">
              <ScrollViewer CanContentScroll="True">
                <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
              </ScrollViewer>
            </Border>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property="IsEnabled" Value="False">
            <Setter Property="Opacity" Value="0.55"/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";

    // ---------- Caixinha de marcar ----------
    public static readonly string CheckBoxXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
  <Setter Property="Foreground" Value="#E4DFFA"/>
  <Setter Property="Cursor" Value="Hand"/>
  <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="CheckBox">
        <StackPanel Orientation="Horizontal" Background="Transparent">
          <Border x:Name="Box" Width="19" Height="19" CornerRadius="6" Background="#2EFFFFFF"
                  BorderBrush="#77FFFFFF" BorderThickness="1" VerticalAlignment="Center" SnapsToDevicePixels="True">
            <Path x:Name="Mark" Data="M4,9.5 L7.8,13 L14.5,5.5" Stroke="White" StrokeThickness="2.2"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round"
                  Visibility="Collapsed"/>
          </Border>
          <ContentPresenter Margin="10,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
        </StackPanel>
        <ControlTemplate.Triggers>
          <Trigger Property="IsMouseOver" Value="True">
            <Setter TargetName="Box" Property="BorderBrush" Value="#F27DBA"/>
          </Trigger>
          <Trigger Property="IsChecked" Value="True">
            <Setter TargetName="Box" Property="Background" Value="#D9559E"/>
            <Setter TargetName="Box" Property="BorderBrush" Value="#F7A8D3"/>
            <Setter TargetName="Mark" Property="Visibility" Value="Visible"/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";

    // ---------- Campo de texto ----------
    public static readonly string TextBoxXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TextBox">
  <Setter Property="Background" Value="#26FFFFFF"/>
  <Setter Property="Foreground" Value="#F4F1FF"/>
  <Setter Property="CaretBrush" Value="#F7A8D3"/>
  <Setter Property="SelectionBrush" Value="#D9559E"/>
  <Setter Property="BorderBrush" Value="#44FFFFFF"/>
  <Setter Property="BorderThickness" Value="1"/>
  <Setter Property="Padding" Value="8,4,8,4"/>
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="TextBox">
        <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="9" SnapsToDevicePixels="True">
          <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}" Focusable="False"/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property="IsKeyboardFocused" Value="True">
            <Setter TargetName="Bd" Property="BorderBrush" Value="#F27DBA"/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";

    // ---------- Lista (diálogo de contas) ----------
    public static readonly string ListBoxItemXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ListBoxItem">
  <Setter Property="Foreground" Value="#F4F1FF"/>
  <Setter Property="Padding" Value="10,7,10,7"/>
  <Setter Property="Cursor" Value="Hand"/>
  <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="ListBoxItem">
        <Border x:Name="Bd" Background="Transparent" CornerRadius="7" Margin="3,1,3,1"
                Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
          <ContentPresenter VerticalAlignment="Center"/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property="IsMouseOver" Value="True">
            <Setter TargetName="Bd" Property="Background" Value="#33F27DBA"/>
          </Trigger>
          <Trigger Property="IsSelected" Value="True">
            <Setter TargetName="Bd" Property="Background" Value="#80C0529A"/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";

    // ---------- Menu ☰ ----------
    public static readonly string MenuItemXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="MenuItem">
  <Setter Property="Foreground" Value="#F4F1FF"/>
  <Setter Property="FontSize" Value="13.5"/>
  <Setter Property="Padding" Value="14,8,18,8"/>
  <Setter Property="Cursor" Value="Hand"/>
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="MenuItem">
        <Border x:Name="Bd" Background="Transparent" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
          <ContentPresenter ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center"/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property="IsHighlighted" Value="True">
            <Setter TargetName="Bd" Property="Background" Value="#4DF27DBA"/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";

    public static readonly string ContextMenuXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ContextMenu">
  <Setter Property="HasDropShadow" Value="False"/>
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="ContextMenu">
        <Border Background="#FF121738" BorderBrush="#99F27DBA" BorderThickness="1" Padding="3" SnapsToDevicePixels="True">
          <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
        </Border>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";

    // ---------- Botões ----------

    /// <summary>Botão de vidro (fundo translúcido, borda clara): aviso, contas, ícones.</summary>
    public static readonly string GlassButtonXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
  <Setter Property="Background" Value="@BG"/>
  <Setter Property="Foreground" Value="@FG"/>
  <Setter Property="BorderBrush" Value="@BORDER"/>
  <Setter Property="BorderThickness" Value="1"/>
  <Setter Property="Cursor" Value="Hand"/>
  <Setter Property="Focusable" Value="False"/>
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="Button">
        <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="10" SnapsToDevicePixels="True">
          <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                            Margin="{TemplateBinding Padding}"/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property="IsMouseOver" Value="True">
            <Setter TargetName="Bd" Property="Background" Value="@HOVER"/>
            <Setter TargetName="Bd" Property="BorderBrush" Value="#B3F27DBA"/>
          </Trigger>
          <Trigger Property="IsPressed" Value="True">
            <Setter TargetName="Bd" Property="Background" Value="@PRESS"/>
          </Trigger>
          <Trigger Property="IsEnabled" Value="False">
            <Setter TargetName="Bd" Property="Opacity" Value="0.5"/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";

    /// <summary>Botão principal: degradê rosa → violeta com um brilho suave atrás (o brilho fica fora do texto, para ele não borrar).</summary>
    public static readonly string PrimaryButtonXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
  <Setter Property="Foreground" Value="#FFFFFF"/>
  <Setter Property="BorderThickness" Value="0"/>
  <Setter Property="Cursor" Value="Hand"/>
  <Setter Property="Focusable" Value="False"/>
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="Button">
        <Grid x:Name="Root">
          <Border x:Name="Glow" CornerRadius="12" Margin="4,6,4,0" Background="#E0568B">
            <Border.Effect>
              <DropShadowEffect Color="#F27DBA" BlurRadius="22" ShadowDepth="0" Opacity="0.55"/>
            </Border.Effect>
          </Border>
          <Border x:Name="Bd" CornerRadius="12" BorderBrush="#66FFFFFF" BorderThickness="1" SnapsToDevicePixels="True">
            <Border.Background>
              <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                <GradientStop Color="#F0629F" Offset="0"/>
                <GradientStop Color="#9B5CF0" Offset="1"/>
              </LinearGradientBrush>
            </Border.Background>
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property="IsMouseOver" Value="True">
            <Setter TargetName="Bd" Property="Background">
              <Setter.Value>
                <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                  <GradientStop Color="#FF7DB8" Offset="0"/>
                  <GradientStop Color="#B07AFF" Offset="1"/>
                </LinearGradientBrush>
              </Setter.Value>
            </Setter>
            <Setter TargetName="Bd" Property="BorderBrush" Value="#CCFFFFFF"/>
          </Trigger>
          <Trigger Property="IsPressed" Value="True">
            <Setter TargetName="Bd" Property="Background">
              <Setter.Value>
                <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                  <GradientStop Color="#C84F86" Offset="0"/>
                  <GradientStop Color="#7440D0" Offset="1"/>
                </LinearGradientBrush>
              </Setter.Value>
            </Setter>
          </Trigger>
          <Trigger Property="IsEnabled" Value="False">
            <Setter TargetName="Root" Property="Opacity" Value="0.5"/>
            <Setter TargetName="Glow" Property="Visibility" Value="Collapsed"/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";

    public static Style Glass(string bg, string hover, string press, string fg, string border = "#44FFFFFF") =>
        Parse(GlassButtonXaml.Replace("@BG", bg).Replace("@HOVER", hover).Replace("@PRESS", press)
            .Replace("@FG", fg).Replace("@BORDER", border));

    public static Style Primary() => Parse(PrimaryButtonXaml);

    /// <summary>Estilos que valem para tudo dentro de uma janela da interface leve (principal e diálogos).</summary>
    public static void Apply(FrameworkElement scope)
    {
        scope.Resources[typeof(ComboBox)] = Parse(ComboBoxXaml);
        scope.Resources[typeof(System.Windows.Controls.CheckBox)] = Parse(CheckBoxXaml);
        scope.Resources[typeof(System.Windows.Controls.TextBox)] = Parse(TextBoxXaml);
        scope.Resources[typeof(System.Windows.Controls.ListBoxItem)] = Parse(ListBoxItemXaml);
    }
}
