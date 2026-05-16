using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using dnSpy.Contracts.Settings;

namespace DnspyMcp.Settings;

[Export]
public sealed class McpSettings : INotifyPropertyChanged
{
    private static readonly Guid SettingsGuid =
        new("CE5B6E62-3A8A-4B6B-9C32-9E1B1F2D5D2E");

    private readonly ISettingsService _settings;
    private bool _enabled;
    private int _port = 8746;
    private string _bind = "127.0.0.1";
    private string _token = string.Empty;

    [ImportingConstructor]
    public McpSettings(ISettingsService settings)
    {
        _settings = settings;
        var section = settings.GetOrCreateSection(SettingsGuid);
        _enabled = section.Attribute<bool>(nameof(Enabled));
        _port = section.Attribute<int?>(nameof(Port)) ?? 8746;
        _bind = section.Attribute<string?>(nameof(Bind)) ?? "127.0.0.1";
        _token = section.Attribute<string?>(nameof(Token)) ?? string.Empty;
    }

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public int Port { get => _port; set => Set(ref _port, value); }
    public string Bind { get => _bind; set => Set(ref _bind, value); }
    public string Token { get => _token; set => Set(ref _token, value); }

    public void Save()
    {
        var section = _settings.GetOrCreateSection(SettingsGuid);
        section.Attribute(nameof(Enabled), _enabled);
        section.Attribute(nameof(Port), _port);
        section.Attribute(nameof(Bind), _bind);
        section.Attribute(nameof(Token), _token);
        Applied?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Applied;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
