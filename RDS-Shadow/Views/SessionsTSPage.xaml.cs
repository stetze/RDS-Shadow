using System.Collections.ObjectModel;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using RDS_Shadow.ViewModels;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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

public sealed partial class SessionsTSPage : Page
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

    public SessionsTSPage()
    {
        ViewModel = App.GetService<SessionsViewModel>();
        _localizationService = App.GetService<ILocalizationService>();

        InitializeComponent();

        // Wire localization change
        _localization_service_subscribe();

        // Set UI texts (use localization with fallback)
        colUsername.Header = LocalizedOrDefault("Sessions_Column_Username", "Benutzer", "Username");
        colServerName.Header = LocalizedOrDefault("Sessions_Column_Server", "Server", "Server");
        colServerName.Header = LocalizedOrDefault("Sessions_Column_ServerName", "Server", "Server");
        colClientName.Header = LocalizedOrDefault("Sessions_Column_ClientName", "Client", "Client");
        colSessionId.Header = LocalizedOrDefault("Sessions_Column_SessionId", "Sitzungs-ID", "SessionId");

        ToolTipService.SetToolTip(refresh, new ToolTip { Content = "Sessions_RefreshButton.ToolTipService.ToolTip".GetLocalized() });
        filterUsername.PlaceholderText = "Sessions_FilterTextBox.PlaceholderText".GetLocalized();
        ToolTipService.SetToolTip(sendMessageToAllUser, new ToolTip { Content = "Sessions_SendMessageAllButton.ToolTipService.ToolTip".GetLocalized() });

        if (refreshToolTip != null) refreshToolTip.Content = "Sessions_RefreshButton.ToolTipService.ToolTip".GetLocalized();
        if (sendAllText != null) sendAllText.Text = "Sessions_SendMessageAllButton_Text".GetLocalized();
        if (sendAllToolTip != null) sendAllToolTip.Content = "Sessions_SendMessageAllButton.ToolTipService.ToolTip".GetLocalized();

        if (menuLogoutItem != null) menuLogoutItem.Text = "Sessions_Context_Logout".GetLocalized();
        if (menuSendMessageItem != null) menuSendMessageItem.Text = "Sessions_Context_SendMessage".GetLocalized();

        if (messageTextBox != null) messageTextBox.PlaceholderText = "Sessions_MessageTextBox.PlaceholderText".GetLocalized();
        if (SendButton != null) SendButton.Content = "Sessions_Message_Send_Button.Content".GetLocalized();

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
            colServerName.Header = "Sessions_Column_ServerName".GetLocalized();
            colClientName.Header = "Sessions_Column_ClientName".GetLocalized();
            colSessionId.Header = "Sessions_Column_SessionId".GetLocalized();

            if (refreshToolTip != null) refreshToolTip.Content = "Sessions_RefreshButton.ToolTipService.ToolTip".GetLocalized();
            if (sendAllText != null) sendAllText.Text = "Sessions_SendMessageAllButton_Text".GetLocalized();
            if (sendAllToolTip != null) sendAllToolTip.Content = "Sessions_SendMessageAllButton.ToolTipService.ToolTip".GetLocalized();

            if (menuLogoutItem != null) menuLogoutItem.Text = "Sessions_Context_Logout".GetLocalized();
            if (menuSendMessageItem != null) menuSendMessageItem.Text = "Sessions_Context_SendMessage".GetLocalized();

            if (messageTextBox != null) messageTextBox.PlaceholderText = "Sessions_MessageTextBox.PlaceholderText".GetLocalized();
            if (SendButton != null) SendButton.Content = "Sessions_Message_Send_Button.Content".GetLocalized();

            filterUsername.PlaceholderText = "Sessions_FilterTextBox.PlaceholderText".GetLocalized();
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
        private string _serverName = string.Empty;
        private int _sessionId;
        private string _clientName = string.Empty;

        public string Username { get => _username; set { _username = value; OnPropertyChanged(); } }
        public string ServerName { get => _serverName; set { _serverName = value; OnPropertyChanged(); } }
        public int SessionId { get => _sessionId; set { _sessionId = value; OnPropertyChanged(); } }
        public string ClientName { get => _clientName; set { _clientName = value; OnPropertyChanged(); } }

        public MyDataClass(string userName, string serverName, int sessionId)
        {
            Username = userName ?? string.Empty;
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
        // Read user preference: include client name lookups
        const string IncludeClientNameKey = "IncludeClientNameSetting";
        var includeClientObj = Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue(IncludeClientNameKey, out var includeObj) ? includeObj as string : null;
        var includeClient = false;
        if (!string.IsNullOrEmpty(includeClientObj) && bool.TryParse(includeClientObj, out var parsed)) includeClient = parsed;

        // read additional column visibility settings
        

         // Show/hide client column based on setting (UI thread)
         try
         {
             if (colClientName != null)
             {
                 colClientName.Visibility = includeClient ? Visibility.Visible : Visibility.Collapsed;
             }
         }
         catch { }

        // Read TS hostnames from Settings_TSname
        var tsHosts = Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("Settings_TSname", out var hostsObj) ? hostsObj as string : null;

        if (string.IsNullOrWhiteSpace(tsHosts))
        {
            // nothing configured
            MyData.Clear();
            return;
        }

        // Split by comma, semicolon or newline; trim entries; remove empties and duplicates
        var hostList = System.Text.RegularExpressions.Regex.Split(tsHosts, "[,;\r\n]+")
            .Select(h => h.Trim())
            .Where(h => !string.IsNullOrEmpty(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (hostList.Count == 0)
        {
            MyData.Clear();
            return;
        }

        MyData.Clear();

        // Run quser for each host in parallel but limit concurrency
        var sem = new SemaphoreSlim(4);
        var tasks = hostList.Select(async host =>
        {
            await sem.WaitAsync();
            try
            {
                var entries = await RunQuserForServerAsync(host);
                return entries;
            }
            finally
            {
                sem.Release();
            }
        }).ToArray();

        // Await and handle per-host failures so we can show a dialog for each failing host
        for (int i = 0; i < tasks.Length; i++)
        {
            var host = hostList[i];
            try
            {
                var entries = await tasks[i];
                foreach (var item in entries)
                {
                    MyData.Add(item);
                }
            }
            catch (Exception ex)
            {
                _ = ex;
                // Show a dialog similar to DB error dialog
                var title = "Sessions_Error_Quser_Title".GetLocalized();
                if (title == "Sessions_Error_Quser_Title") title = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Fehler beim Abfragen von Sitzungen" : "Error querying sessions";

                var contentKey = "Sessions_Error_Quser_Content".GetLocalized();
                var content = contentKey == "Sessions_Error_Quser_Content" ? ex.Message : contentKey + "\n\n" + ex.Message;

                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = $"{host}: {ex.Message}",
                    CloseButtonText = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "OK" : "OK",
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = GetCurrentAppTheme()
                };

                try
                {
                    await ShowContentDialogSerializedAsync(dialog);
                }
                catch
                {
                    // ignore dialog failures
                }
            }
        }

        if (includeClient)
        {
            var lookupSemaphore = new SemaphoreSlim(8);
            var lookupTasks = MyData.Select(async item =>
            {
                await lookupSemaphore.WaitAsync();
                try
                {
                    var client = await GetClientNameForSessionAsync(item.ServerName, item.SessionId, timeoutMs: 2000);
                    if (!string.IsNullOrEmpty(client)) item.ClientName = client;
                }
                finally { lookupSemaphore.Release(); }
            }).ToArray();

            await Task.WhenAll(lookupTasks);
        }
    }

    // --- helper methods to parse quser output ---
    // Run 'quser' command for a server and parse output lines
    private static IEnumerable<MyDataClass> ParseQuserOutput(string server, string output)
    {
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            // quser output typically: USERNAME              SESSIONNAME        ID  STATE   IDLE TIME  LOGON TIME
            // We'll split by whitespace but allow usernames with spaces if needed; use regex would be better but keep simple
            var parts = System.Text.RegularExpressions.Regex.Split(line.Trim(), @"\s{2,}");
            if (parts.Length >= 3)
            {
                var username = parts[0];
                var sessionIdStr = parts[2];
                if (int.TryParse(sessionIdStr, out var sessionId))
                {
                    yield return new MyDataClass(username, server, sessionId);
                }
            }
        }
    }

    private async Task<IEnumerable<MyDataClass>> RunQuserForServerAsync(string server)
    {
        if (string.IsNullOrWhiteSpace(server)) return Enumerable.Empty<MyDataClass>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "quser",
                Arguments = $"/server:{server}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return Enumerable.Empty<MyDataClass>();

            var output = await proc.StandardOutput.ReadToEndAsync();
            var error = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            // If quser produced error output or non-zero exit code, treat as failure
            if (!string.IsNullOrEmpty(error) || proc.ExitCode != 0)
            {
                var combined = (output + "\n" + error).ToLowerInvariant();
                // Common messages when no users are present (English/German)
                var noUserIndicators = new[] { "no user", "no users", "no user exists", "no user exists for", "es sind keine benutzer", "keine benutzer", "kein benutzer vorhanden", "kein benutzer vorhanden für" };
                if (noUserIndicators.Any(ind => combined.Contains(ind)))
                {
                    // treat as empty result, not an error
                    return Enumerable.Empty<MyDataClass>();
                }

                var errMsg = string.IsNullOrEmpty(error) ? $"quser exited with code {proc.ExitCode}" : error;
                throw new InvalidOperationException($"Failed to run 'quser' on '{server}': {errMsg}");
            }

            return ParseQuserOutput(server, output).ToList();
        }
        catch (Exception ex)
        {
            // Bubble up the exception so caller can show a dialog referencing the host
            throw new InvalidOperationException($"Failed to run 'quser' on '{server}': {ex.Message}", ex);
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
                "ServerName" => itemsToSort.OrderBy(item => item.ServerName),
                "SessionId" => itemsToSort.OrderBy(item => item.SessionId),
                "ClientName" => itemsToSort.OrderBy(item => item.ClientName),
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
                _ = ex;
                MyData.Add(new MyDataClass("Fehler", string.Empty, 0));
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
                _ = ex;
                var err = "Sessions_Error_SendMessageUser".GetLocalized();
                if (err == "Sessions_Error_SendMessageUser") err = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Nachricht an {0} fehlgeschlagen: {1}" : "Error sending message to {0}: {1}";
                MyData.Add(new MyDataClass("Fehler", string.Empty, 0));
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
                _ = ex;
                var err = "Sessions_Error_SendMessage".GetLocalized();
                if (err == "Sessions_Error_SendMessage") err = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Fehler beim Senden der Nachricht: {0}" : "Error sending message: {0}";
                MyData.Add(new MyDataClass("Fehler", string.Empty, 0));
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
                        _ = ex;
                        var err = "Sessions_Error_SendAll".GetLocalized();
                        if (err == "Sessions_Error_SendAll") err = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Fehler beim Senden an alle Nutzer: {0}" : "Error sending message to all users: {0}";
                        MyData.Add(new MyDataClass("Fehler", string.Empty, 0));
                    }
                }
            }
            else
            {
                MyData.Add(new MyDataClass("Warnung", string.Empty, 0));
            }
        }
    }
}
