# Tutorial: Real Time Process Monitoring using Pubnub

Time is essential in a server environment. And when it comes to a possible failure we need to act fast!

In this tutorial, we will build a Windows service application that monitors server processing and memory consumption, sending alerts to a Windows Phone 8.1.

The service starts a thread that will get CPU consumption and available memory and compares to parameters. If the CPU usage is greater than a threshold (MaxCPUUsage) for a certain length of time (Period) then send a message with the type `AlertType.PROCESS_ALERT` to the channel.

Likewise, if available memory less than a threshold (MinRAMAvailable) then send a message with the type `AlertType.RAM_ALERT` to the channel.

## Real Time Process Monitoring Service

This is a Windows service project created using Visual Studio 2015 and .Net Framework 4. This project is responsible to send alert message to **PNRTPM** channel.

### Configuring parameters

>You can use RegEdit to configure tree parameters at HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\PubnubRTPM

**MaxCPUUsage**

The max CPU usage to alert in % (default=50)

**MinRAMAvailable**

The min RAM available to alert in % (default=10)

**Period**

The period of time to watch continue CPU usage in seconds (default=60)

>Edit `imagePath` and configure your parameters
>
>Example: "C:\Directory_OF_Application\PubnubRTPM.exe" 60 20 60

### OnStart Service Method

This method is in the `service.cs` file and is triggered when Windows starts the service. 

We subscribe to **PNRTPM** channel and a thread is created to monitor the server while service is running.

``` csharp
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

### Process Thread

This thread is in the `service.cs` file and is started from `OnStart Service` method.

This thread uses a `PerformanceCounter` class to get the CPU usage and available memory and compares with parameters. If the CPU usage is greater than a threshold (MaxCPUUsage) for a certain length of time (Period) then send a message with the type `AlertType.PROCESS_ALERT` to the channel.

Likewise, if available memory less than a threshold (MinRAMAvailable) then send a message with the type `AlertType.RAM_ALERT` to the channel.

``` csharp
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

### Sending Alert Message

This method is in the `service.cs` file and is started from `process` thread.

To compose an alert message, a list of `RTPMProcess` is created getting CPU usage and memory of each process.

After that, a `RTPMServer` class is created with the `alert type, date, server name, CPU usage, RAM available, RTPMProcessList` and it's sent to the channel.

``` csharp
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

### OnStop Service Method

This method is in the `service.cs` file and is triggered when Windows stops the service. 

We unsubscribe **PNRTPM** channel and finalize the thread that was created to monitor the server.

``` csharp
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

This is a Windows Phone 8.1 project created using Visual Studio 2015 and Windows Phone 8.1 SDK

You can receive alert messages from Real Time Process Monitoring service. You also receive messages from more than one server.

This project is responsible to process alert messages from **PNRTPM** channel.

![Real Time Process Monitoring Logo](https://raw.github.com/marceloinacio/PubnubBlog/master/Real-Time-Process-Monitoring/HowTo/images/windowsphone81.png)

This is the `mainpage.xaml` of figure above using `XAML` notation. We created a grid for messages and a button to starts and stops monitoring.
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


### Start Monitoring

When you click on button `Start Monitoring`, we subscribe to **PNRTPM** channel and wait for an alert message.

### Stop Monitoring

If you click on button `Stop Monitoring`, we unsubscribe **PNRTPM** channel.

### Alert Message Received

This method is in the `mainpage.xaml.cs` file and is triggered when there is a message on **PNRTPM** channel.

``` csharp
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

### Conclusion

In this tutorial you can learn about Windows service, collecting CPU and memory information, Windows Phone 8.1 SDK and send/receive messages using Pubnub c-sharp SDK.
We collected CPU and memory information using `PerformanceCounter` class and send messages using `publish` function.

You can add an e-mail notification and SMS message on the service project to notify the administrator about CPU and memory consumption and also improve Windows Phone 8.1 project adding a window where the user could see all server processes and find out which is consuming the CPU or memory.

You can download the Real Time Process Monitoring service project [here](https://github.com/marceloinacio/PubnubBlog/tree/master/Real-Time-Process-Monitoring/PubnubRTPM)

You can download the Real Time Process Monitoring Windows Phone 8.1 project [here](https://github.com/marceloinacio/PubnubBlog/tree/master/Real-Time-Process-Monitoring/PhoneRTPM)
