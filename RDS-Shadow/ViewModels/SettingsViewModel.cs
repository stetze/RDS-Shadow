using System.Reflection;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml;

using RDS_Shadow.Contracts.Services;
using RDS_Shadow.Helpers;

using Windows.ApplicationModel;
using Windows.Storage;

namespace RDS_Shadow.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    private readonly IThemeSelectorService _themeSelectorService;

    private ElementTheme _elementTheme;
    public ElementTheme ElementTheme
    {
        get => _elementTheme;
        set => SetProperty(ref _elementTheme, value);
    }

    private string _versionDescription = string.Empty;
    public string VersionDescription
    {
        get => _versionDescription;
        set => SetProperty(ref _versionDescription, value);
    }

    // New: version number only (e.g. "1.2.3.4")
    private string _version = string.Empty;
    public string Version
    {
        get => _version;
        private set => SetProperty(ref _version, value);
    }

    // New: Ask for client name setting
    private bool _askForClientName;
    public bool AskForClientName
    {
        get => _askForClientName;
        set => SetProperty(ref _askForClientName, value);
    }

    public ICommand SwitchThemeCommand
    {
        get;
    }

    public SettingsViewModel(IThemeSelectorService themeSelectorService)
    {
        _themeSelectorService = themeSelectorService;
        ElementTheme = _themeSelectorService.Theme;

        // compute version number once
        Version = GetVersionOnly();
        VersionDescription = $"{"AppDisplayName".GetLocalized()} - {Version}";

        // Initialize AskForClientName from LocalSettings if present
        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue("IncludeClientNameSetting", out var obj) && obj is string s && bool.TryParse(s, out var parsed))
            {
                _askForClientName = parsed;
            }
            else
            {
                _askForClientName = false;
            }
        }
        catch
        {
            _askForClientName = false;
        }

        SwitchThemeCommand = new RelayCommand<ElementTheme>(
            async (param) =>
            {
                if (ElementTheme != param)
                {
                    ElementTheme = param;
                    await _themeSelectorService.SetThemeAsync(param);
                }
            });
    }

    private static string GetVersionOnly()
    {
        Version version;

        if (RuntimeHelper.IsMSIX)
        {
            var packageVersion = Package.Current.Id.Version;

            version = new(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        }
        else
        {
            version = Assembly.GetExecutingAssembly().GetName().Version!;
        }

        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}
