using System;
using System.Globalization;
using System.Threading;
using Windows.ApplicationModel.Resources.Core;
using Windows.Globalization;
using System.Diagnostics;

namespace RDS_Shadow.Helpers
{
    public interface ILocalizationService
    {
        event EventHandler? LanguageChanged;
        string CurrentLanguage { get; }
        void ApplyLanguage(string languageTag);
    }

    public class LocalizationService : ILocalizationService
    {
        public event EventHandler? LanguageChanged;

        public string CurrentLanguage => ApplicationLanguages.PrimaryLanguageOverride ?? CultureInfo.CurrentUICulture.Name;

        public void ApplyLanguage(string languageTag)
        {
            Debug.WriteLine($"LocalizationService.ApplyLanguage called with: '{languageTag}'");

            if (string.IsNullOrWhiteSpace(languageTag))
            {
                // reset to system default
                ApplicationLanguages.PrimaryLanguageOverride = string.Empty;
                var installed = CultureInfo.InstalledUICulture;
                CultureInfo.DefaultThreadCurrentUICulture = installed;
                CultureInfo.DefaultThreadCurrentCulture = installed;

                Thread.CurrentThread.CurrentCulture = installed;
                Thread.CurrentThread.CurrentUICulture = installed;

                Debug.WriteLine($"LocalizationService: reset to installed culture {installed.Name}");
            }
            else
            {
                ApplicationLanguages.PrimaryLanguageOverride = languageTag;
                var ci = new CultureInfo(languageTag);
                CultureInfo.DefaultThreadCurrentUICulture = ci;
                CultureInfo.DefaultThreadCurrentCulture = ci;

                Thread.CurrentThread.CurrentCulture = ci;
                Thread.CurrentThread.CurrentUICulture = ci;

                Debug.WriteLine($"LocalizationService: PrimaryLanguageOverride set to {languageTag}");
            }

            // Notify listeners
            Debug.WriteLine("LocalizationService: raising LanguageChanged event");
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
