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
using RDS_Shadow.Contracts.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Linq;
using System.Globalization;
using Windows.Globalization;

namespace RDS_Shadow.Views;

public sealed partial class SessionsPage : Page
{
    public SessionsViewModel ViewModel { get; }
    private readonly ILocalizationService _localizationService;
    private FrameworkElement? _lastRightClickTarget;

    // Class-level helper to provide localization with fallback
    private static string LocalizedOrDefault(string key, string deDefault, string enDefault)
    {
        var val = key.GetLocalized();
        if (val == key)
        {
            // Prefer ApplicationLanguages.PrimaryLanguageOverride when set
            try
            {
                var overrideLang = ApplicationLanguages.PrimaryLanguageOverride;
                if (!string.IsNullOrEmpty(overrideLang))
                {
                    return overrideLang.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? deDefault : enDefault;
                }
            }
            catch
            {
                // ignore
            }

            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? deDefault : enDefault;
        }
        return val;
    }

    public SessionsPage()
    {
        ViewModel = App.GetService<SessionsViewModel>();
        _localizationService = App.GetService<ILocalizationService>();

        InitializeComponent();

        // Wire localization change
        _localization_service_subscribe();

        // Set UI texts (use localization with fallback)
        colUsername.Header = LocalizedOrDefault("Sessions_Column_Username", "Benutzer", "Username");
        colPoolName.Header = LocalizedOrDefault("Sessions_Column_PoolName", "Pool", "Pool");
        colServerName.Header = LocalizedOrDefault("Sessions_Column_ServerName", "Server", "Server");
        colClientName.Header = LocalizedOrDefault("Sessions_Column_ClientName", "Client", "Client");
        colSessionId.Header = LocalizedOrDefault("Sessions_Column_SessionId", "Sitzungs-ID", "SessionId");

        ToolTipService.SetToolTip(refresh, new ToolTip { Content = "Sessions_RefreshButton.ToolTipService.ToolTip".GetLocalized() });
        try { tbSearch.PlaceholderText = "Sessions_FilterTextBox.PlaceholderText".GetLocalized(); } catch { }
        ToolTipService.SetToolTip(sendMessageToAllUser, new ToolTip { Content = "Sessions_SendMessageAllButton.ToolTipService.ToolTip".GetLocalized() });
        if (sendAllText != null) sendAllText.Text = "Sessions_SendMessageAllButton_Text".GetLocalized();

        // Ensure the list is populated automatically when the page is first shown
        Loaded += SessionsPage_Loaded;
    }

    private void _localization_service_subscribe()
    {
        _localizationService.LanguageChanged += LocalizationService_LanguageChanged;
    }

