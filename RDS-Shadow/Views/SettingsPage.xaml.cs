using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;
using Windows.Globalization;

using RDS_Shadow.ViewModels;
using Windows.Storage;
using RDS_Shadow.Helpers; // for GetLocalized()
using RDS_Shadow.Contracts.Services;

namespace RDS_Shadow.Views;

public sealed partial class SettingsPage : Page
{
    private const string SqlServerSettingKey = "SqlServerSetting";
    private const string DatabaseNameSettingKey = "DatabaseNameSetting";
    private const string IncludeClientNameKey = "IncludeClientNameSetting";
    private const string LanguageSettingKey = "LanguageSetting";

    private readonly ILocalizationService _localizationService;
    private readonly IThemeSelectorService _themeSelectorService;

    // Track the language that was selected when the page was loaded so we only trigger a restart
    // if the user actually changed the selection before pressing Save.
    private string _initialSelectedLanguage = string.Empty;
    private bool _hadStoredLanguage = false;
    private string _systemLanguageAtLoad = string.Empty;

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

        _localizationService.LanguageChanged += LocalizationService_LanguageChanged;

        ApplyLocalizedStrings();
    }

    private void LocalizationService_LanguageChanged(object? sender, System.EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => ApplyLocalizedStrings());
    }

    private void ApplyLocalizedStrings()
    {
        // Localize placeholders and buttons
        Settings_SQLServer.PlaceholderText = "Settings_SQLServer.PlaceholderText".GetLocalized();
        Settings_Databasename.PlaceholderText = "Settings_Databasename.PlaceholderText".GetLocalized();
        Settings_Database_SaveButton.Content = "Settings_Database_SaveButton.Content".GetLocalized();

        // Localize checkbox label
        var key = "Settings_IncludeClientName.Content";
        var localized = key.GetLocalized();
        if (localized == key)
        {
            var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            Settings_IncludeClientNameCheckBox.Content = lang == "de" ? "Clientname abfragen" : "Include client name";
        }
        else
        {
            Settings_IncludeClientNameCheckBox.Content = localized;
        }

        // Language label
        var langKey = "Settings_Language.Text";
        var langLocalized = langKey.GetLocalized();
        if (langLocalized == langKey)
        {
            Settings_LanguageLabel.Text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Sprache" : "Language";
        }
        else
        {
            Settings_LanguageLabel.Text = langLocalized;
        }

        // Populate combobox item contents from resources
        if (Settings_LanguageComboBox.Items.Count > 0)
        {
            foreach (var obj in Settings_LanguageComboBox.Items)
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

        // Localize About and OtherSettings headers and privacy link
        var aboutText = "Settings_About.Text".GetLocalized();
        Settings_About.Text = aboutText == "Settings_About.Text" ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Über diese Anwendung" : "About this app") : aboutText;

        var aboutDesc = "Settings_AboutDescription.Text".GetLocalized();
        Settings_AboutDescription.Text = aboutDesc == "Settings_AboutDescription.Text" ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Beschreibung zur Anwendung" : "Description about the application") : aboutDesc;

        var otherText = "Settings_OtherSettings.Text".GetLocalized();
        Settings_OtherSettings.Text = otherText == "Settings_OtherSettings.Text" ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Sonstige Einstellungen" : "Other settings") : otherText;

        var privacyText = "SettingsPage_PrivacyTermsLink.Content".GetLocalized();
        SettingsPage_PrivacyTermsLink.Content = privacyText == "SettingsPage_PrivacyTermsLink.Content" ? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Datenschutz" : "Privacy & Terms") : privacyText;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ApplicationData.Current.LocalSettings.Values.TryGetValue(SqlServerSettingKey, out var sqlServerSetting))
        {
            Settings_SQLServer.Text = sqlServerSetting?.ToString() ?? string.Empty;
        }

        if (ApplicationData.Current.LocalSettings.Values.TryGetValue(DatabaseNameSettingKey, out var databaseNameSetting))
        {
            Settings_Databasename.Text = databaseNameSetting?.ToString() ?? string.Empty;
        }

        if (ApplicationData.Current.LocalSettings.Values.TryGetValue(IncludeClientNameKey, out var includeClientObj) && includeClientObj is string includeClientStr && bool.TryParse(includeClientStr, out var includeClient))
        {
            Settings_IncludeClientNameCheckBox.IsChecked = includeClient;
        }
        else
        {
            Settings_IncludeClientNameCheckBox.IsChecked = false;
        }

        // Determine system fallback tag used for initial selection
        _systemLanguageAtLoad = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de-DE" : "en-US";

        // Initialize language combobox selection
        string selectedLang = string.Empty;
        _hadStoredLanguage = false;
        if (ApplicationData.Current.LocalSettings.Values.TryGetValue(LanguageSettingKey, out var langObj) && langObj is string langStr)
        {
            // If stored value is empty (previously used system-default), remove it so it doesn't cause confusion
            if (string.IsNullOrWhiteSpace(langStr))
            {
                ApplicationData.Current.LocalSettings.Values.Remove(LanguageSettingKey);
                langStr = string.Empty;
            }

            if (!string.IsNullOrEmpty(langStr))
            {
                _hadStoredLanguage = true;
                var found = false;
                foreach (var obj in Settings_LanguageComboBox.Items)
                {
                    if (obj is ComboBoxItem item)
                    {
                        var tag = item.Tag as string ?? string.Empty;
                        if (string.Equals(tag, langStr, StringComparison.OrdinalIgnoreCase))
                        {
                            Settings_LanguageComboBox.SelectedItem = item;
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    // Stored value doesn't match any available item -> remove and fallback to system language
                    ApplicationData.Current.LocalSettings.Values.Remove(LanguageSettingKey);

                    foreach (var obj in Settings_LanguageComboBox.Items)
                    {
                        if (obj is ComboBoxItem item)
                        {
                            var tag = item.Tag as string ?? string.Empty;
                            if (string.Equals(tag, _systemLanguageAtLoad, StringComparison.OrdinalIgnoreCase))
                            {
                                Settings_LanguageComboBox.SelectedItem = item;
                                found = true;
                                break;
                            }
                        }
                    }

                    if (!found && Settings_LanguageComboBox.Items.Count > 0)
                    {
                        Settings_LanguageComboBox.SelectedIndex = 0;
                    }
                }

                selectedLang = langStr;
            }
            else
            {
                // No stored language -> pick best guess from system language
                var found = false;
                foreach (var obj in Settings_LanguageComboBox.Items)
                {
                    if (obj is ComboBoxItem item)
                    {
                        var tag = item.Tag as string ?? string.Empty;
                        if (string.Equals(tag, _systemLanguageAtLoad, StringComparison.OrdinalIgnoreCase))
                        {
                            Settings_LanguageComboBox.SelectedItem = item;
                            found = true;
                            break;
                        }
                    }
                }

                if (!found && Settings_LanguageComboBox.Items.Count > 0)
                {
                    Settings_LanguageComboBox.SelectedIndex = 0;
                }

                // For initial selection, treat empty stored value as empty (system default)
                selectedLang = string.Empty;
            }
        }
        else
        {
            // No saved value at all -> pick best guess from system language
            var found = false;
            foreach (var obj in Settings_LanguageComboBox.Items)
            {
                if (obj is ComboBoxItem item)
                {
                    var tag = item.Tag as string ?? string.Empty;
                    if (string.Equals(tag, _systemLanguageAtLoad, StringComparison.OrdinalIgnoreCase))
                    {
                        Settings_LanguageComboBox.SelectedItem = item;
                        found = true;
                        break;
                    }
                }
            }

            if (!found && Settings_LanguageComboBox.Items.Count > 0)
            {
                Settings_LanguageComboBox.SelectedIndex = 0;
            }

            // No stored language -> initial selection is empty (system default)
            selectedLang = string.Empty;
        }

        // Save the initial selection so we can detect real user changes on Save
        _initialSelectedLanguage = selectedLang;
    }

    private async void Settings_Database_Click(object sender, RoutedEventArgs e)
    {
        var prevLang = ApplicationData.Current.LocalSettings.Values.TryGetValue(LanguageSettingKey, out var prev) && prev is string ? (string)prev! : string.Empty;

        ApplicationData.Current.LocalSettings.Values[SqlServerSettingKey] = Settings_SQLServer.Text;
        ApplicationData.Current.LocalSettings.Values[DatabaseNameSettingKey] = Settings_Databasename.Text;
        ApplicationData.Current.LocalSettings.Values[IncludeClientNameKey] = (Settings_IncludeClientNameCheckBox.IsChecked ?? false).ToString();

        // Save language selection
        string newLang = string.Empty;
        if (Settings_LanguageComboBox.SelectedItem is ComboBoxItem sel && sel.Tag is string t)
        {
            // If there was no stored language initially and the selected tag equals the system fallback,
            // treat this as "no change" and keep stored value empty.
            if (!_hadStoredLanguage && string.Equals(t, _systemLanguageAtLoad, StringComparison.OrdinalIgnoreCase))
            {
                // keep newLang empty to represent system default
                newLang = string.Empty;
                ApplicationData.Current.LocalSettings.Values.Remove(LanguageSettingKey);
            }
            else
            {
                newLang = t;
                if (string.IsNullOrWhiteSpace(newLang))
                {
                    // User selected system default explicitly: remove stored override
                    ApplicationData.Current.LocalSettings.Values.Remove(LanguageSettingKey);
                }
                else
                {
                    ApplicationData.Current.LocalSettings.Values[LanguageSettingKey] = t;
                }
            }
        }

        // Only consider language "changed" if the user actually changed the selection since the page loaded
        if (!string.Equals(_initialSelectedLanguage, newLang, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // Persist PrimaryLanguageOverride now so restart uses it and notify listeners
                _localizationService.ApplyLanguage(newLang);

                var dialog = new ContentDialog
                {
                    Title = "Settings_LanguageChange_Title".GetLocalized(),
                    Content = "Settings_LanguageChange_Content".GetLocalized(),
                    PrimaryButtonText = "Settings_LanguageChange_Restart".GetLocalized(),
                    CloseButtonText = "Settings_LanguageChange_Later".GetLocalized(),
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = _themeSelectorService?.Theme ?? ElementTheme.Default
                };

                var res = await dialog.ShowAsync();
                if (res == ContentDialogResult.Primary)
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

        // Optional: show a localized confirmation (not implemented visually here)
    }

    private void Settings_LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Do nothing here; language saved on Save button. Could implement immediate switch.
    }
}
