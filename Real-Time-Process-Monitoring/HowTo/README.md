# Tutorial: Real Time Process Monitoring using Pubnub

In a Server environment, the Time is essential. And when it comes to a possible failure we need to act fast!

In this tutorial, we will build a Windows Service Application that monitors server processing and memory consumption sending alerts to a Windows Phone 8.1.

The Service starts a Thread that will get CPU Consumption and Available Memory and compares to parameters. If CPU usage >= **MaxCPUUsage** for a Time=**Period**, then a AlertType.PROCESS_ALERT message is send to channel.

Likewise, if Available Memory <= **MinRAMAvailable**, then a AlertType.RAM_ALERT message is send to channel.

## Real Time Process Monitoring Service

This is a Windows Service Project created using Visual Studio 2015 and .Net Framework 4. This Project is responsible to send Alert Message to **PNRTPM** channel.

### 1.Configuring parameters

>You can use RegEdit to configure tree parameters at HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\PubnubRTPM

**MaxCPUUsage**

The Max CPU Usage to alert in % (Default=50)

**MinRAMAvailable**

The min RAM available to alert in % (Default=10)

**Period**

The Period of Time to watch continue CPU usage in seconds (Default=60)

>Edit ImagePath and configure your parameters
>Example: "C:\Directory_OF_Application\PubnubRTPM.exe" 60 20 60

### 2. OnStart Service Method

This method is in Service.cs file and is trigger when Windows starts the Service. 

We subscribe to **PNRTPM** channel and a Thread is created to monitoring the server

``` java
protected override void OnStart(string[] args)
{
    string[] imagePathArgs = Environment.GetCommandLineArgs();
    if (imagePathArgs.Length >= 2)
    {
        int value = 0;
        if (Int32.TryParse(imagePathArgs[1], out value)) MaxCPUUsage = value;
        else
        {
            eventLog.WriteEntry("Fail to convert parameter 1:" + imagePathArgs[1]);
        }
    }
    if (imagePathArgs.Length >= 3)
    {
        int value = 0;
        if (Int32.TryParse(imagePathArgs[2], out value)) MinRAMAvailable = value;
        else
        {
            eventLog.WriteEntry("Fail to convert parameter 2:" + imagePathArgs[2]);
        }
    }
    if (imagePathArgs.Length >= 3)
    {
        int value = 0;
        if (Int32.TryParse(imagePathArgs[3], out value)) Period = value;
        else
        {
            eventLog.WriteEntry("Fail to convert parameter 3:" + imagePathArgs[3]);
        }
    }

    eventLog.WriteEntry("MaxCPUUsage=" + MaxCPUUsage.ToString() + ", " + "MinRAMAvailable=" + MinRAMAvailable.ToString() + ", " + "Period=" + Period.ToString());

    try
    {
        eventLog.WriteEntry("Subscribe channel " + channel);
        mrePubNub = new ManualResetEvent(false);
        pubnub.Subscribe<string>(channel, ReceivedMessageCallbackWhenSubscribed, SubscribeMethodForConnectCallback, ErrorCallback);
        mrePubNub.WaitOne(manualResetEventsWaitTimeout);
        eventLog.WriteEntry("Channel " + channel + " subscribed.");

        trf = new Thread(new ThreadStart(Process));
        trf.IsBackground = true;
        trf.SetApartmentState(ApartmentState.MTA);
        trf.Start();
    }
    catch (Exception erro)
    {
        eventLog.WriteEntry("Subscribe channel " + channel + "\r\n" + erro.Message);
    }
}
```

### 3. Process Thread

This Thread is in Service.cs file and is started from **OnStart Service Method**

This Thread uses a PerformanceCounter class to get the CPU usage and Available Memory and compares with parameters. If CPU usage >= MaxCPUUsage for a Time=Period, then a AlertType.PROCESS_ALERT message is send to channel.

Likewise, if Available Memory <= MinRAMAvailable, then a AlertType.RAM_ALERT message is send to channel.

``` java
private void Process()
{
    while (!finalizeService)
    {
        PerformanceCounter PC = new PerformanceCounter();
        PC.CategoryName = "Processor";
        PC.CounterName = "% Processor Time";
        PC.InstanceName = "_Total";
        PC.ReadOnly = true;
        var value = PC.NextValue();
        Thread.Sleep(1000);
        value = PC.NextValue();
        PC.Close();
        PC.Dispose();

        PerformanceCounter ramCounter = new PerformanceCounter("Memory", "Available MBytes", true);
        var ramValue = ramCounter.NextValue();
        if (ramValue <= MinRAMAvailable)
        {
            SendAlertMessage(AlertType.RAM_ALERT, value,Convert.ToInt64(ramValue));
        }

        if (value >= MaxCPUUsage)
        {
            totalHits = totalHits + 1;
            if (totalHits == Period)
            {
                SendAlertMessage(AlertType.PROCESS_ALERT, value, Convert.ToInt64(ramValue));
                totalHits = 0;
            }
        }
        else
        {
            totalHits = 0;
        }

    }
    eventLog.WriteEntry(ServiceName +  " stoped.");
}
```

