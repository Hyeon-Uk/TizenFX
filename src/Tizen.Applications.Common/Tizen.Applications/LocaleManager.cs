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

using System.Collections.Generic;
using System.Globalization;

namespace Tizen.Applications
{
    static class LocaleManager
    {
        private static string LogTag = "Tizen.Applications";
        private static string SupportedLocalesFilePath = "/usr/share/i18n/SUPPORTED";
        private static HashSet<string> supportedLocales;

        static LocaleManager() { }

        public static void SetApplicationLocale(CultureInfo info)
        { 
                        
        }

        public static CultureInfo GetApplicationLocale() {
            return null;
        }

        public static CultureInfo GetSystemLocale() {
            return null;
        }
    }
}