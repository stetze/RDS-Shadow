using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;
using Windows.Globalization;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

using RDS_Shadow.ViewModels;
using RDS_Shadow.Helpers; // for GetLocalized()
using RDS_Shadow.Contracts.Services;
using CommunityToolkit.WinUI.Controls;

namespace RDS_Shadow.Views;

public sealed partial class SettingsPage : Page
{
    private const string SqlServerSettingKey = "SqlServerSetting";
    private const string DatabaseNameSettingKey = "DatabaseNameSetting";
    private const string IncludeClientNameKey = "IncludeClientNameSetting";
    private const string LanguageSettingKey = "LanguageSetting";
    private const string TsNameSettingKey = "Settings_TSname";

    private readonly ILocalizationService _localizationService;
    private readonly IThemeSelectorService _themeSelectorService;

    // Track the language that was selected when the page was loaded so we only trigger a restart
    // if the user actually changed the selection before pressing Save.
    private string _initialSelectedLanguage = string.Empty;

    private string _systemLanguageAtLoad = string.Empty; // Track system language at page load

    // Track current applied language tag (e.g. "en-US" or "de-DE") to allow reverting if user cancels
    private string _currentLanguageTag = string.Empty;

    // Suppress handling of selection-changed events while page initializes
    private bool _suppressLanguageSelectionChanged = false;

    // Track previous values to detect deletions
    private string _previousSqlServer = string.Empty;
    private string _previousDatabaseName = string.Empty;
    private string _previousTsNames = string.Empty;

    // Suppress TextChanged/LostFocus handlers when programmatically restoring values
    private bool _suppressTextChange = false;

    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        _localizationService = App.GetService<ILocalizationService>();
        _themeSelectorService = App.GetService<IThemeSelectorService>();

        InitializeComponent();
        Loaded += SettingsPage_Loaded;

