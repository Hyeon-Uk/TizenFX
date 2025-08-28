/*
 * Copyright (c) 2025 Samsung Electronics Co., Ltd All Rights Reserved
 *
 * Licensed under the Apache License, Version 2.0 (the License);
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an AS IS BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Tizen.Applications
{
    static class LocaleManager
    {
        private static readonly string LogTag = "Tizen.Applications";
        private static readonly string SupportedLocalesFilePath = "/usr/share/i18n/SUPPORTED";
        private static bool _fileExists = File.Exists(SupportedLocalesFilePath);
        private static HashSet<string> _supportedLocales;

        public static void SetApplicationLocale(CultureInfo info)
        {
            if (!_fileExists)
            {
                Log.Error(LogTag, $"{SupportedLocalesFilePath} not found");
                return;
            }
            var converter = new SystemLocaleConverter();
            string convertedLocale = converter.Convert(info);

            if (VerifySupportedLocale(convertedLocale))
            {
                Interop.AppCore.AppLocaleManagerSetLanguage(convertedLocale);
                SetCurrentUICultureInfo(convertedLocale);
            }
            else
            {
                Log.Error(LogTag, $"Unsupported Language : {info.Name}");
            }
        }

        public static CultureInfo GetApplicationLocale()
        {
            IntPtr lang = IntPtr.Zero;

            int result = Interop.AppCore.AppLocaleManagerGetLanguage(out lang);

            if (result != 0 || lang == IntPtr.Zero)
            {
                Log.Error(LogTag, $"");
                return null;
            }

            string language = Marshal.PtrToStringUTF8(lang);

            var converter = new SystemLocaleConverter();
            return converter.Convert(language);
        }

        public static string GetSystemLocale()
        {
            IntPtr lang = IntPtr.Zero;

            int result = Interop.AppCore.AppLocaleManagerGetSystemLanguage(out lang);

            if (result != 0 || lang == IntPtr.Zero)
            {
                Log.Error(LogTag, $"");
                return null;
            }
            string language = Marshal.PtrToStringAnsi(lang);
            return language;
        }

        internal static void SetCurrentCultureInfo(string locale)
        {
            var converter = new SystemLocaleConverter();
            CultureInfo cultureInfo = converter.Convert(locale);
            if (cultureInfo != null)
            {
                CultureInfo.CurrentCulture = cultureInfo;
            }
            else
            {
                Log.Error(LogTag, "CultureInfo is null. locale: " + locale);
            }
        }

        internal static void SetCurrentUICultureInfo(string locale)
        {
            var converter = new SystemLocaleConverter();
            CultureInfo cultureInfo = converter.Convert(locale);
            if (cultureInfo != null)
            {
                CultureInfo.CurrentUICulture = cultureInfo;
            }
            else
            {
                Log.Error(LogTag, "CultureInfo is null. locale: " + locale);
            }
        }

        private static bool VerifySupportedLocale(string locale)
        {
            if (_supportedLocales == null)
            {
                LoadSupportedLocales();
            }

            return _supportedLocales.Contains(locale);
        }

        /// <summary>
        /// Parses a line from the SUPPORTED file and formats it into "{language}_{country}.{encoding}".
        /// The input lineParts can be:
        /// 1. ["{localeString}", "{encodingString}"] e.g., ["en_US", "UTF-8"]
        /// 2. ["{localeString}"] e.g., ["ko_KR"] (encoding will default to UTF-8)
        /// The localeString itself might contain an encoding, e.g., "ko_KR.UTF-8", which will be stripped.
        /// </summary>
        /// <param name="lineParts">Array of strings from splitting a line by space.</param>
        /// <returns>Formatted locale string or null if parsing fails.</returns>
        private static string ParseAndFormatLocaleLine(string[] lineParts)
        {
            if (lineParts == null || lineParts.Length == 0 || string.IsNullOrWhiteSpace(lineParts[0]))
            {
                return null;
            }

            string localeString = lineParts[0];
            string encodingString;

            if (lineParts.Length == 2)
            {
                encodingString = lineParts[1];
            }
            else // lineParts.Length == 1
            {
                encodingString = Encoding.UTF8.BodyName; // Default to UTF-8 if not specified on the line
            }

            // Strip any existing encoding from the localeString (e.g., "ko_KR.UTF-8" -> "ko_KR")
            int dotIndex = localeString.IndexOf('.');
            if (dotIndex != -1)
            {
                localeString = localeString[..dotIndex];
            }

            if (string.IsNullOrWhiteSpace(localeString) || string.IsNullOrWhiteSpace(encodingString))
            {
                return null;
            }

            return $"{localeString}.{encodingString}";
        }

        private static void LoadSupportedLocales()
        {
            _supportedLocales = File.ReadAllLines(SupportedLocalesFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) // Split by space only
                .Select(lineParts => ParseAndFormatLocaleLine(lineParts))
                .Where(formattedLocale => formattedLocale != null)
                .ToHashSet();
        }
    }
}
