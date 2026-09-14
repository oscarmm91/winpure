using System.Windows.Markup;
using WinPure.Services;

namespace WinPure.Views;

/// <summary>
/// Text="{l:Tr 'English text'}" in XAML: that text in the user's language, looked up once as the view loads.
/// Always quote it — the tests read the quoted text to know which translations the views need. A text with an
/// apostrophe cannot be written this way (the test reports it); translate that one in code with Loc.T instead.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
