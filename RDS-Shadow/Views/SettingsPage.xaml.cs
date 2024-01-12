﻿using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using RDS_Shadow.ViewModels;
using Windows.Storage;

namespace RDS_Shadow.Views;

public sealed partial class SettingsPage : Page
{
    private const string SqlServerSettingKey = "SqlServerSetting";
    private const string DatabaseNameSettingKey = "DatabaseNameSetting";

    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }
    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ApplicationData.Current.LocalSettings.Values.TryGetValue(SqlServerSettingKey, out var sqlServerSetting))
        {
            Settings_SQLServer.Text = sqlServerSetting.ToString();
        }

        if (ApplicationData.Current.LocalSettings.Values.TryGetValue(DatabaseNameSettingKey, out var databaseNameSetting))
        {
            Settings_Databasename.Text = databaseNameSetting.ToString();
        }
    }
    private void Settings_Database_Click(object sender, RoutedEventArgs e)
    {
        ApplicationData.Current.LocalSettings.Values[SqlServerSettingKey] = Settings_SQLServer.Text;
        ApplicationData.Current.LocalSettings.Values[DatabaseNameSettingKey] = Settings_Databasename.Text;
    }
}
