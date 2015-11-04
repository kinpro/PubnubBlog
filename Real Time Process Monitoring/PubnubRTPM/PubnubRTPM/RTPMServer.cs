using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PubnubRTPM
{
    public class RTPMServer
    {
        public string ServerName { get; set; }
        public List<RTPMProcess> Processes { get; set; }
    }
}
