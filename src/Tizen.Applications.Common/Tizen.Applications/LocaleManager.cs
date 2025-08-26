using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Tizen.Applications
{
    /// <summary>
    /// Provides methods to manage system locale settings.
    /// </summary>
    public static class LocaleManager
    {
        private const string LogTag = "Tizen.Applications";
        private const string SupportedLocalesFilePath = "/usr/share/i18n/SUPPORTED";

        /// <summary>
        /// Attempts to set the Application's locale based on the provided CultureInfo and Encoding.
        /// </summary>
        /// <param name="cultureInfo">The CultureInfo object representing the desired locale.</param>
        /// <param name="encoding">The preferred encoding. If null, UTF-8 is used as default.</param>
        /// <remarks>
        /// This method checks the /usr/share/i18n/SUPPORTED file for the locale and its encoding.
        /// If the locale (e.g., "en-US" converted to "en_US") is found, it uses the full locale string
        /// from the file (e.g., "en_US.UTF-8") to update the system language.
        /// If not found, it logs an error.
        /// </remarks>
        public static void UpdateApplicationLanguage(CultureInfo cultureInfo, Encoding encoding = null)
        {
            if (!File.Exists(SupportedLocalesFilePath))
            {
                Log.Error(LogTag, $"{SupportedLocalesFilePath} not found.");
                return;
            }

            if (cultureInfo == null)
            {
                Log.Error(LogTag, "CultureInfo cannot be null.");
                return;
            }

            string cultureInfoName = cultureInfo.Name;

            if (string.IsNullOrEmpty(cultureInfoName))
            {
                Log.Error(LogTag, "CultureInfo.Name is null or empty.");
                return;
            }

            var supportedLocales = File.ReadAllLines(SupportedLocalesFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(localeFileLineParts => localeFileLineParts.Length == 2)
                .Select(localeFileLineParts => FormatLocaleString(localeFileLineParts))
                .ToHashSet();

            string posixLocaleName = cultureInfoName.Replace('-', '_');
            Encoding effectiveEncoding = encoding ?? Encoding.UTF8;
            string targetLocaleString = FormatLocaleString(new string[] { posixLocaleName, effectiveEncoding.BodyName });

            if (supportedLocales.Contains(targetLocaleString))
            {
                Interop.AppCore.UpdateLanguage(targetLocaleString);
            }
            else
            {
                Log.Error(LogTag, $"Unsupported Language : {targetLocaleString}");
            }
        }

        /// <summary>
        /// Formats an array containing a locale name and an encoding into a single locale string.
        /// The first element of the array can be a locale name (e.g., "en_US") or a locale string with an existing encoding (e.g., "en_US.LATIN-1").
        /// The second element is the desired encoding name (e.g., "UTF-8").
        /// The method ensures the output is in the format {language}_{country}.{encoding}, using the encoding from the second element.
        /// </summary>
        /// <param name="localeParts">An array where the first element is the locale name (potentially with an old encoding) and the second is the target encoding name.</param>
        /// <returns>A consistently formatted locale string (e.g., "en_US.UTF-8").</returns>
        private static string FormatLocaleString(string[] localeParts)
        {
            string baseLocale = localeParts[0];
            string targetEncoding = localeParts[1];

            int dotIndex = baseLocale.IndexOf('.');
            if (dotIndex != -1)
            {
                baseLocale = baseLocale.Substring(0, dotIndex);
            }

            return $"{baseLocale}.{targetEncoding}";
        }
    }
}
