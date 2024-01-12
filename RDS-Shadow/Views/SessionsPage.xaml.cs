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

namespace RDS_Shadow.Views;

public sealed partial class SessionsPage : Page
{
    public SessionsViewModel ViewModel
    {
        get;
    }

    public SessionsPage()
    {
        ViewModel = App.GetService<SessionsViewModel>();
        InitializeComponent();
        PopulateList(true);
    }
    public class MyDataClass
    {
        public string Username
        {
            get; set;
        }
        public string PoolName
        {
            get; set;
        }
        public string ServerName
        {
            get; set;
        }
        public int SessionId
        {
            get; set;
        }

        public MyDataClass(string userName, string poolName, string serverName, int sessionId)
        {
            Username = userName;
            PoolName = poolName;
            ServerName = serverName;
            SessionId = sessionId;
        }
    }
    private readonly ObservableCollection<MyDataClass> MyData = new ObservableCollection<MyDataClass>();

    private void PopulateList(bool firstTime)
    {
        var server = (string)Windows.Storage.ApplicationData.Current.LocalSettings.Values["SqlServerSetting"];
        var db = (string)Windows.Storage.ApplicationData.Current.LocalSettings.Values["DatabaseNameSetting"];

        if (!string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(db))
        {
            var connectionString = $"Server={server};Database={db};Integrated Security=SSPI;TrustServerCertificate=true";

            try
            {
                MyData.Clear();
                using SqlConnection connection = new SqlConnection(connectionString);
                connection.Open();
                SqlCommand command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM Shadowing ORDER BY Username ASC";
                SqlDataReader reader = command.ExecuteReader();

                if (firstTime)
                {
                    while (reader.Read())
                    {
                        var username = reader.GetString(0); // Assuming the column index of the username is 0
                        var poolName = reader.GetString(1); // Assuming the column index of the pool name is 1
                        var serverName = reader.GetString(2); // Assuming the column index of the server name is 2
                        var sessionId = reader.GetInt32(3); // Assuming the column index of the session ID is 3

                        MyData.Add(new MyDataClass(username, poolName, serverName, sessionId));
                    }
                    reader.Close();
                    connection.Close();
                }
            }
            catch (SqlException)
            {
                MyData.Add(new MyDataClass("Error", "Leider konnte keine Verbindung zu Ihrer Datenbank hergestellt werden. \nBitte kontrollieren Sie Ihre Daten in den Einstellungen bzw. prüfen Ihre Netzwerkverbindung.", "", 0));
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

    //private readonly ObservableCollection<MyDataClass>? originalData; // Variable zum Speichern des ursprünglichen Datensatzes

    private void filterUsername_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private string currentFilter = string.Empty;

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        PopulateList(firstTime: true);
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
            shadowingView.ItemsSource = new ObservableCollection<MyDataClass>(MyData.Where(item => item.Username.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0));
        }
    }

    private void DataGrid_DoubleTapped(object sender, RoutedEventArgs e)
    {
        // Hier können Sie den Code für den Doppelklick auf die Zeile hinzufügen

        if (shadowingView.SelectedItem is MyDataClass selectedRow)
        {
            try
            {
                Process.Start("mstsc.exe", $"/v:{selectedRow.ServerName} /shadow:{selectedRow.SessionId} /control");
            }
            catch (Exception)
            {
                // Hier erscheint die Fehlermeldung
            }
        }
    }

    private void DataGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var grid = (DataGrid)sender;
        var clickedRow = (MyDataClass)((FrameworkElement)e.OriginalSource).DataContext;

        // Setzen Sie die Auswahl manuell
        grid.SelectedItem = clickedRow;

        // Zeigen Sie das Kontextmenü an
        var menu = new MenuFlyout();
        var abmeldenItem = new MenuFlyoutItem { Text = "Abmelden" };
        abmeldenItem.Click += Abmelden_Click;
        menu.Items.Add(abmeldenItem);

        var SendMessageToUserItem = new MenuFlyoutItem { Text = "Nachricht senden" };
        SendMessageToUserItem.Click += SendMessageToUser_Click;
        menu.Items.Add(SendMessageToUserItem);

        menu.ShowAt(grid, e.GetPosition(grid));
        e.Handled = true;
    }

    private void Abmelden_Click(object sender, RoutedEventArgs e)
    {
        if (shadowingView.SelectedItem is MyDataClass selectedRow)
        {
            try
            {
                // Erstellen Sie eine ProcessStartInfo, um die Eigenschaften des zu startenden Prozesses zu konfigurieren.
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "logoff",
                    Arguments = $"{selectedRow.SessionId} /server:{selectedRow.ServerName}",
                    CreateNoWindow = true,  // Diese Eigenschaft verhindert das Anzeigen des CMD-Fensters.
                    UseShellExecute = false  // Erforderlich, wenn CreateNoWindow auf true gesetzt ist.
                };

                // Verwenden Sie Process.Start mit ProcessStartInfo, um den Prozess auszuführen.
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                // Fehlerbehandlung für Ausnahmen beim Ausführen des logoff-Befehls
                MyData.Add(new MyDataClass("Error", $"Fehler beim Abmelden: {ex.Message}", "", 0));
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
                    CreateNoWindow = true,  // Diese Eigenschaft verhindert das Anzeigen des CMD-Fensters.
                    UseShellExecute = false  // Erforderlich, wenn CreateNoWindow auf true gesetzt ist.
                };
                // Verwenden Sie den Process.Start, um den "msg" Befehl auszuführen
                // Fügen Sie die eingegebene Nachricht aus dem Textfeld hinzu
                Process.Start(psi);

                // Schließen Sie das Flyout nach dem Senden der Nachricht
                messageFlyout.Hide();
            }
            catch (Exception ex)
            {
                // Fehlerbehandlung für Ausnahmen beim Ausführen des msg-Befehls
                MyData.Add(new MyDataClass("Error", $"Fehler beim Senden der Nachricht: {ex.Message}", "", 0));
            }
        }
    }

}
