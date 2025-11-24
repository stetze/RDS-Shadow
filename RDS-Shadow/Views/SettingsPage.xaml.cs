using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;
using Windows.Globalization;

using RDS_Shadow.ViewModels;
using Windows.Storage;
using RDS_Shadow.Helpers; // for GetLocalized()

namespace RDS_Shadow.Views;

public sealed partial class SettingsPage : Page
{
    private const string SqlServerSettingKey = "SqlServerSetting";
    private const string DatabaseNameSettingKey = "DatabaseNameSetting";
    private const string IncludeClientNameKey = "IncludeClientNameSetting";
    private const string LanguageSettingKey = "LanguageSetting";

    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
        Loaded += SettingsPage_Loaded;

        // Localize placeholders and buttons
        Settings_SQLServer.PlaceholderText = "Settings_SQLServer.PlaceholderText".GetLocalized();
        Settings_Databasename.PlaceholderText = "Settings_Databasename.PlaceholderText".GetLocalized();
        Settings_Database_SaveButton.Content = "Settings_Database_SaveButton.Content".GetLocalized();

        // Localize the new checkbox label with fallback
        var key = "Settings_IncludeClientName.Label";
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

        // Language label with fallback
        var langKey = "Settings_Language.Label";
        var langLocalized = langKey.GetLocalized();
        if (langLocalized == langKey)
        {
            Settings_LanguageLabel.Text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Sprache" : "Language";
        }
        else
        {
            Settings_LanguageLabel.Text = langLocalized;
        }
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

        // Initialize language combobox selection
        if (ApplicationData.Current.LocalSettings.Values.TryGetValue(LanguageSettingKey, out var langObj) && langObj is string langStr && !string.IsNullOrEmpty(langStr))
        {
            foreach (var obj in Settings_LanguageComboBox.Items)
            {
                if (obj is ComboBoxItem item)
                {
                    var tag = item.Tag as string ?? string.Empty;
                    if (string.Equals(tag, langStr, StringComparison.OrdinalIgnoreCase))
                    {
                        Settings_LanguageComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }
        else
        {
            // Select system default
            Settings_LanguageComboBox.SelectedIndex = 0;
        }
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
            newLang = t;
            ApplicationData.Current.LocalSettings.Values[LanguageSettingKey] = t;
        }

        // If language changed and not empty, ask user to restart so resources reload cleanly
        if (!string.IsNullOrEmpty(newLang) && !string.Equals(prevLang, newLang, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // Persist PrimaryLanguageOverride now so restart uses it
                ApplicationLanguages.PrimaryLanguageOverride = newLang;

                var dialog = new ContentDialog
                {
                    Title = "Settings_LanguageChange_Title".GetLocalized(),
                    Content = "Settings_LanguageChange_Content".GetLocalized(),
                    PrimaryButtonText = "Settings_LanguageChange_Restart".GetLocalized(),
                    CloseButtonText = "Settings_LanguageChange_Later".GetLocalized(),
                    XamlRoot = this.XamlRoot
                };

                var res = await dialog.ShowAsync();
                if (res == ContentDialogResult.Primary)
                {
                    // Restart application: start new process and exit current
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
                    catch
                    {
                        // ignore
                    }

                    // Exit current process
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
