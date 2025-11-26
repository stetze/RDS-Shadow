using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Windows.Storage;

using RDS_Shadow.Activation;
using RDS_Shadow.Contracts.Services;
using RDS_Shadow.Core.Contracts.Services;
using RDS_Shadow.Core.Services;
using RDS_Shadow.Helpers;
using RDS_Shadow.Models;
using RDS_Shadow.Services;
using RDS_Shadow.ViewModels;
using RDS_Shadow.Views;

namespace RDS_Shadow;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    public IHost Host
    {
        get;
    }

    public static T GetService<T>()
        where T : class
    {
        if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    private static WindowEx? _mainWindow;

    public static WindowEx MainWindow
    {
        get
        {
            if (_mainWindow is null)
            {
                _mainWindow = new MainWindow();
            }

            return _mainWindow;
        }
    }

    public static UIElement? AppTitlebar { get; set; }

    public App()
    {
        InitializeComponent();

        Host = Microsoft.Extensions.Hosting.Host.
        CreateDefaultBuilder().
        UseContentRoot(AppContext.BaseDirectory).
        ConfigureServices((context, services) =>
        {
            // Default Activation Handler
            services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

            // Other Activation Handlers

            // Services
            services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddTransient<INavigationViewService, NavigationViewService>();

            // Localization service
            services.AddSingleton<ILocalizationService, LocalizationService>();

            services.AddSingleton<IActivationService, ActivationService>();
            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<INavigationService, NavigationService>();

            // Core Services
            services.AddSingleton<IFileService, FileService>();

            // Views and ViewModels
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SettingsPage>();
            services.AddTransient<SessionsViewModel>();
            services.AddTransient<SessionsPage>();
            services.AddTransient<ShellPage>();
            services.AddTransient<ShellViewModel>();

            // Configuration
            services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
        }).
        Build();

        // Wire language change to update main window title and refresh current page
        var localization = GetService<ILocalizationService>();
        localization.LanguageChanged += (s, e) =>
        {
            if (MainWindow != null)
            {
                MainWindow.Title = "AppDisplayName".GetLocalized();
            }

            // Try to refresh the currently displayed page so XAML x:Uid resources reapply.
            try
            {
                var navigationService = GetService<INavigationService>();
                var frame = navigationService.Frame;
                var vm = frame?.GetPageViewModel();
                if (vm != null)
                {
                    var vmKey = vm.GetType().FullName!;
                    // Use a changing parameter to force the NavigationService to recreate the page
                    navigationService.NavigateTo(vmKey, parameter: System.Guid.NewGuid().ToString());
                }
            }
            catch
            {
                // ignore refresh failures
            }
        };

        // Apply previously saved language setting on startup (if present)
        try
        {
            if (ApplicationData.Current?.LocalSettings?.Values != null && ApplicationData.Current.LocalSettings.Values.TryGetValue("LanguageSetting", out var langObj) && langObj is string langStr && !string.IsNullOrWhiteSpace(langStr))
            {
                localization.ApplyLanguage(langStr);
            }
            else
            {
                // If setting is absent or empty, ensure LocalizationService uses system default (no override)
                localization.ApplyLanguage(string.Empty);
            }
        }
        catch
        {
            // ignore failures applying saved language
        }

        UnhandledException += App_UnhandledException;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // TODO: Log and handle exceptions as appropriate.
        // https://docs.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.application.unhandledexception.
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        await App.GetService<IActivationService>().ActivateAsync(args);
    }
}
