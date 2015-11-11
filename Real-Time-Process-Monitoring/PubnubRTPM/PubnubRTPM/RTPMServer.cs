using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PubnubRTPM
{
    public class RTPMServer
    {
        public AlertType AlertType { get; set; }
        public string ServerName { get; set; }
        public DateTime Date { get; set; }
        public double CPUUsage { get; set; }
        public long RAMAvailable { get; set; }
        public List<RTPMProcess> Processes { get; set; }
    }

    public enum AlertType
    {
        RAM_ALERT,
        PROCESS_ALERT
    }
}