    private void LocalizationService_LanguageChanged(object? sender, System.EventArgs e)
    {
        Debug.WriteLine("SessionsPage: LanguageChanged received, re-localizing UI");

        // Re-apply localized strings when language changes
        void UpdateTexts()
        {
            // Update programmatically set values
            colUsername.Header = "Sessions_Column_Username".GetLocalized();
            colPoolName.Header = "Sessions_Column_PoolName".GetLocalized();
            colServerName.Header = "Sessions_Column_ServerName".GetLocalized();
            colClientName.Header = "Sessions_Column_ClientName".GetLocalized();
            colSessionId.Header = "Sessions_Column_SessionId".GetLocalized();

            if (sendAllText != null) sendAllText.Text = "Sessions_SendMessageAllButton_Text".GetLocalized();
            ToolTipService.SetToolTip(sendMessageToAllUser, new ToolTip { Content = "Sessions_SendMessageAllButton.ToolTipService.ToolTip".GetLocalized() });

            if (menuLogoutItem != null) menuLogoutItem.Text = "Sessions_Context_Logout".GetLocalized();
            if (menuSendMessageItem != null) menuSendMessageItem.Text = "Sessions_Context_SendMessage".GetLocalized();

            if (messageTextBox != null) messageTextBox.PlaceholderText = "Sessions_MessageTextBox.PlaceholderText".GetLocalized();
            if (SendButton != null) SendButton.Content = "Sessions_Message_Send_Button.Content".GetLocalized();

            try { tbSearch.PlaceholderText = "Sessions_FilterTextBox.PlaceholderText".GetLocalized(); } catch { }
        }

        // Ensure update occurs on UI thread
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => UpdateTexts());
    }

    private ElementTheme GetCurrentAppTheme()
    {
        try
        {
            if (App.MainWindow?.Content is FrameworkElement root)
            {
                return root.ActualTheme;
            }
        }
        catch { }

        // fallback to theme service
        try
        {
            var svc = App.GetService<IThemeSelectorService>();
            return svc?.Theme ?? ElementTheme.Default;
        }
        catch { }

        return ElementTheme.Default;
    }

    private async void SessionsPage_Loaded(object? sender, RoutedEventArgs e)
    {
        // Populate on first load and apply current filter
        await PopulateList(firstTime: true);
        ApplyFilter();

        // Unregister handler to avoid repeated loading
        Loaded -= SessionsPage_Loaded;
    }

    public class MyDataClass : INotifyPropertyChanged
    {
        private string _username = string.Empty;
        private string _poolName = string.Empty;
        private string _serverName = string.Empty;
        private int _sessionId;
        private string _clientName = string.Empty;

        public string Username { get => _username; set { _username = value; OnPropertyChanged(); } }
        public string PoolName { get => _poolName; set { _poolName = value; OnPropertyChanged(); } }
        public string ServerName { get => _serverName; set { _serverName = value; OnPropertyChanged(); } }
        public int SessionId { get => _sessionId; set { _sessionId = value; OnPropertyChanged(); } }

        // New: client name retrieved via WTS from the terminal server
        public string ClientName { get => _clientName; set { _clientName = value; OnPropertyChanged(); } }

        public MyDataClass(string userName, string poolName, string serverName, int sessionId)
        {
            Username = userName ?? string.Empty;
            PoolName = poolName ?? string.Empty;
            ServerName = serverName ?? string.Empty;
            SessionId = sessionId;
            ClientName = string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private readonly ObservableCollection<MyDataClass> MyData = new ObservableCollection<MyDataClass>();

    // Semaphore to serialize ContentDialog.ShowAsync calls to avoid the "Only a single ContentDialog can be open at any time" COMException
    private readonly SemaphoreSlim _dialogSemaphore = new SemaphoreSlim(1, 1);

    private async Task<ContentDialogResult> ShowContentDialogSerializedAsync(ContentDialog dialog)
    {
        await _dialogSemaphore.WaitAsync();
        try
        {
            return await dialog.ShowAsync();
        }
        catch (COMException)
        {
            // If another dialog is shown concurrently, swallow or log as needed.
            return ContentDialogResult.None;
        }
        finally
        {
            _dialogSemaphore.Release();
        }
    }

    // --- WTS P/Invoke declarations for client lookup ---
    private enum WTS_INFO_CLASS
    {
        WTSClientName = 10,
        WTSClientAddress = 14
    }

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr WTSOpenServer(string pServerName);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSCloseServer(IntPtr hServer);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, WTS_INFO_CLASS wtsInfoClass, out IntPtr ppBuffer, out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    // Decode pointer buffer robustly: try UTF-16 (Unicode), then ANSI (1252), then UTF-8
    private static string PtrToStringSmart(IntPtr pBuffer, int bytes)
    {
        if (pBuffer == IntPtr.Zero || bytes <= 0)
            return string.Empty;

        // Try Unicode (UTF-16LE)
        try
        {
            var s = Marshal.PtrToStringUni(pBuffer);
            if (!string.IsNullOrWhiteSpace(s))
            {
                // check for likely valid characters
                var total = s.Length;
                var good = s.Count(c => (c >= 0x20 && c <= 0x7E) || char.IsLetterOrDigit(c) || char.IsWhiteSpace(c));
                if (total > 0 && ((double)good / total) > 0.2)
                {
                    return s.Trim('\0', ' ');
                }
            }
        }
        catch { }

        // Fallback: copy raw bytes and try ANSI (code page 1252)
        try
        {
            var buffer = new byte[bytes];
            Marshal.Copy(pBuffer, buffer, 0, bytes);

            // Trim trailing zero bytes
            var actualLength = buffer.Length;
            while (actualLength > 0 && buffer[actualLength - 1] == 0) actualLength--;
            if (actualLength <= 0) return string.Empty;

            var ansi = Encoding.GetEncoding(1252).GetString(buffer, 0, actualLength);
            if (!string.IsNullOrWhiteSpace(ansi))
            {
                var total = ansi.Length;
                var good = ansi.Count(c => (c >= 0x20 && c <= 0x7E) || char.IsLetterOrDigit(c) || char.IsWhiteSpace(c));
                if (total > 0 && ((double)good / total) > 0.2)
                {
                    return ansi.Trim('\0', ' ');
                }
            }

            // Last try UTF8
            try
            {
                var utf8 = Encoding.UTF8.GetString(buffer, 0, actualLength);
                if (!string.IsNullOrWhiteSpace(utf8)) return utf8.Trim('\0', ' ');
            }
            catch { }
        }
        catch { }

        return string.Empty;
    }

    private static string GetClientNameForSession(string server, int sessionId)
    {
        if (string.IsNullOrWhiteSpace(server)) return string.Empty;
        IntPtr hServer = IntPtr.Zero;
        try
        {
            hServer = WTSOpenServer(server);
            if (hServer == IntPtr.Zero) return string.Empty;

            if (WTSQuerySessionInformation(hServer, sessionId, WTS_INFO_CLASS.WTSClientName, out var pBuffer, out var bytes) && pBuffer != IntPtr.Zero)
            {
                try
                {
                    var clientName = PtrToStringSmart(pBuffer, bytes);
                    return clientName;
                }
                finally
                {
                    WTSFreeMemory(pBuffer);
                }
            }
        }
        catch
        {
            // ignore errors and return empty
        }
        finally
        {
            if (hServer != IntPtr.Zero)
            {
                WTSCloseServer(hServer);
            }
        }

        return string.Empty;
    }

    private static async Task<string> GetClientNameForSessionAsync(string server, int sessionId, int timeoutMs = 2000)
    {
        var t = Task.Run(() => GetClientNameForSession(server, sessionId));
        if (await Task.WhenAny(t, Task.Delay(timeoutMs)) == t)
        {
            return t.Result;
        }
        return string.Empty;
    }

    private async Task PopulateList(bool firstTime)
    {
        const string IncludeClientNameKey = "IncludeClientNameSetting";

        // Read user preference: include client name lookups
        var includeClientObj = Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue(IncludeClientNameKey, out var includeObj) ? includeObj as string : null;
        var includeClient = false;
        if (!string.IsNullOrEmpty(includeClientObj) && bool.TryParse(includeClientObj, out var parsed)) includeClient = parsed;

        // Show/hide client column based on setting (UI thread)
        try
        {
            if (colClientName != null)
            {
                colClientName.Visibility = includeClient ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        catch { }

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

                    // Optionally enrich with client names via WTS lookups if enabled
                    if (includeClient)
                    {
                        var lookupSemaphore = new SemaphoreSlim(8); // limit concurrency
                        var lookupTasks = MyData.Select(async item =>
                        {
                            await lookupSemaphore.WaitAsync();
                            try
                            {
                                var client = await GetClientNameForSessionAsync(item.ServerName, item.SessionId, timeoutMs: 2000);
                                if (!string.IsNullOrEmpty(client))
                                {
                                    item.ClientName = client;
                                }
                            }
                            finally
                            {
                                lookupSemaphore.Release();
                            }
                        }).ToArray();

                        await Task.WhenAll(lookupTasks);
                    }
                }
            }
            catch (SqlException ex)
            {
                // Show a friendly dialog to the user with localized text
                var title = "Sessions_Error_DbConnection".GetLocalized();
                var content = "Sessions_Error_DbConnection".GetLocalized();

                content = content + "\n\n" + ex.Message;

                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = content,
                    CloseButtonText = "Common_Ok".GetLocalized(),
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = GetCurrentAppTheme()
                };

                await ShowContentDialogSerializedAsync(dialog);
            }
            catch (Exception ex)
            {
                // Unexpected error: show generic dialog
                var dialog = new ContentDialog
                {
                    Title = "Common_ErrorTitle".GetLocalized(),
                    Content = ex.Message,
                    CloseButtonText = "Common_Ok".GetLocalized(),
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = GetCurrentAppTheme()
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

            var itemsToSort = string.IsNullOrEmpty(tbSearch?.Text)
                ? MyData
                : new ObservableCollection<MyDataClass>(MyData.Where(item => item.Username.IndexOf(tbSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0));

            var sortedItems = propertyName switch
            {
                "Username" => itemsToSort.OrderBy(item => item.Username),
                "PoolName" => itemsToSort.OrderBy(item => item.PoolName),
                "ServerName" => itemsToSort.OrderBy(item => item.ServerName),
                "SessionId" => itemsToSort.OrderBy(item => item.SessionId),
                "ClientName" => itemsToSort.OrderBy(item => item.ClientName), // Sort by client name if applicable
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

    private void tbSearch_TextChanged(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, Microsoft.UI.Xaml.Controls.AutoSuggestBoxTextChangedEventArgs e)
    {
        try { ApplyFilter(); } catch { }
    }

    private void tbSearch_QuerySubmitted(Microsoft.UI.Xaml.Controls.AutoSuggestBox sender, Microsoft.UI.Xaml.Controls.AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        try { ApplyFilter(); } catch { }
    }

    private string currentFilter = string.Empty;

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await PopulateList(firstTime: true);
        ApplyFilter(); // Apply the current filter after refreshing the list
    }

    private void ApplyFilter()
    {
        currentFilter = tbSearch?.Text ?? string.Empty;

        if (string.IsNullOrEmpty(currentFilter))
        {
            shadowingView.ItemsSource = MyData;
        }
        else
        {
            // Filter anwenden: Suche nach Username oder ServerName
            var filteredData = MyData.Where(item =>
                item.Username.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.ServerName.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.ClientName.IndexOf(currentFilter, StringComparison.OrdinalIgnoreCase) >= 0);

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
            _lastRightClickTarget = grid;

            // Zeigen Sie das Kontextmenü an
            var menu = new MenuFlyout();
            var abmeldenItem = new MenuFlyoutItem { Text = "Sessions_Context_Logout".GetLocalized() };
            abmeldenItem.Click += Abmelden_Click;
            menu.Items.Add(abmeldenItem);

            var sendMessageItem = new MenuFlyoutItem { Text = "Sessions_Context_SendMessage".GetLocalized() };
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
                MyData.Add(new MyDataClass("Common_ErrorTitle".GetLocalized(), string.Format("Sessions_LogoutFailed".GetLocalized(), ex.Message), "", 0));
            }
        }
    }

    private async void SendMessageToUser_Click(object sender, RoutedEventArgs e)
    {
        if (shadowingView.SelectedItem is MyDataClass selectedRow)
        {
            // Always open a ContentDialog for per-user messages so it matches the "send to all" dialog
            await ShowSendUserDialogAsync(selectedRow);
        }
    }

    private async Task ShowSendUserDialogAsync(MyDataClass user)
    {
        // Use same layout as SendAll dialog: larger textbox
        var textBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 220,
            Width = 520,
            PlaceholderText = "Sessions_MessageAllTextBox.PlaceholderText".GetLocalized()
        };

        var titleTemplate = LocalizedOrDefault("Sessions_MessageToUser_Title", "Nachricht an {0} ({1})", "Message to {0} ({1})");
        var dialog = new ContentDialog
        {
            Title = string.Format(titleTemplate, user.Username, user.ServerName),
            Content = textBox,
            PrimaryButtonText = "Sessions_MessageAll_SendButton.Content".GetLocalized(),
            CloseButtonText = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Abbrechen" : "Cancel",
            XamlRoot = this.XamlRoot,
            RequestedTheme = GetCurrentAppTheme()
        };

        var result = await ShowContentDialogSerializedAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            var message = textBox.Text;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "msg",
                    Arguments = $"{user.SessionId} /server:{user.ServerName} {message}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                var err = "Sessions_Error_SendMessageUser".GetLocalized();
                if (err == "Sessions_Error_SendMessageUser") err = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Nachricht an {0} fehlgeschlagen: {1}" : "Error sending message to {0}: {1}";
                MyData.Add(new MyDataClass("Common_ErrorTitle".GetLocalized(), string.Format(err, user.Username, ex.Message), "", 0));
            }
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
                messageFlyout?.Hide();
            }
            catch (Exception ex)
            {
                var err = "Sessions_Error_SendMessage".GetLocalized();
                if (err == "Sessions_Error_SendMessage") err = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Fehler beim Senden der Nachricht: {0}" : "Error sending message: {0}";
                MyData.Add(new MyDataClass("Common_ErrorTitle".GetLocalized(), string.Format(err, ex.Message), "", 0));
            }
        }
    }

    private async void SendAllButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowSendAllDialogAsync();
    }

    private async Task ShowSendAllDialogAsync()
    {
        var textBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 220,
            Width = 520,
            PlaceholderText = "Sessions_MessageAllTextBox.PlaceholderText".GetLocalized()
        };

        var dialog = new ContentDialog
        {
            Title = "Sessions_MessageAllTitle.Text".GetLocalized(),
            Content = textBox,
            PrimaryButtonText = "Sessions_MessageAll_SendButton.Content".GetLocalized(),
            CloseButtonText = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Abbrechen" : "Cancel",
            XamlRoot = this.XamlRoot,
            RequestedTheme = GetCurrentAppTheme()
        };

        var result = await ShowContentDialogSerializedAsync(dialog);
        if (result == ContentDialogResult.Primary)
        {
            var messageToAllUsers = textBox.Text;

            // Verwenden Sie die aktuell angezeigte (gefilterte) Liste
            var filteredData = shadowingView.ItemsSource as ObservableCollection<MyDataClass>;

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
                        var err = "Sessions_Error_SendAll".GetLocalized();
                        if (err == "Sessions_Error_SendAll") err = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Fehler beim Senden an alle Nutzer: {0}" : "Error sending message to all users: {0}";
                        MyData.Add(new MyDataClass("Common_ErrorTitle".GetLocalized(), string.Format(err, ex.Message), "", 0));
                    }
                }
            }
            else
            {
                MyData.Add(new MyDataClass("Common_WarningTitle".GetLocalized(), "Sessions_NoUsers".GetLocalized(), "", 0));
            }
        }
    }
}
