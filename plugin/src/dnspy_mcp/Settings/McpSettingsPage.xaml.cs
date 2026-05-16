using System.Windows.Controls;

namespace DnspyMcp.Settings;

public partial class McpSettingsPage : UserControl
{
    public McpSettingsPage(McpSettings settings)
    {
        InitializeComponent();
        DataContext = settings;
    }
}
