using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tizen.NUI
{
    public class ServiceFactory
    {
        private ServiceFactory(){}

        public static OneShotService CreateService(string name, bool autoClose)
        {
            return new OneShotService(name, autoClose);
        }
    }
}