### 4. Sending Alert Message

This method is in Service.cs file and is started from **Process Thread**

To compose an Alert Message, a list of RTPMProcess is create getting CPU usage and Memory of each process.

After that, a RTPMServer class is create with the Alert Type, Date, Server Name, CPU Usage, Ram Available and RTPMProcessList and it's sent to the channel

``` java
private void SendAlertMessage(AlertType alertType, double value, long ramValue)
{
    List<RTPMProcess> list = new List<RTPMProcess>();
    lstPerformance = new List<PerformanceCounter>();
    Process[] processes = System.Diagnostics.Process.GetProcesses();
    for (int i = 0; i < processes.Length; i++)
    {
        PerformanceCounter pc = new PerformanceCounter("Process", "% Processor Time", processes[i].ProcessName, true);
        try
        {
            pc.NextValue();
        }
        catch { }
        lstPerformance.Add(pc);
    }
    Thread.Sleep(1000);
    for (int i = 0; i < processes.Length; i++)
    {
        RTPMProcess r = new RTPMProcess();
        r.RAMUsage = processes[i].PrivateMemorySize64;
        r.ProcessName = processes[i].ProcessName;
        r.Id = processes[i].Id;
        try
        {
            r.CPUUsage = lstPerformance[i].NextValue() / Environment.ProcessorCount;
        }
        catch { }
        list.Add(r);
    }

    var pList = (from pp in list orderby pp.CPUUsage descending select pp).ToList();

    mrePublish = new ManualResetEvent(false);
    RTPMServer rtpmServer = new RTPMServer();
    rtpmServer.AlertType = alertType;
    rtpmServer.Date = DateTime.UtcNow;
    rtpmServer.ServerName = Environment.MachineName;
    rtpmServer.CPUUsage = value;
    rtpmServer.RAMAvailable = ramValue;
    rtpmServer.Processes = pList;
    publishedMessage = rtpmServer;
    pubnub.Publish<string>(channel, publishedMessage, PublishCallback, ErrorCallback);
    mrePublish.WaitOne(manualResetEventsWaitTimeout);

}
```

### 5. OnStop Service Method

This method is in Service.cs file and is trigger when Windows stops the Service. 

We unsubscribe **PNRTPM** channel and finalize a Thread that was create to monitoring the server

``` java
protected override void OnStop()
{
    try
    {
        eventLog.WriteEntry("Unsubscribe channel " + channel);
        mrePubNub = new ManualResetEvent(false);
        pubnub.Unsubscribe<string>(channel, UnsubscribeCallback, SubscribeMethodForConnectCallback, UnsubscribeMethodForDisconnectCallback, ErrorCallback);
        mrePubNub.WaitOne(manualResetEventsWaitTimeout);
        eventLog.WriteEntry("Channel " + channel + " unsubscribed.");
        finalizeService = true;
    }
    catch (Exception erro)
    {
        eventLog.WriteEntry("Unsubscribe channel " + channel + "\r\n" + erro.Message);
    }
}
```

## Real Time Process Monitoring Windows Phone 8.1

This is a Windows Phone 8.1 Project created using Visual Studio 2015 and Windows Phone 8.1 SDK

You can receive Alert Messages from Real Time Process Monitoring Service. You also receive messages from more than one server.

This Project is responsible to process Alert Message from **PNRTPM** channel.

