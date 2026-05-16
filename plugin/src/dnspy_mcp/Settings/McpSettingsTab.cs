using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using dnSpy.Contracts.Images;
using dnSpy.Contracts.Settings.Dialog;

namespace DnspyMcp.Settings;

[Export(typeof(IAppSettingsPageProvider))]
public sealed class McpSettingsTabProvider : IAppSettingsPageProvider
{
    private readonly McpSettings _settings;

    [ImportingConstructor]
    public McpSettingsTabProvider(McpSettings settings) => _settings = settings;

    public IEnumerable<AppSettingsPage> Create()
    {
        yield return new McpSettingsTab(_settings);
    }
}

internal sealed class McpSettingsTab : AppSettingsPage
{
    private static readonly Guid TabGuid =
        new("FCEDBE60-5D5F-4D58-AC65-7A2A48D2DD7B");
    private readonly McpSettings _settings;

    public McpSettingsTab(McpSettings settings) => _settings = settings;
    public override double Order => 0;
    public override Guid ParentGuid =>
        new("AB46F37A-95E8-4FFC-9B0A-7E7C4D9D6213"); // Misc parent
    public override Guid Guid => TabGuid;
    public override string Title => "dnspy-mcp";
    public override object UIObject => new McpSettingsPage(_settings);
    public override ImageReference Icon => ImageReference.None;

    public override void OnApply() => _settings.Save();
    public override void OnClosed() { }
}
