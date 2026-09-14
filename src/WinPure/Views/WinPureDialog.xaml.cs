using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinPure.Services;

namespace WinPure.Views;

/// <summary>
/// A dark, WinPure-styled replacement for the native white <see cref="MessageBox"/>. The static
/// <see cref="Show"/> mirrors MessageBox.Show's signature and return type, so a call site swaps one for the
/// other without touching its logic. The global crash handler in App.xaml.cs deliberately keeps the native
/// box: it must work even when the app's own resources cannot be loaded.
/// </summary>
public partial class WinPureDialog : Window
{
    private MessageBoxResult _result;

    private WinPureDialog() => InitializeComponent();

    public static MessageBoxResult Show(string message, string caption,
        MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var dlg = new WinPureDialog { Title = caption };
        dlg.TitleText.Text = caption;
        dlg.MessageText.Text = message;
        dlg.ApplyIcon(icon);
        dlg.BuildButtons(button, defaultResult);

        // Centre on the main window when there is one and it is not this dialog; otherwise centre on screen so a
        // dialog raised before the main window exists still appears sensibly.
        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner != dlg && owner.IsLoaded) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dlg.ShowDialog();
        return dlg._result;
    }

    private void ApplyIcon(MessageBoxImage icon)
    {
        (string glyph, object brushKeyOrColor) = icon switch
        {
            MessageBoxImage.Information => ("", "Brush.Accent"),
            MessageBoxImage.Question => ("", "Brush.Accent"),
            MessageBoxImage.Warning => ("", "Brush.Orange"),
            MessageBoxImage.Error => ("", (object)Color.FromRgb(0xF1, 0x70, 0x7A)),
            _ => ("", ""),
        };
        if (glyph.Length == 0)
        {
            IconGlyph.Visibility = Visibility.Collapsed;
            return;
        }
        IconGlyph.Text = glyph;
        IconGlyph.Foreground = brushKeyOrColor switch
        {
            string key when TryFindResource(key) is Brush b => b,
            Color c => new SolidColorBrush(c),
            _ => (Brush)FindResource("Brush.TextPrimary"),
        };
    }

    private void BuildButtons(MessageBoxButton button, MessageBoxResult defaultResult)
    {
        // The result when the dialog is closed by Esc or the title-bar X: the safe, non-committal choice.
        _result = button switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            _ => MessageBoxResult.Cancel,
        };

        switch (button)
        {
            case MessageBoxButton.OK:
                AddButton(Loc.T("OK"), MessageBoxResult.OK, primary: true, isDefault: true, isCancel: true);
                break;
            case MessageBoxButton.OKCancel:
                AddButton(Loc.T("Cancel"), MessageBoxResult.Cancel, primary: false, isDefault: false, isCancel: true);
                AddButton(Loc.T("OK"), MessageBoxResult.OK, primary: true, isDefault: defaultResult != MessageBoxResult.Cancel, isCancel: false);
                break;
            case MessageBoxButton.YesNo:
                AddButton(Loc.T("No"), MessageBoxResult.No, primary: false, isDefault: defaultResult == MessageBoxResult.No, isCancel: true);
                AddButton(Loc.T("Yes"), MessageBoxResult.Yes, primary: true, isDefault: defaultResult != MessageBoxResult.No, isCancel: false);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton(Loc.T("Cancel"), MessageBoxResult.Cancel, primary: false, isDefault: false, isCancel: true);
                AddButton(Loc.T("No"), MessageBoxResult.No, primary: false, isDefault: defaultResult == MessageBoxResult.No, isCancel: false);
                AddButton(Loc.T("Yes"), MessageBoxResult.Yes, primary: true, isDefault: defaultResult == MessageBoxResult.Yes, isCancel: false);
                break;
        }
    }

    private void AddButton(string text, MessageBoxResult result, bool primary, bool isDefault, bool isCancel)
    {
        var b = new Button
        {
            Content = text,
            Style = (Style)FindResource(primary ? "PrimaryButton" : "SubtleButton"),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 88,
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        b.Click += (_, _) => { _result = result; Close(); };
        Buttons.Children.Add(b);
    }
}