![Real Time Process Monitoring Logo](https://github.com/marceloinacio/PubnubBlog/tree/master/Real-Time-Process-Monitoring/HowTo/images/windowsphone81.png)

This is MainPage.xaml
``` xml
<Page
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:PhoneRTPM"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:System="using:System"
    x:Class="PhoneRTPM.MainPage"
    mc:Ignorable="d"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">

    <Grid Margin="0,-26.667,0,-0.333">
        <Grid.Background>
            <LinearGradientBrush EndPoint="0.5,1" StartPoint="0.5,0">
                <GradientStop Color="Black" Offset="1"/>
                <GradientStop Color="#FFE60707"/>
            </LinearGradientBrush>
        </Grid.Background>
        <Image x:Name="image" HorizontalAlignment="Center" Height="63" Margin="74,574,92,0" VerticalAlignment="Top" Width="234" Source="Assets/pubnub_large.png"/>
        <Button x:Name="btnStart" Content="Start Monitoring" HorizontalAlignment="Center" Margin="58,489,72,0" VerticalAlignment="Top" Height="72" Width="270" Click="btnStart_Click"/>
        <ScrollViewer Name="scroll" Width="400" Height="441" VerticalScrollBarVisibility="Visible" VerticalAlignment="Bottom" VerticalScrollMode="Enabled" Margin="0,0,0,190">
            <Grid Name="grid" HorizontalAlignment="Left" Height="auto" Margin="0,0,0,0" VerticalAlignment="Top" Width="400"  >
                <Grid.RowDefinitions>
                    <RowDefinition Height="30" />
                </Grid.RowDefinitions>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="0.9*" />
                    <ColumnDefinition Width="0.9*" />
                    <ColumnDefinition Width="0.5*" />
                    <ColumnDefinition Width="0.5*" />
                </Grid.ColumnDefinitions>
                <TextBlock FontSize="18.667" HorizontalAlignment="Left">Date</TextBlock>
                <TextBlock FontSize="18.667" Grid.Column="1" Grid.Row="0" HorizontalAlignment="Left" >Server</TextBlock>
                <TextBlock FontSize="18.667" Grid.Column="2" Grid.Row="0" HorizontalAlignment="Center" >CPU</TextBlock>
                <TextBlock FontSize="18.667" Grid.Column="3" Grid.Row="0" HorizontalAlignment="Center" >RAM</TextBlock>
            </Grid>
        </ScrollViewer>


    </Grid>
</Page>
```


### 1. Start Monitoring

When you click on button Start Monitoring, we subscribe to **PNRTPM** channel and wait for an Alert Message.

### 2. Stop Monitoring

If you click on button Stop Monitoring, we unsubscribe **PNRTPM** channel.

### 3. Alert Message Received

This method is in MainPage.xaml.cs file and is trigger when there is a message on **PNRTPM** channel.

``` java
private void ReceivedMessageCallbackWhenSubscribed(string result)
{
    if (!string.IsNullOrEmpty(result) && !string.IsNullOrEmpty(result.Trim()))
    {
        List<object> deserializedMessage = pubnub.JsonPluggableLibrary.DeserializeToListOfObject(result);
        if (deserializedMessage != null && deserializedMessage.Count > 0)
        {
            object subscribedObject = (object)deserializedMessage[0];
            if (subscribedObject != null)
            {
                string serializedResultMessage = pubnub.JsonPluggableLibrary.SerializeToJsonString(subscribedObject);
                RTPMServer rtpmServer = JsonConvert.DeserializeObject<RTPMServer>(serializedResultMessage);

                Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => {
                    ctd = grid.RowDefinitions.Count;
                    grid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(columnHeight) });

                    TextBlock t = new TextBlock();
                    t.HorizontalAlignment = HorizontalAlignment.Left;
                    t.FontSize = 11;
                    t.Text = rtpmServer.Date.ToLocalTime().ToString();
                    t.SetValue(Grid.ColumnProperty, 0);
                    t.SetValue(Grid.RowProperty, ctd);
                    grid.Children.Add(t);

                    TextBlock tS = new TextBlock();
                    tS.HorizontalAlignment = HorizontalAlignment.Left;
                    tS.FontSize = 12;
                    tS.Text = rtpmServer.ServerName;
                    tS.SetValue(Grid.ColumnProperty, 1);
                    tS.SetValue(Grid.RowProperty, ctd);
                    grid.Children.Add(tS);

                    TextBlock tC = new TextBlock();
                    tC.HorizontalAlignment = HorizontalAlignment.Center;
                    tC.FontSize = 14;
                    tC.Text = rtpmServer.CPUUsage.ToString("###.##") + " %";
                    tC.SetValue(Grid.ColumnProperty, 2);
                    tC.SetValue(Grid.RowProperty, ctd);
                    grid.Children.Add(tC);

                    TextBlock tR = new TextBlock();
                    tR.HorizontalAlignment = HorizontalAlignment.Center;
                    tR.FontSize = 14;
                    tR.Text = rtpmServer.RAMUsage.ToString() + " Mb";
                    tR.SetValue(Grid.ColumnProperty, 3);
                    tR.SetValue(Grid.RowProperty, ctd);
                    grid.Children.Add(tR);

                    ctd++;
                }).GetResults();
            }
        }
    }
    mrePubNub.Set();
}
```

You can download the Real Time Process Monitoring Service [here](https://github.com/marceloinacio/PubnubBlog/tree/master/Real-Time-Process-Monitoring/PubnubRTPM)

You can download the Real Time Process Monitoring Windows Phone 8.1 [here](https://github.com/marceloinacio/PubnubBlog/tree/master/Real-Time-Process-Monitoring/PhoneRTPM)
