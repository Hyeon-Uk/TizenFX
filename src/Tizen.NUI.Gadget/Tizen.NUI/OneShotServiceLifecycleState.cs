using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tizen.NUI
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public enum OneShotServiceLifecycleState
    {
        Initialized = 0,
        Created = 1,
        Running = 2,
        Destroyed = 3,
    }
}
