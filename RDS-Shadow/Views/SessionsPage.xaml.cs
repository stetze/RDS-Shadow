using System.Collections.ObjectModel;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Data.SqlClient;
using RDS_Shadow.ViewModels;
using System.Data;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using ColorCode.Compilation.Languages;
using RDS_Shadow.Helpers; // for GetLocalized()
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;

namespace RDS_Shadow.Views;

public sealed partial class SessionsPage : Page
{
    public SessionsViewModel ViewModel { get; }

    public SessionsPage()
    {
        ViewModel = App.GetService<SessionsViewModel>();
        InitializeComponent();

        // Set UI texts (fallback to German professional labels)
        colUsername.Header = "Benutzer";
        colPoolName.Header = "Pool";
        colServerName.Header = "Server";
        colSessionId.Header = "Sitzungs-ID";

        ToolTipService.SetToolTip(refresh, new ToolTip { Content = "Aktualisieren" });
        filterUsername.PlaceholderText = "Filter (Benutzer oder Server)";
        ToolTipService.SetToolTip(sendMessageToAllUser, new ToolTip { Content = "Nachricht an alle senden" });

        SendAllButton.Content = "Alle benachrichtigen";
        SendButton.Content = "Senden";
        Sessions_MessageAllTitle.Text = "Nachricht an alle Nutzer";
        messageAllTextBox.PlaceholderText = "Nachricht eingeben...";

        // Ensure the list is populated automatically when the page is first shown
        Loaded += SessionsPage_Loaded;
    }

    private async void SessionsPage_Loaded(object? sender, RoutedEventArgs e)
    {
        // Populate on first load and apply current filter
        await PopulateList(firstTime: true);
        ApplyFilter();

        // Unregister handler to avoid repeated loading
        Loaded -= SessionsPage_Loaded;
    }

    public class MyDataClass
    {
        public string Username { get; set; }
        public string PoolName { get; set; }
        public string ServerName { get; set; }
        public int SessionId { get; set; }

        public MyDataClass(string userName, string poolName, string serverName, int sessionId)
        {
            Username = userName;
            PoolName = poolName;
            ServerName = serverName;
            SessionId = sessionId;
        }
    }

    private readonly ObservableCollection<MyDataClass> MyData = new ObservableCollection<MyDataClass>();

    // Semaphore to serialize ContentDialog.ShowAsync calls to avoid the "Only a single ContentDialog can be open at any time" COMException
    private readonly SemaphoreSlim _dialogSemaphore = new SemaphoreSlim(1, 1);

    private async Task ShowContentDialogSerializedAsync(ContentDialog dialog)
    {
        await _dialogSemaphore.WaitAsync();
        try
        {
            await dialog.ShowAsync();
        }
        catch (COMException)
        {
            // If another dialog is shown concurrently, swallow or log as needed.
        }
        finally
        {
            _dialogSemaphore.Release();
        }
    }

