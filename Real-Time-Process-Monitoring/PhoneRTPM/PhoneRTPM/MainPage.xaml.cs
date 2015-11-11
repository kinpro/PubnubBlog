using PubNubMessaging.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Popups;
using Newtonsoft.Json;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=391641

namespace PhoneRTPM
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        const string PublishKey = "demo";
        const string SubscribeKey = "demo";
        const string SecretKey = "demo";
        const string channel = "PNRTPM";
        Pubnub pubnub = new Pubnub(PublishKey, SubscribeKey, SecretKey, "", false);
        ManualResetEvent mrePubNub = new ManualResetEvent(false);
        int manualResetEventsWaitTimeout = 30 * 1000;



        int ctd = 0;
        int columnHeight = 25;
        public MainPage()
        {
            this.InitializeComponent();

            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.
        /// This parameter is typically used to configure the page.</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            // TODO: Prepare page for display here.

            // TODO: If your application contains multiple pages, ensure that you are
            // handling the hardware Back button by registering for the
            // Windows.Phone.UI.Input.HardwareButtons.BackPressed event.
            // If you are using the NavigationHelper provided by some templates,
            // this event is handled for you.
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if (btnStart.Content.ToString().ToLower()=="start monitoring")
            {
                btnStart.Content = "Stop Monitoring";
                mrePubNub = new ManualResetEvent(false);
                pubnub.Subscribe<string>(channel, ReceivedMessageCallbackWhenSubscribed, SubscribeMethodForConnectCallback, ErrorCallback);
                mrePubNub.WaitOne(manualResetEventsWaitTimeout);

            }
            else
            {
                btnStart.Content = "Start Monitoring";
                mrePubNub = new ManualResetEvent(false);
                pubnub.Unsubscribe<string>(channel, UnsubscribeCallback, SubscribeMethodForConnectCallback, UnsubscribeMethodForDisconnectCallback, ErrorCallback);
                mrePubNub.WaitOne(manualResetEventsWaitTimeout);
            }
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

        void SubscribeMethodForConnectCallback(string receivedMessage)
        {
            mrePubNub.Set();
        }

        private void ErrorCallback(PubnubClientError result)
        {
            if (result != null)
            {
                MessageDialog dlg = new MessageDialog(result.Message);
                dlg.ShowAsync().GetResults();
            }
            mrePubNub.Set();
        }

        private void UnsubscribeCallback(string result)
        {
            mrePubNub.Set();
        }

        void UnsubscribeMethodForDisconnectCallback(string receivedMessage)
        {
            mrePubNub.Set();
        }

    }
}
