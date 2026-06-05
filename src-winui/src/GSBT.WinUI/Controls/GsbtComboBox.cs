using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace GSBT.WinUI.Controls;

/// <summary>
/// App-standard <see cref="ComboBox"/> with classic downward dropdown and arrow cursor.
/// </summary>
public class GsbtComboBox : ComboBox
{
    public GsbtComboBox()
    {
        GsbtComboBoxChrome.SetClassicDropdown(this, true);
    }

    internal void SetArrowCursor() =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
}
