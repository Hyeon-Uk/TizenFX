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

            string convertedLocale = ConvertCultureInfo(info);

            if (VerifySupportedLocale(convertedLocale))
            {
                Interop.AppCore.UpdateLanguage(convertedLocale);
            }
            else
            {
                Log.Error(LogTag, $"Unsupported Language : {info.Name}");
            }
        }

        public static CultureInfo GetApplicationLocale()
        {
            return null;
        }

        public static CultureInfo GetSystemLocale()
        {
            return null;
        }

        private static string ConvertCultureInfo(CultureInfo info)
        {
            return $"{info.Name}.{Encoding.UTF8.BodyName}";
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

        internal class ULocale
        {
            private const int ULOC_FULLNAME_CAPACITY = 157;
            private const int ULOC_LANG_CAPACITY = 12;
            private const int ULOC_SCRIPT_CAPACITY = 6;
            private const int ULOC_COUNTRY_CAPACITY = 4;
            private const int ULOC_VARIANT_CAPACITY = ULOC_FULLNAME_CAPACITY;

            internal ULocale(string locale)
            {
                Locale = Canonicalize(locale);
                Language = GetLanguage(Locale);
                Script = GetScript(Locale);
                Country = GetCountry(Locale);
                Variant = GetVariant(Locale);
                LCID = GetLCID(Locale);
            }

            internal string Locale { get; private set; }
            internal string Language { get; private set; }
            internal string Script { get; private set; }
            internal string Country { get; private set; }
            internal string Variant { get; private set; }
            internal int LCID { get; private set; }

            private string Canonicalize(string localeName)
            {
                // Get the locale name from ICU
                StringBuilder sb = new StringBuilder(ULOC_FULLNAME_CAPACITY);
                if (Interop.BaseUtilsi18n.Canonicalize(localeName, sb, sb.Capacity) <= 0)
                {
                    return null;
                }

                return sb.ToString();
            }

            private string GetLanguage(string locale)
            {
                // Get the language name from ICU
                StringBuilder sb = new StringBuilder(ULOC_LANG_CAPACITY);
                if (Interop.BaseUtilsi18n.GetLanguage(locale, sb, sb.Capacity, out int bufSizeLanguage) != 0)
                {
                    return null;
                }

                return sb.ToString();
            }

            private string GetScript(string locale)
            {
                // Get the script name from ICU
                StringBuilder sb = new StringBuilder(ULOC_SCRIPT_CAPACITY);
                if (Interop.BaseUtilsi18n.GetScript(locale, sb, sb.Capacity) <= 0)
                {
                    return null;
                }

                return sb.ToString();
            }

            private string GetCountry(string locale)
            {
                int err = 0;

                // Get the country name from ICU
                StringBuilder sb = new StringBuilder(ULOC_COUNTRY_CAPACITY);
                if (Interop.BaseUtilsi18n.GetCountry(locale, sb, sb.Capacity, out err) <= 0)
                {
                    return null;
                }

                return sb.ToString();
            }

            private string GetVariant(string locale)
            {
                // Get the variant name from ICU
                StringBuilder sb = new StringBuilder(ULOC_VARIANT_CAPACITY);
                if (Interop.BaseUtilsi18n.GetVariant(locale, sb, sb.Capacity) <= 0)
                {
                    return null;
                }

                return sb.ToString();
            }

            private int GetLCID(string locale)
            {
                // Get the LCID from ICU
                uint lcid = Interop.BaseUtilsi18n.GetLCID(locale);
                return (int)lcid;
            }

            internal static string GetDefaultLocale()
            {
                IntPtr stringPtr = Interop.Libc.GetEnvironmentVariable("LANG");
                if (stringPtr == IntPtr.Zero)
                {
                    return string.Empty;
                }

                return Marshal.PtrToStringAnsi(stringPtr);
            }
        }
    }
}
