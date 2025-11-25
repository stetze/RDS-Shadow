using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using RDS_Shadow.Contracts.Services;
using RDS_Shadow.Helpers;
using RDS_Shadow.ViewModels;

using Windows.System;

namespace RDS_Shadow.Views;

// TODO: Update NavigationViewItem titles and icons in ShellPage.xaml.
public sealed partial class ShellPage : Page
{
    public ShellViewModel ViewModel
    {
        get;
    }

    private readonly ILocalizationService _localizationService;

    public ShellPage(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        _localizationService = App.GetService<ILocalizationService>();

        InitializeComponent();

        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.NavigationViewService.Initialize(NavigationViewControl);

        // A custom title bar is required for full window theme and Mica support.
        // https://docs.microsoft.com/windows/apps/develop/title-bar?tabs=winui3#full-customization
        App.MainWindow.ExtendsContentIntoTitleBar = true;
        App.MainWindow.SetTitleBar(AppTitleBar);
        App.MainWindow.Activated += MainWindow_Activated;

        AppTitleBarText.Text = "AppDisplayName".GetLocalized();

        _localization_service_subscribe();
    }

    private void _localization_service_subscribe()
    {
        _localizationService.LanguageChanged += LocalizationService_LanguageChanged;
    }

    private void LocalizationService_LanguageChanged(object? sender, System.EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            // Update App title
            AppTitleBarText.Text = "AppDisplayName".GetLocalized();

            // Update NavigationView menu items using Tag as resource key
            void UpdateItem(object item)
            {
                if (item is NavigationViewItem nvi)
                {
                    if (nvi.Tag is string tagKey && !string.IsNullOrEmpty(tagKey))
                    {
                        nvi.Content = tagKey.GetLocalized();
                    }

                    if (nvi.MenuItems != null && nvi.MenuItems.Count > 0)
                    {
                        foreach (var sub in nvi.MenuItems)
                        {
                            UpdateItem(sub);
                        }
                    }
                }
                else if (item is FrameworkElement fe && fe.Tag is string tagKey && !string.IsNullOrEmpty(tagKey))
                {
                    if (fe is NavigationViewItem nav)
                    {
                        nav.Content = tagKey.GetLocalized();
                    }
                }
            }

            foreach (var menuItem in NavigationViewControl.MenuItems)
            {
                UpdateItem(menuItem);
            }

            // Update settings item if present
            if (NavigationViewControl.SettingsItem is FrameworkElement settingsFe && settingsFe.Tag is string settingsTagKey)
            {
                if (NavigationViewControl.SettingsItem is NavigationViewItem settingsNvi)
                {
                    settingsNvi.Content = settingsTagKey.GetLocalized();
                }
            }

            // Force reload of the current page inside the NavigationFrame so x:Uid resources reapply
            try
            {
                if (NavigationFrame?.Content != null)
                {
                    var currentPageType = NavigationFrame.Content.GetType();
                    // Use a new parameter to force navigation even if same page type
                    NavigationFrame.Navigate(currentPageType, System.Guid.NewGuid().ToString());
                }
            }
            catch
            {
                // ignore navigation refresh errors
            }
        });
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        TitleBarHelper.UpdateTitleBar(RequestedTheme);

        KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu));
        KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.GoBack));
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        App.AppTitlebar = AppTitleBarText as UIElement;
    }

    private void NavigationViewControl_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        AppTitleBar.Margin = new Thickness()
        {
            Left = sender.CompactPaneLength * (sender.DisplayMode == NavigationViewDisplayMode.Minimal ? 2 : 1),
            Top = AppTitleBar.Margin.Top,
            Right = AppTitleBar.Margin.Right,
            Bottom = AppTitleBar.Margin.Bottom
        };
    }

    private static KeyboardAccelerator BuildKeyboardAccelerator(VirtualKey key, VirtualKeyModifiers? modifiers = null)
    {
        var keyboardAccelerator = new KeyboardAccelerator() { Key = key };

        if (modifiers.HasValue)
        {
            keyboardAccelerator.Modifiers = modifiers.Value;
        }

        keyboardAccelerator.Invoked += OnKeyboardAcceleratorInvoked;

        return keyboardAccelerator;
    }

    private static void OnKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var navigationService = App.GetService<INavigationService>();

        var result = navigationService.GoBack();

        args.Handled = result;
    }
}
