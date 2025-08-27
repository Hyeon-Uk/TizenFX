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
using System.Runtime.InteropServices;

internal static partial class Interop
{
    internal static partial class AppCore
    {
        [DllImport(Libraries.AppCommon, EntryPoint = "app_locale_manager_set_language")]
        internal static extern int AppLocaleManagerSetLanguage(string lang);
        // int app_locale_manager_set_language(const char* lang)

        [DllImport(Libraries.AppCommon, EntryPoint = "app_locale_manager_get_language")]
        internal static extern int AppLocaleManagerGetLanguage(out IntPtr langPtr);
        // int app_locale_manager_get_language(const char** lang)

        [DllImport(Libraries.AppCommon, EntryPoint = "app_locale_manager_get_system_language")]
        internal static extern int AppLocaleManagerGetSystemLanguage(out IntPtr langPtr);
        // int app_locale_manager_get_system_language(const char** lang)
    }
}
