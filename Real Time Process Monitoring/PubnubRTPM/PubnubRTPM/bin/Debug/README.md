Pubnub Real Time Process Monitoring Service
.Net Framework 4.0

Monitoring Process and Memory sending alerts on PNRTPM channel.

publishKey = "demo";
subscribeKey = "demo";
secretKey = "demo";
channel = "PNRTPM";

You can set parameters using this sequence:
MaxCPUUsage MinRAMAvailable Period

MaxCPUUsage: The Max CPU Usage to alert in % (Default=50)
MinRAMAvailable: The min RAM available to alert in % (Default=10)
Period: The Period of Time to watch continue CPU usage in seconds (Default=60)

Use regedit to set your custom parameters!