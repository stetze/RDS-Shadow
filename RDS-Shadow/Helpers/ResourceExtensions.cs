using Microsoft.Windows.ApplicationModel.Resources;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Xml.Linq;
using System.Collections.Concurrent;

namespace RDS_Shadow.Helpers;

public static class ResourceExtensions
{
    private static readonly ResourceLoader _resourceLoader = new ResourceLoader();
    private static readonly ConcurrentDictionary<string, string> _manualCache = new();

    public static string GetLocalized(this string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            return string.Empty;
        }

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

        // Try manual lookup from Strings folder (.resw files) as a robust fallback
        try
        {
            var lang = CultureInfo.CurrentUICulture.Name; // e.g., "de-DE" or "en-US"

            // Cache key uses language and resource key so different languages cache separately
            var cacheKey = $"{lang}|{resourceKey}";
            if (_manualCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var baseDir = AppContext.BaseDirectory ?? string.Empty;
            var candidates = new List<string>
            {
                Path.Combine(baseDir, "Strings", lang, "Resources.resw"),
                Path.Combine(baseDir, "Strings", lang.ToLowerInvariant(), "Resources.resw"),
                Path.Combine(baseDir, "Strings", lang.Split('-')[0], "Resources.resw"),
                Path.Combine(baseDir, "Strings", lang.Split('-')[0].ToLowerInvariant(), "Resources.resw"),
            };

            foreach (var path in candidates.Distinct())
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var doc = XDocument.Load(path);
                        var data = doc.Root?.Elements()
                            .Where(x => x.Name.LocalName == "data")
                            .FirstOrDefault(x => (string)x.Attribute("name") == resourceKey);

                        var val = data?.Elements().FirstOrDefault(x => x.Name.LocalName == "value")?.Value;
                        if (!string.IsNullOrEmpty(val))
                        {
                            _manualCache[cacheKey] = val;
                            return val;
                        }
                    }
                    catch
                    {
                        // ignore and continue
                    }
                }
            }
        }
        catch
        {
            // ignore manual lookup errors
        }

        // Final fallback: return the key so UI shows a visible identifier rather than throwing
        return resourceKey;
    }
}
