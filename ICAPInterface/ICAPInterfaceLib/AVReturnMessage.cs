using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ICAPInterfaceLib
{
    public class AVReturnMessage
    {
        public bool Success { get; set; }
        public int ICAPStatusCode { get; set; }
        public int HTTPStatusCode { get; set; }
        public string Message { get; set; }
    }
}
