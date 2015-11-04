using PubNubMessaging.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PubnubRTPM
{
    public partial class Service : ServiceBase
    {
        const string publishKey = "demo";
        const string subscribeKey = "demo";
        const string secretKey = "demo";
        const string channel = "PNRTPM";
        Pubnub pubnub = null;
        ManualResetEvent mrePubNub = new ManualResetEvent(false);
        ManualResetEvent mrePublish = new ManualResetEvent(false);
        object publishedMessage = null;
        bool receivedMessage = false;
        bool isPublished = false;
        int manualResetEventsWaitTimeout = 30 * 1000;
        EventLog eventLog;
        Thread trf = null;
        bool finalizeService = false;
        int totalHits = 0;
        List<PerformanceCounter> lstPerformance = null;
        /// <summary>
        /// The Max CPU Usage to alert in %
        /// </summary>
        int MaxCPUUsage = 50;
        /// <summary>
        /// The min RAM available to alert in %
        /// </summary>
        int MinRAMAvailable = 10;
        /// <summary>
        /// The Period of Time to watch continue CPU usage in seconds
        /// </summary>
        int Period = 60;


        public Service()
        {
            InitializeComponent();
            if (!EventLog.SourceExists(ServiceName))
            {
                EventLog.CreateEventSource(ServiceName, "Application");
            }

            eventLog = new EventLog();
            eventLog.Source = ServiceName;
            pubnub = new Pubnub(publishKey, subscribeKey, secretKey, "", false);
        }

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

                trf = new Thread(new ThreadStart(ProcessTrf));
                trf.IsBackground = true;
                trf.SetApartmentState(ApartmentState.MTA);
                trf.Start();
            }
            catch (Exception erro)
            {
                eventLog.WriteEntry("Subscribe channel " + channel + "\r\n" + erro.Message);
            }

        }

        private void ProcessTrf()
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

                if (value >= MaxCPUUsage)
                {
                    totalHits = totalHits + 1;
                    if (totalHits == Period)
                    {
                        List<RTPMProcess> list = new List<RTPMProcess>();
                        lstPerformance = new List<PerformanceCounter>();
                        Process[] processes = Process.GetProcesses();
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
                        rtpmServer.ServerName = Environment.MachineName;
                        rtpmServer.Processes = pList;
                        publishedMessage = rtpmServer;
                        pubnub.Publish<string>(channel, publishedMessage, PublishCallback, ErrorCallback);
                        mrePublish.WaitOne(manualResetEventsWaitTimeout);


                        totalHits = 0;
                    }
                }
                else
                {
                    totalHits = 0;
                }

                PerformanceCounter ramCounter = new PerformanceCounter("Memory", "Available MBytes", true);
                var ramValue = ramCounter.NextValue();
                if (ramValue<=MinRAMAvailable)
                {
                    mrePublish = new ManualResetEvent(false);
                    publishedMessage = String.Format("RAM Alert! Less than {0}% available", MinRAMAvailable);
                    pubnub.Publish<string>(channel, publishedMessage, PublishCallback, ErrorCallback);
                    mrePublish.WaitOne(manualResetEventsWaitTimeout);
                }
            }
            eventLog.WriteEntry(ServiceName +  " stoped.");
        }

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


        internal void TestStartupAndStop(string[] args)
        {
            this.OnStart(args);
            Console.WriteLine("Press a key...");
            Console.ReadLine();
            this.OnStop();
        }

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
                        string serializedPublishMesage = pubnub.JsonPluggableLibrary.SerializeToJsonString(publishedMessage);
                        if (serializedResultMessage == serializedPublishMesage)
                        {
                            receivedMessage = true;
                        }

                    }
                }
            }
            mrePubNub.Set();
        }

        void SubscribeMethodForConnectCallback(string receivedMessage)
        {
            mrePubNub.Set();
        }

        private void ErrorCallback(PubnubClientError result)
        {
            if (result != null)
            {
                eventLog.WriteEntry("PubnubClientError result = " + result.Message);
            }
            mrePubNub.Set();
            mrePublish.Set();
        }

        private void UnsubscribeCallback(string result)
        {
            mrePubNub.Set();
        }

        void UnsubscribeMethodForDisconnectCallback(string receivedMessage)
        {
            mrePubNub.Set();
        }

        private void PublishCallback(string result)
        {
            if (!string.IsNullOrEmpty(result) && !string.IsNullOrEmpty(result.Trim()))
            {
                List<object> deserializedMessage = pubnub.JsonPluggableLibrary.DeserializeToListOfObject(result);
                if (deserializedMessage != null && deserializedMessage.Count > 0)
                {
                    long statusCode = Int64.Parse(deserializedMessage[0].ToString());
                    string statusMessage = (string)deserializedMessage[1];
                    if (statusCode == 1 && statusMessage.ToLower() == "sent")
                    {
                        isPublished = true;
                    }
                }
            }

            mrePublish.Set();
        }
    }
}
