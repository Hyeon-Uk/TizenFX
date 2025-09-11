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
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tizen.NUI
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class OneShotService
    {
        public OneShotServiceLifecycleState State
        {
            get;
            internal set;
        }

        public EventHandler LifecycleStateChanged
        {
            get;
            internal set;
        }

        public string Name
        {
            get;
            set;
        }

        private bool _autoClose;

        public OneShotService(string name,bool autoClose)
        {
            Name = name;
            _autoClose = autoClose;
            State = OneShotServiceLifecycleState.Initialized;
        }

        protected virtual void OnCreate()
        {
            State = OneShotServiceLifecycleState.Created;
        }

        protected virtual void OnDestroy()
        {
            State = OneShotServiceLifecycleState.Destroyed;
        }

        public async void Run()
        {
            State = OneShotServiceLifecycleState.Running;
            OnCreate();
            while (!_autoClose)
            {
                await Task.Delay(100); // Prevent tight loop, allow for async operations and cancellation checks
            }
            OnDestroy();
        }

        public void Exit(bool waitForJoin)
        {
            _autoClose = true;
        }
    }
}
