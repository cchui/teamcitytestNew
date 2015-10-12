using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ICAPInterfaceLib
{
    public class ICAPException : Exception
    {
        public ICAPException(string message)
            : base(message)
        {
        }
    }
}
