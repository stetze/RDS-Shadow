using System.Runtime.InteropServices;
using System.Globalization;
using System.Xml.Linq;
using System.Collections.Concurrent;
using Windows.Globalization;
using Windows.ApplicationModel.Resources.Core;

namespace RDS_Shadow.Helpers;

public static class ResourceExtensions
{
    private static readonly Microsoft.Windows.ApplicationModel.Resources.ResourceLoader _resourceLoader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
    private static readonly ConcurrentDictionary<string, string> _manualCache = new();

    public static string GetLocalized(this string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            return string.Empty;
        }

        // Determine language to prefer for manual lookup
        var overrideLang = string.Empty;
        try
        {
            overrideLang = ApplicationLanguages.PrimaryLanguageOverride ?? string.Empty;
        }
        catch { }

        // If an explicit PrimaryLanguageOverride is set, prefer resolved lookup for that language
        if (!string.IsNullOrEmpty(overrideLang))
        {
            // Try ResourceManager with specific ResourceContext first (works for PRI resources)
            try
            {
                var fromRm = TryResourceManagerLookup(overrideLang, resourceKey);
                if (!string.IsNullOrEmpty(fromRm)) return fromRm;
            }
            catch { }

            // Next try manual resw file lookup
            var manual = TryManualLookup(overrideLang, resourceKey);
            if (!string.IsNullOrEmpty(manual)) return manual;

            // fall back to ResourceLoader if manual lookup didn't find a value
            try
            {
                var val = _resourceLoader.GetString(resourceKey);
                if (!string.IsNullOrEmpty(val)) return val;
            }
            catch { }

            // final attempt: try manual lookup using CurrentUICulture
            var lang = CultureInfo.CurrentUICulture.Name;
            var manual2 = TryManualLookup(lang, resourceKey);
            if (!string.IsNullOrEmpty(manual2)) return manual2;

            return resourceKey;
        }

        // Default path: try ResourceLoader first (PRI), then manual fallback
        try
        {
            var value = _resourceLoader.GetString(resourceKey);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }
        catch (COMException)
        {
            // fall through to manual lookup
        }
        catch
        {
            // fall through to manual lookup
        }

        try
        {
            var lang = CultureInfo.CurrentUICulture.Name; // e.g., "de-DE" or "en-US"
            var manual = TryManualLookup(lang, resourceKey);
            if (!string.IsNullOrEmpty(manual)) return manual;

            // also try language prefix (e.g., "en")
            var prefix = lang.Split('-')[0];
            var manualPrefix = TryManualLookup(prefix, resourceKey);
            if (!string.IsNullOrEmpty(manualPrefix)) return manualPrefix;
        }
        catch { }

        // Final fallback: return the key so UI shows a visible identifier rather than throwing
        return resourceKey;
    }

    private static string TryResourceManagerLookup(string lang, string resourceKey)
    {
        if (string.IsNullOrEmpty(lang) || string.IsNullOrEmpty(resourceKey)) return string.Empty;

        try
        {
            // Use view-independent ResourceContext and set language
            var ctx = Windows.ApplicationModel.Resources.Core.ResourceContext.GetForViewIndependentUse();
            ctx.Languages = new[] { lang };

            var rm = Windows.ApplicationModel.Resources.Core.ResourceManager.Current;
            // Resource names are typically under "Resources/" in the resource map
            var mapKey = "Resources/" + resourceKey;
            var val = rm.MainResourceMap.GetValue(mapKey, ctx)?.ValueAsString;
            if (!string.IsNullOrEmpty(val)) return val;

            // Try without the Resources/ prefix as a fallback
            val = rm.MainResourceMap.GetValue(resourceKey, ctx)?.ValueAsString;
            if (!string.IsNullOrEmpty(val)) return val;
        }
        catch
        {
            // ignore
        }

        return string.Empty;
    }

    private static string TryManualLookup(string lang, string resourceKey)
    {
        if (string.IsNullOrEmpty(lang) || string.IsNullOrEmpty(resourceKey)) return string.Empty;

        var cacheKey = $"{lang}|{resourceKey}";
        if (_manualCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var baseDir = AppContext.BaseDirectory ?? string.Empty;

        // Build list of candidate language folders to search for any .resw files
        var folderCandidates = new List<string>
        {
            Path.Combine(baseDir, "Strings", lang),
            Path.Combine(baseDir, "Strings", lang.ToLowerInvariant()),
            Path.Combine(baseDir, "Strings", lang.Replace('-', '_')),
            Path.Combine(baseDir, "Strings", lang.Split('-')[0]),
            Path.Combine(baseDir, "Strings", lang.Split('-')[0].ToLowerInvariant()),
        };

        foreach (var folder in folderCandidates.Distinct())
        {
            if (!Directory.Exists(folder)) continue;

            try
            {
                foreach (var path in Directory.GetFiles(folder, "*.resw"))
                {
                    try
                    {
                        var doc = XDocument.Load(path);
                        var data = doc.Root?.Elements()
                            .Where(x => x.Name.LocalName == "data")
                            .FirstOrDefault(x => string.Equals(x.Attribute("name")?.Value, resourceKey, StringComparison.Ordinal));

                        var val = data?.Elements().FirstOrDefault(x => x.Name.LocalName == "value")?.Value;
                        if (!string.IsNullOrEmpty(val))
                        {
                            _manualCache[cacheKey] = val;
                            return val;
                        }
                    }
                    catch
                    {
                        // ignore individual file parse errors and continue
                    }
                }
            }
            catch
            {
                // ignore folder enumeration errors
            }
        }

        return string.Empty;
    }
}
