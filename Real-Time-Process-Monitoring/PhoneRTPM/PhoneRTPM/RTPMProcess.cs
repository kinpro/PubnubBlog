using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PhoneRTPM
{
    public class RTPMProcess
    {
        public int Id { get; set; }
        public string ProcessName { get; set; }
        public double CPUUsage { get; set; }
        public long RAMUsage { get; set; }
    }
}