        ApplyLocalizedStrings();
    }

    private void LocalizationService_LanguageChanged(object? sender, System.EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => ApplyLocalizedStrings());
    }

    private void ApplyLocalizedStrings()
    {
        Settings_SQLServer.PlaceholderText = "Settings_SQLServer.PlaceholderText".GetLocalized();
        Settings_Databasename.PlaceholderText = "Settings_Databasename.PlaceholderText".GetLocalized();
        // Save button removed; do not set its content here.

        // Localize top language combobox items
        try
        {
            if (SettingsLanguageComboBox != null)
            {
                foreach (var obj in SettingsLanguageComboBox.Items)
                {
                    if (obj is ComboBoxItem item)
                    {
                        var tag = item.Tag as string ?? string.Empty;
                        switch (tag)
                        {
                            case "":
                            {
                                var sysText = "Settings_Language_SystemDefault.Content".GetLocalized();
                                item.Content = sysText == "Settings_Language_SystemDefault.Content" ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Systemstandard" : "System default") : sysText;
                                break;
                            }
                            case "en-US":
                            {
                                var enText = "Settings_Language_English.Content".GetLocalized();
                                item.Content = enText == "Settings_Language_English.Content" ? "English (United States)" : enText;
                                break;
                            }
                            case "de-DE":
                            {
                                var deText = "Settings_Language_German.Content".GetLocalized();
                                item.Content = deText == "Settings_Language_German.Content" ? "Deutsch (Deutschland)" : deText;
                                break;
                            }
                            default:
                                item.Content = tag;
                                break;
                        }
                    }
                }
            }
        }
        catch { }

        // Hint text under TS settings (use resource if present, otherwise fallback)
        var hint = "Settings_TSSettings_Hint".GetLocalized();
        if (hint == "Settings_TSSettings_Hint")
        {
            hint = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Für Terminalserver, die nicht über den RD‑Verbindungsbroker ermittelt werden." : "For terminal servers that are not discovered via the RD Connection Broker.";
        }
        try
        {
            var hintTb = this.FindName("Settings_TSSettings_Hint") as TextBlock;
            if (hintTb != null)
            {
                hintTb.Text = hint;
            }
        }
        catch { }

        // Set About header to app name from resources
        try
        {
            SettingsAboutHeaderText.Text = "AppDisplayName".GetLocalized();
        }
        catch { }
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _suppressLanguageSelectionChanged = true;

        try
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(SqlServerSettingKey, out var sqlServerSetting))
            {
                Settings_SQLServer.Text = sqlServerSetting?.ToString() ?? string.Empty;
                _previousSqlServer = Settings_SQLServer.Text;
            }

            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(DatabaseNameSettingKey, out var databaseNameSetting))
            {
                Settings_Databasename.Text = databaseNameSetting?.ToString() ?? string.Empty;
                _previousDatabaseName = Settings_Databasename.Text;
            }

            // Load TS names (comma-separated)
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(TsNameSettingKey, out var tsNameSetting))
            {
                Settings_TSname.Text = tsNameSetting?.ToString() ?? string.Empty;
                _previousTsNames = Settings_TSname.Text;
            }

            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(IncludeClientNameKey, out var includeClientObj) && includeClientObj is string includeClientStr && bool.TryParse(includeClientStr, out var includeClient))
            {
                try { Settings_IncludeClientToggle.IsOn = includeClient; } catch { }
            }
            else
            {
                try { Settings_IncludeClientToggle.IsOn = false; } catch { }
            }

            // Determine system fallback tag used for initial selection
            _systemLanguageAtLoad = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de-DE" : "en-US";

            // Initialize language selection in the top combobox
            string selectedLang = string.Empty;

            if (SettingsLanguageComboBox != null)
            {
                if (ApplicationData.Current.LocalSettings.Values.TryGetValue(LanguageSettingKey, out var langObj) && langObj is string langStr)
                {
                    if (string.IsNullOrWhiteSpace(langStr))
                    {
                        ApplicationData.Current.LocalSettings.Values.Remove(LanguageSettingKey);
                        langStr = string.Empty;
                    }

                    if (!string.IsNullOrEmpty(langStr))
                    {
                        var found = false;
                        foreach (var obj in SettingsLanguageComboBox.Items)
                        {
                            if (obj is ComboBoxItem item)
                            {
                                var tag = item.Tag as string ?? string.Empty;
                                if (string.Equals(tag, langStr, StringComparison.OrdinalIgnoreCase))
                                {
                                    SettingsLanguageComboBox.SelectedItem = item;
                                    found = true;
                                    break;
                                }
                            }
                        }

                        if (!found)
                        {
                            ApplicationData.Current.LocalSettings.Values.Remove(LanguageSettingKey);

                            foreach (var obj in SettingsLanguageComboBox.Items)
                            {
                                if (obj is ComboBoxItem item)
                                {
                                    var tag = item.Tag as string ?? string.Empty;
                                    if (string.Equals(tag, _systemLanguageAtLoad, StringComparison.OrdinalIgnoreCase))
                                    {
                                        SettingsLanguageComboBox.SelectedItem = item;
                                        found = true;
                                        break;
                                    }
                                }
                            }

                            if (!found && SettingsLanguageComboBox.Items.Count > 0)
                            {
                                SettingsLanguageComboBox.SelectedIndex = 0;
                            }
                        }

                        selectedLang = langStr;
                    }
                    else
                    {
                        var found = false;
                        foreach (var obj in SettingsLanguageComboBox.Items)
                        {
                            if (obj is ComboBoxItem item)
                            {
                                var tag = item.Tag as string ?? string.Empty;
                                if (string.Equals(tag, _systemLanguageAtLoad, StringComparison.OrdinalIgnoreCase))
                                {
                                    SettingsLanguageComboBox.SelectedItem = item;
                                    found = true;
                                    break;
                                }
                            }
                        }

                        if (!found && SettingsLanguageComboBox.Items.Count > 0)
                        {
                            SettingsLanguageComboBox.SelectedIndex = 0;
                        }

                        selectedLang = string.Empty;
                    }
                }
                else
                {
                    var found = false;
                    foreach (var obj in SettingsLanguageComboBox.Items)
                    {
                        if (obj is ComboBoxItem item)
                        {
                            var tag = item.Tag as string ?? string.Empty;
                            if (string.Equals(tag, _systemLanguageAtLoad, StringComparison.OrdinalIgnoreCase))
                            {
                                SettingsLanguageComboBox.SelectedItem = item;
                                found = true;
                                break;
                            }
                        }
                    }

                    if (!found && SettingsLanguageComboBox.Items.Count > 0)
                    {
                        SettingsLanguageComboBox.SelectedIndex = 0;
                    }

                    selectedLang = string.Empty;
                }
            }

            // Initialize theme ComboBox selection to reflect current ViewModel.ElementTheme
            try
            {
                if (SettingsThemeComboBox != null)
                {
                    var themeName = ViewModel?.ElementTheme.ToString() ?? ElementTheme.Default.ToString();
                    foreach (var obj in SettingsThemeComboBox.Items)
                    {
                        if (obj is ComboBoxItem item)
                        {
                            var tag = item.Tag as string ?? string.Empty;
                            if (string.Equals(tag, themeName, StringComparison.OrdinalIgnoreCase))
                            {
                                SettingsThemeComboBox.SelectedItem = item;
                                break;
                            }
                        }
                    }

                    // If nothing selected, pick first
                    if (SettingsThemeComboBox.SelectedItem == null && SettingsThemeComboBox.Items.Count > 0)
                    {
                        SettingsThemeComboBox.SelectedIndex = 0;
                    }
                }
            }
            catch { }

            // Save the initial selection so we can detect real user changes on Save
            _initialSelectedLanguage = selectedLang;
            _currentLanguageTag = selectedLang;
        }
        finally
        {
            // Re-enable handlers
            _suppressLanguageSelectionChanged = false;
        }
    }

    private async void Settings_Database_Click(object sender, RoutedEventArgs e)
    {
        // Save button is disabled because fields are saved automatically on LostFocus.
        // Keep method for compatibility but do nothing.
    }

    private void SaveDatabaseSettingsIfComplete()
    {
        try
        {
            var sql = Settings_SQLServer.Text?.Trim() ?? string.Empty;
            var db = Settings_Databasename.Text?.Trim() ?? string.Empty;

            // Only persist if both fields contain values
            if (!string.IsNullOrEmpty(sql) && !string.IsNullOrEmpty(db))
            {
                ApplicationData.Current.LocalSettings.Values[SqlServerSettingKey] = sql;
                ApplicationData.Current.LocalSettings.Values[DatabaseNameSettingKey] = db;
            }
        }
        catch { }
    }

    private void Settings_SQLServer_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveDatabaseSettingsIfComplete();
    }

    private void Settings_Databasename_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveDatabaseSettingsIfComplete();
    }

    private void Settings_TSname_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[TsNameSettingKey] = Settings_TSname.Text?.Trim() ?? string.Empty;
        }
        catch { }
    }

    private void Settings_LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Not used anymore (lower combobox removed)
    }

    // Handler: immediate apply when top card language ComboBox changes
    private async void SettingsLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageSelectionChanged) return;

        if (!(sender is ComboBox cb && cb.SelectedItem is ComboBoxItem selected && selected.Tag is string tag)) return;

        // If nothing changed, ignore
        if (string.Equals(tag, _currentLanguageTag, StringComparison.OrdinalIgnoreCase)) return;

        // Single dialog: ask to apply language and offer restart now or later
        var dialog = new ContentDialog
        {
            Title = "Settings_LanguageChange_Title".GetLocalized(),
            Content = "Settings_LanguageChange_Content".GetLocalized(),
            PrimaryButtonText = "Settings_LanguageChange_Restart".GetLocalized(), // Restart now
            CloseButtonText = "Settings_LanguageChange_Later".GetLocalized(),    // Apply later / later
            XamlRoot = this.XamlRoot,
            RequestedTheme = _themeSelectorService?.Theme ?? ElementTheme.Default
        };

        var result = await dialog.ShowAsync();

        // If user cancelled the dialog (closed without choosing Restart or Later) -> revert
        if (result != ContentDialogResult.Primary && result != ContentDialogResult.None)
        {
            // Unexpected value; revert to previous
            try
            {
                _suppressLanguageSelectionChanged = true;
                foreach (var obj in cb.Items)
                {
                    if (obj is ComboBoxItem item)
                    {
                        var t = item.Tag as string ?? string.Empty;
                        if (string.Equals(t, _currentLanguageTag, StringComparison.OrdinalIgnoreCase))
                        {
                            cb.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _suppressLanguageSelectionChanged = false;
            }

            return;
        }

        // User confirmed (either Restart (Primary) or Later (CloseButton) ) -> apply language
        try
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                ApplicationData.Current.LocalSettings.Values.Remove(LanguageSettingKey);
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values[LanguageSettingKey] = tag;
            }

            _localizationService.ApplyLanguage(tag);
            _currentLanguageTag = tag;

            // Sync selection state (no lower combobox exists now)

            // If user selected Restart now
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exe))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = exe,
                            UseShellExecute = true
                        });
                    }
                }
                catch { }

                Environment.Exit(0);
            }
        }
        catch
        {
            // ignore
        }
    }

    // New handler: when user selects an item in the card's ComboBox, execute the ViewModel SwitchThemeCommand
    private void SettingsThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem selected && selected.Tag is string tag)
        {
            if (Enum.TryParse(typeof(ElementTheme), tag, out var parsed))
            {
                var theme = (ElementTheme)parsed;
                // Use command if available
                if (ViewModel?.SwitchThemeCommand != null && ViewModel.SwitchThemeCommand.CanExecute(theme))
                {
                    ViewModel.SwitchThemeCommand.Execute(theme);
                }
                else
                {
                    // Fallback: call theme service directly
                    _ = _themeSelectorService?.SetThemeAsync(theme);
                }
            }
        }
    }

    private async void OnCardClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            // Prefer the localized TextBlock content if available
            var text = Settings_RepoCommand?.Text ?? "git clone https://github.com/stetze/RDS-Shadow.git";
            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();

            // Optionally provide lightweight feedback by briefly changing the header or similar (omitted)
        }
        catch
        {
            // ignore clipboard failures
        }
    }

    private void Settings_IncludeClientToggle_Toggled(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is ToggleSwitch ts)
            {
                // Persist to LocalSettings so it's update-persistent
                ApplicationData.Current.LocalSettings.Values[IncludeClientNameKey] = ts.IsOn.ToString();

                // Keep ViewModel in sync
                try { ViewModel.AskForClientName = ts.IsOn; } catch { }
            }
        }
        catch
        {
            // ignore persistence errors
        }
    }

    private async void Settings_SQLServer_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChange) return;

        try
        {
            if (string.IsNullOrWhiteSpace(Settings_SQLServer.Text))
            {
                // Confirm dialog: warn about clearing and offer to proceed or cancel
                var dialog = new ContentDialog
                {
                    Title = "Warning".GetLocalized(),
                    Content = "Settings_SqlServer_ClearWarning".GetLocalized(),
                    PrimaryButtonText = "Settings_SqlServer_ClearProceed".GetLocalized(), // Proceed
                    CloseButtonText = "Settings_SqlServer_ClearCancel".GetLocalized(),    // Cancel
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = _themeSelectorService?.Theme ?? ElementTheme.Default
                };

                // Show the dialog and wait for user response
                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // User confirmed, proceed with clearing
                    ApplicationData.Current.LocalSettings.Values[SqlServerSettingKey] = string.Empty;
                    _previousSqlServer = string.Empty;
                }
                else
                {
                    // User cancelled, revert the TextBox value
                    _suppressTextChange = true;
                    Settings_SQLServer.Text = _previousSqlServer;
                    _suppressTextChange = false;
                }
            }
            else
            {
                // Regular text change, just update the previous value
                _previousSqlServer = Settings_SQLServer.Text;
            }
        }
        catch { }
    }

    private async void Settings_Databasename_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChange) return;

        try
        {
            if (string.IsNullOrWhiteSpace(Settings_Databasename.Text))
            {
                // Confirm dialog: warn about clearing and offer to proceed or cancel
                var dialog = new ContentDialog
                {
                    Title = "Warning".GetLocalized(),
                    Content = "Settings_Database_ClearWarning".GetLocalized(),
                    PrimaryButtonText = "Settings_Database_ClearProceed".GetLocalized(), // Proceed
                    CloseButtonText = "Settings_Database_ClearCancel".GetLocalized(),    // Cancel
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = _themeSelectorService?.Theme ?? ElementTheme.Default
                };

                // Show the dialog and wait for user response
                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // User confirmed, proceed with clearing
                    ApplicationData.Current.LocalSettings.Values[DatabaseNameSettingKey] = string.Empty;
                    _previousDatabaseName = string.Empty;
                }
                else
                {
                    // User cancelled, revert the TextBox value
                    _suppressTextChange = true;
                    Settings_Databasename.Text = _previousDatabaseName;
                    _suppressTextChange = false;
                }
            }
            else
            {
                // Regular text change, just update the previous value
                _previousDatabaseName = Settings_Databasename.Text;
            }
        }
        catch { }
    }

    private async void Settings_TSname_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChange) return;

        try
        {
            if (string.IsNullOrWhiteSpace(Settings_TSname.Text))
            {
                // Confirm dialog: warn about clearing and offer to proceed or cancel
                var dialog = new ContentDialog
                {
                    Title = "Warning".GetLocalized(),
                    Content = "Settings_TSname_ClearWarning".GetLocalized(),
                    PrimaryButtonText = "Settings_TSname_ClearProceed".GetLocalized(), // Proceed
                    CloseButtonText = "Settings_TSname_ClearCancel".GetLocalized(),    // Cancel
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = _themeSelectorService?.Theme ?? ElementTheme.Default
                };

                // Show the dialog and wait for user response
                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // User confirmed, proceed with clearing
                    ApplicationData.Current.LocalSettings.Values[TsNameSettingKey] = string.Empty;
                    _previousTsNames = string.Empty;
                }
                else
                {
                    // User cancelled, revert the TextBox value
                    _suppressTextChange = true;
                    Settings_TSname.Text = _previousTsNames;
                    _suppressTextChange = false;
                }
            }
            else
            {
                // Regular text change, just update the previous value
                _previousTsNames = Settings_TSname.Text;
            }
        }
        catch { }
    }
}
