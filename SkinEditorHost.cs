using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace MoonlightLauncher;

// Liga o criador de skin (SkinEditor) à aba Skins.
public partial class MainWindow
{
    private bool _editorWired;

    private void SkinTab_Checked(object sender, RoutedEventArgs e)
    {
        if (SkinsMain == null || SkinEditorView == null || SkinEditorLock == null ||
            sender is not RadioButton rb) return;
        ApplySkinTab(rb.Tag?.ToString() ?? "mine");
    }

    private string CurrentSkinTab() =>
        SkinEditorTab?.IsChecked == true ? "editor" : "mine";

    // O criador de skin só abre com o Golden Moon ativo; sem ele aparece o cartão com o cadeado.
    private void ApplySkinTab(string tab)
    {
        if (SkinsMain == null || SkinEditorView == null || SkinEditorLock == null) return;

        var editor = tab == "editor";
        var golden = GoldenActive;
        var editorOk = editor && golden;
        if (editorOk) WireSkinEditor();

        SkinsMain.Visibility = tab == "mine" ? Visibility.Visible : Visibility.Collapsed;
        SkinEditorView.Visibility = editorOk ? Visibility.Visible : Visibility.Collapsed;
        SkinEditorLock.Visibility = editor && !golden ? Visibility.Visible : Visibility.Collapsed;

        HomeSkinView.Paused = _renderPaused;
        SkinView.Paused = _renderPaused || tab != "mine"; // o boneco da outra tela descansa enquanto você edita
    }

    private void WireSkinEditor()
    {
        if (_editorWired) return;
        _editorWired = true;

        SkinEditorView.CurrentSkinProvider = () =>
        {
            var acc = _active;
            if (acc != null && File.Exists(SkinFilePath(acc)))
                return (File.ReadAllBytes(SkinFilePath(acc)), acc.SkinSlim);
            return (null, false);
        };

        SkinEditorView.SaveRequested += (png, slim, name) =>
        {
            SaveToLibrary(name, png, slim);
            RefreshSkinLibrary();
        };

        SkinEditorView.UseRequested += (png, slim) =>
        {
            var acc = _active;
            if (acc == null) return "Crie ou selecione uma conta primeiro (aba Contas).";
            SaveToLibrary("editor-" + DateTime.Now.ToString("MMdd-HHmm"), png, slim);
            ApplySkin(acc, png, slim);
            return $"Skin aplicada à conta {acc.Name} ✔ " + NextStepHint(acc);
        };
    }
}