    private async Task PopulateList(bool firstTime)
    {
        var server = Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("SqlServerSetting", out var s) ? s as string : null;
        var db = Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("DatabaseNameSetting", out var d) ? d as string : null;

        if (!string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(db))
        {
            // Shorten connection timeout so errors surface quickly
            var connectionString = $"Server={server};Database={db};Integrated Security=SSPI;TrustServerCertificate=true;Connect Timeout=3";

            try
            {
                MyData.Clear();
                using SqlConnection connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                using SqlCommand command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM Shadowing ORDER BY Username ASC";

                using SqlDataReader reader = await command.ExecuteReaderAsync();

                if (firstTime)
                {
                    while (await reader.ReadAsync())
                    {
                        var username = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                        var poolName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        var serverName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        var sessionId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);

                        MyData.Add(new MyDataClass(username, poolName, serverName, sessionId));
                    }
                }
            }
            catch (SqlException ex)
            {
                // Show a friendly dialog to the user with localized text
                var title = "Datenbank-Verbindungsfehler";
                var content = "Es konnte keine Verbindung zur Datenbank hergestellt werden. Bitte prüfen Sie die Einstellungen und Ihre Netzwerkverbindung.";

                content = content + "\n\n" + ex.Message;

                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = content,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };

                await ShowContentDialogSerializedAsync(dialog);
            }
            catch (Exception ex)
            {
                // Unexpected error: show generic dialog
                var dialog = new ContentDialog
                {
                    Title = "Fehler",
                    Content = ex.Message,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };

                await ShowContentDialogSerializedAsync(dialog);
            }
        }
    }

    private void shadowingView_Sorting(object sender, DataGridColumnEventArgs e)
    {
        if (e.Column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding)
        {
            var propertyName = binding.Path.Path;

            var itemsToSort = string.IsNullOrEmpty(filterUsername.Text)
                ? MyData
                : new ObservableCollection<MyDataClass>(MyData.Where(item => item.Username.IndexOf(filterUsername.Text, StringComparison.OrdinalIgnoreCase) >= 0));

            var sortedItems = propertyName switch
            {
                "Username" => itemsToSort.OrderBy(item => item.Username),
                "PoolName" => itemsToSort.OrderBy(item => item.PoolName),
                "ServerName" => itemsToSort.OrderBy(item => item.ServerName),
                "SessionId" => itemsToSort.OrderBy(item => item.SessionId),
                _ => throw new InvalidOperationException("Invalid property name"),
            };

            if (e.Column.SortDirection == null || e.Column.SortDirection == DataGridSortDirection.Descending)
            {
                shadowingView.ItemsSource = new ObservableCollection<MyDataClass>(sortedItems);
                e.Column.SortDirection = DataGridSortDirection.Ascending;
            }
            else
            {
                shadowingView.ItemsSource = new ObservableCollection<MyDataClass>(sortedItems.Reverse());
                e.Column.SortDirection = DataGridSortDirection.Descending;
            }

            // Remove sorting indicators from other columns
            foreach (var dgColumn in shadowingView.Columns)
            {
                if (dgColumn != e.Column)
                {
                    dgColumn.SortDirection = null;
                }
            }
        }
    }

    private void filterUsername_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private string currentFilter = string.Empty;

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await PopulateList(firstTime: true);
        ApplyFilter(); // Apply the current filter after refreshing the list
    }

    private void ApplyFilter()
    {
        currentFilter = filterUsername.Text;

        if (string.IsNullOrEmpty(currentFilter))
        {
            shadowingView.ItemsSource = MyData;
        }
        else
        {
            // Filter anwenden: Suche nach Username oder ServerName
            var filteredData = MyData.Where(item =>
                item.Username.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.ServerName.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0);

            shadowingView.ItemsSource = new ObservableCollection<MyDataClass>(filteredData);
        }
    }

    private void DataGrid_DoubleTapped(object sender, RoutedEventArgs e)
    {
        if (shadowingView.SelectedItem is MyDataClass selectedRow)
        {
            try
            {
                Process.Start("mstsc.exe", $"/v:{selectedRow.ServerName} /shadow:{selectedRow.SessionId} /control");
            }
            catch (Exception)
            {
                // ignore
            }
        }
    }

    private void DataGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var grid = (DataGrid)sender;
        var originalSource = e.OriginalSource as FrameworkElement;

        // Überprüfen Sie, ob der Rechtsklick auf einer Zeile erfolgt
        if (originalSource != null && originalSource.DataContext is MyDataClass clickedRow)
        {
            // Setzen Sie die Auswahl manuell
            grid.SelectedItem = clickedRow;

            // Zeigen Sie das Kontextmenü an
            var menu = new MenuFlyout();
            var abmeldenItem = new MenuFlyoutItem { Text = "Abmelden" };
            abmeldenItem.Click += Abmelden_Click;
            menu.Items.Add(abmeldenItem);

            var sendMessageItem = new MenuFlyoutItem { Text = "Nachricht senden" };
            sendMessageItem.Click += SendMessageToUser_Click;
            menu.Items.Add(sendMessageItem);

            menu.ShowAt(grid, e.GetPosition(grid));
            e.Handled = true;
        }
    }

    private void Abmelden_Click(object sender, RoutedEventArgs e)
    {
        if (shadowingView.SelectedItem is MyDataClass selectedRow)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "logoff",
                    Arguments = $"{selectedRow.SessionId} /server:{selectedRow.ServerName}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MyData.Add(new MyDataClass("Fehler", $"Abmelden fehlgeschlagen: {ex.Message}", "", 0));
            }
        }
    }

    private void SendMessageToUser_Click(object sender, RoutedEventArgs e)
    {
        if (shadowingView.SelectedItem is MyDataClass selectedRow)
        {
            // Öffnen Sie das Flyout für die Nachrichteneingabe
            messageFlyout.ShowAt(shadowingView, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Full });
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (shadowingView.SelectedItem is MyDataClass selectedRow)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "msg",
                    Arguments = $"{selectedRow.SessionId} /server:{selectedRow.ServerName} {messageTextBox.Text}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);

                // Schließen Sie das Flyout nach dem Senden der Nachricht
                messageFlyout.Hide();
            }
            catch (Exception ex)
            {
                MyData.Add(new MyDataClass("Fehler", $"Nachricht senden fehlgeschlagen: {ex.Message}", "", 0));
            }
        }
    }

    private async void SendAllButton_Click(object sender, RoutedEventArgs e)
    {
        // Stellen Sie sicher, dass die Liste vor dem Senden aktualisiert wird
        await PopulateList(firstTime: true);
        ApplyFilter();  // Wenden Sie den aktuellen Filter an, um sicherzustellen, dass die gefilterte Liste aktuell ist

        try
        {
            string messageToAllUsers = messageAllTextBox.Text;

            // Verwenden Sie die aktuell angezeigte (gefilterte) Liste
            var filteredData = shadowingView.ItemsSource as ObservableCollection<MyDataClass>;

            // Überprüfen Sie, ob filteredData nicht null ist, bevor Sie fortfahren
            if (filteredData != null)
            {
                foreach (var user in filteredData)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "msg",
                            Arguments = $"{user.SessionId} /server:{user.ServerName} {messageToAllUsers}",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };

                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        MyData.Add(new MyDataClass("Fehler", $"Nachricht an {user.Username} fehlgeschlagen: {ex.Message}", "", 0));
                    }
                }
            }
            else
            {
                MyData.Add(new MyDataClass("Warnung", "Keine Nutzer verfügbar.", "", 0));
            }

            // Schließen Sie das Flyout nach dem Senden der Nachrichten
            messageAllFlyout.Hide();
        }
        catch (Exception ex)
        {
            MyData.Add(new MyDataClass("Fehler", $"Nachricht an alle fehlgeschlagen: {ex.Message}", "", 0));
        }
    }
}
