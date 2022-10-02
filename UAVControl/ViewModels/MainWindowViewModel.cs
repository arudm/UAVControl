using log4net;
using MapControl;
using MissionPlanner;
using MissionPlanner.ArduPilot;
using MissionPlanner.Comms;
using MissionPlanner.Utilities;
using netDxf.Entities;
using netDxf.Tables;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using UAVControl.Commands;
using UAVControl.Models;
using UAVControl.ViewModels.Base;
using static alglib;
using static System.CustomMessageBox;

namespace UAVControl.ViewModels
{
    internal class MainWindowViewModel : ViewModel
    {
        private static readonly ILog log =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public List<PointItem> Points { get; } = new List<PointItem>();
        public List<PointItem> Pushpins { get; } = new List<PointItem>();
        public List<PolylineItem> Polylines { get; } = new List<PolylineItem>();
        //public MAVLink.MavlinkParse mavlink { get; set; } = new MAVLink.MavlinkParse();
        public string? TelemetryPacket { get; set; }
        public UdpSerial UdpSerial { get; set; } = new UdpSerial();
        public DateTime Connecttime { get; set; }
        public bool UseCachedParams { get; set; } = false;
        ManualResetEvent SerialThreadrunner = new ManualResetEvent(false);

        /// <summary>
        /// store the time we first connect
        /// </summary>
        DateTime connecttime = DateTime.Now;

        DateTime nodatawarning = DateTime.Now;
        DateTime OpenTime = DateTime.Now;

        /// <summary>
        /// controls the main serial reader thread
        /// </summary>
        bool serialThread = false;

        /// <summary>
        /// Active Comport interface
        /// </summary>
        public MAVLinkInterface comPort
        {
            get { return _comPort; }
            set
            {
                if (_comPort == value)
                    return;
                _comPort = value;
                /* if (instance == null)
                     return;
                 _comPort.MavChanged -= instance.comPort_MavChanged;
                 _comPort.MavChanged += instance.comPort_MavChanged;
                 instance.comPort_MavChanged(null, null);*/
            }
        }
        private MAVLinkInterface _comPort = new MAVLinkInterface();

        /// <summary>
        /// speech engine enable
        /// </summary>
        public bool speechEnable
        {
            get { return speechEngine == null ? false : speechEngine.speechEnable; }
            set
            {
                if (speechEngine != null) speechEngine.speechEnable = value;
            }
        }

        public bool speech_armed_only = false;
        public bool speechEnabled()
        {
            if (!speechEnable)
            {
                return false;
            }
            if (speech_armed_only)
            {
                return comPort.MAV.cs.armed;
            }
            return true;
        }

        /// <summary>
        /// spech engine static class
        /// </summary>
        public  ISpeech speechEngine { get; set; }

        /// <summary>
        /// track the last heartbeat sent
        /// </summary>
        private DateTime heatbeatSend = DateTime.Now;

        /// <summary>
        /// passive comports
        /// </summary>
        public List<MAVLinkInterface> Comports = new List<MAVLinkInterface>();


        //-------------------------------------------------------------------------
        #region Команды

        #region ConnectionUdpCommand
        public ICommand ConnectionUdpCommand { get; }

        private bool CanConnectionUdpCommandExecute(object p) => true;
        private void OnConnectionUdpCommandExecuted(object p)
        {
            comPort.giveComport = false;

            log.Info("MenuConnect Start");
            /*
            // sanity check
            if (comPort.BaseStream.IsOpen && comPort.MAV.cs.groundspeed > 4)
            {
                if ((int)DialogResult.No ==
                    CustomMessageBox.Show(Strings.Stillmoving, Strings.Disconnect, MessageBoxButtons.YesNo))
                {
                    return;
                }
            }
            */
            try
            {
                log.Info("Cleanup last logfiles");
                // cleanup from any previous sessions
                if (comPort.logfile != null)
                    comPort.logfile.Close();

                if (comPort.rawlogfile != null)
                    comPort.rawlogfile.Close();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(Strings.ErrorClosingLogFile + ex.Message, Strings.ERROR);
            }

            comPort.logfile = null;
            comPort.rawlogfile = null;
            comPort.BaseStream = new UdpSerial(); //(MissionPlanner.Comms.ICommsSerial)UdpSerial;

            BackgroundWorker bgw = new BackgroundWorker();

            bgw.DoWork += bgw_DoWork;

            bgw.RunWorkerAsync();
        }

        void bgw_DoWork(object sender, DoWorkEventArgs e)
        {
            // decide if this is a connect or disconnect
            if (comPort.BaseStream.IsOpen)
            {
                doDisconnect(comPort);
            }
            else
            {
                doConnect(comPort, "UDP", "115200");
                Comports.Add(comPort);
            }

            //_connectionControl.UpdateSysIDS();

            //if (comPort.BaseStream.IsOpen)
            //    loadph_serial();
        }

        public void doDisconnect(MAVLinkInterface comPort)
        {
            log.Info("We are disconnecting");
            try
            {
                //if (speechEngine != null) // cancel all pending speech
                //    speechEngine.SpeakAsyncCancelAll();

                comPort.BaseStream.DtrEnable = false;
                comPort.Close();
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            /*
            // now that we have closed the connection, cancel the connection stats
            // so that the 'time connected' etc does not grow, but the user can still
            // look at the now frozen stats on the still open form
            try
            {
                // if terminal is used, then closed using this button.... exception
                if (this.connectionStatsForm != null)
                    ((ConnectionStats)this.connectionStatsForm.Controls[0]).StopUpdates();
            }
            catch
            {
            }

            // refresh config window if needed
            if (MyView.current != null)
            {
                if (MyView.current.Name == "HWConfig")
                    MyView.ShowScreen("HWConfig");
                if (MyView.current.Name == "SWConfig")
                    MyView.ShowScreen("SWConfig");
            }

            try
            {
                System.Threading.ThreadPool.QueueUserWorkItem((WaitCallback)delegate
                {
                    try
                    {
                        MissionPlanner.Log.LogSort.SortLogs(Directory.GetFiles(Settings.Instance.LogDir, "*.tlog"));
                    }
                    catch
                    {
                    }
                }
                );
            }
            catch
            {
            }

            this.MenuConnect.Image = global::MissionPlanner.Properties.Resources.light_connect_icon;
            */
        }

        public void doConnect(MAVLinkInterface comPort, string portname, string baud, bool getparams = false, bool showui = false)
        {
            bool skipconnectcheck = false;
            log.Info($"We are connecting to {portname} {baud}");
            switch (portname)
            {
                /*case "preset":
                    skipconnectcheck = true;
                    this.BeginInvokeIfRequired(() =>
                    {
                        if (comPort.BaseStream is TcpSerial)
                            _connectionControl.CMB_serialport.Text = "TCP";
                        if (comPort.BaseStream is UdpSerial)
                           /* _connectionControl.CMB_serialport.Text = "UDP";
                        if (comPort.BaseStream is UdpSerialConnect)
                            _connectionControl.CMB_serialport.Text = "UDPCl";
                        if (comPort.BaseStream is SerialPort)
                        {
                            _connectionControl.CMB_serialport.Text = comPort.BaseStream.PortName;
                            _connectionControl.CMB_baudrate.Text = comPort.BaseStream.BaudRate.ToString();
                        }
                    });
                    
                    break;
                
                case "TCP":
                comPort.BaseStream = new TcpSerial();
                _connectionControl.CMB_serialport.Text = "TCP";
                break;*/
                case "UDP":
                    comPort.BaseStream = new UdpSerial();
                    //_connectionControl.CMB_serialport.Text = "UDP";
                    break;
                /*case "WS":
                     comPort.BaseStream = new WebSocket();
                     _connectionControl.CMB_serialport.Text = "WS";
                     break;
                 case "UDPCl":
                     comPort.BaseStream = new UdpSerialConnect();
                     _connectionControl.CMB_serialport.Text = "UDPCl";
                     break;
                 case "AUTO":
                     // do autoscan
                     Comms.CommsSerialScan.Scan(true);
                     DateTime deadline = DateTime.Now.AddSeconds(50);
                     ProgressReporterDialogue prd = new ProgressReporterDialogue();
                     prd.UpdateProgressAndStatus(-1, "Waiting for ports");
                     prd.DoWork += sender =>
                     {
                         while (Comms.CommsSerialScan.foundport == false || Comms.CommsSerialScan.run == 1)
                         {
                             System.Threading.Thread.Sleep(500);
                             Console.WriteLine("wait for port " + CommsSerialScan.foundport + " or " +
                                               CommsSerialScan.run);
                             if (sender.doWorkArgs.CancelRequested)
                             {
                                 sender.doWorkArgs.CancelAcknowledged = true;
                                 return;
                             }

                             if (DateTime.Now > deadline)
                             {
                                 _connectionControl.IsConnected(false);
                                 throw new Exception(Strings.Timeout);
                             }
                         }
                     };
                     prd.RunBackgroundOperationAsync();
                     return;*/
                default:
                    //comPort.BaseStream = new SerialPort();
                    comPort.BaseStream = new UdpSerial();
                    break;
            }
            /*
            // Tell the connection UI that we are now connected.
            this.BeginInvokeIfRequired(() =>
            {
                _connectionControl.IsConnected(true);

                // Here we want to reset the connection stats counter etc.
                this.ResetConnectionStats();
            });
            */
            comPort.MAV.cs.ResetInternals();

            //cleanup any log being played
            comPort.logreadmode = false;
            if (comPort.logplaybackfile != null)
                comPort.logplaybackfile.Close();
            comPort.logplaybackfile = null;

            try
            {
                log.Info("Set Portname");
                // set port, then options
                if (portname.ToLower() != "preset")
                    comPort.BaseStream.PortName = portname;

                log.Info("Set Baudrate");
                try
                {
                    if (baud != "" && baud != "0" && baud.IsNumber())
                        comPort.BaseStream.BaudRate = int.Parse(baud);
                }
                catch (Exception exp)
                {
                    log.Error(exp);
                }

                // prevent serialreader from doing anything
                comPort.giveComport = true;

                log.Info("About to do dtr if needed");
                // reset on connect logic.
                if (Settings.Instance.GetBoolean("CHK_resetapmonconnect") == true)
                {
                    log.Info("set dtr rts to false");
                    comPort.BaseStream.DtrEnable = false;
                    comPort.BaseStream.RtsEnable = false;

                    comPort.BaseStream.toggleDTR();
                }

                comPort.giveComport = false;

                // setup to record new logs
                try
                {
                    Directory.CreateDirectory(Settings.Instance.LogDir);
                    lock (this)
                    {
                        // create log names
                        var dt = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
                        var tlog = Settings.Instance.LogDir + Path.DirectorySeparatorChar +
                                   dt + ".tlog";
                        var rlog = Settings.Instance.LogDir + Path.DirectorySeparatorChar +
                                   dt + ".rlog";

                        // check if this logname already exists
                        int a = 1;
                        while (File.Exists(tlog))
                        {
                            Thread.Sleep(1000);
                            // create new names with a as an index
                            dt = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + "-" + a.ToString();
                            tlog = Settings.Instance.LogDir + Path.DirectorySeparatorChar +
                                   dt + ".tlog";
                            rlog = Settings.Instance.LogDir + Path.DirectorySeparatorChar +
                                   dt + ".rlog";
                        }

                        //open the logs for writing
                        comPort.logfile =
                            new BufferedStream(
                                File.Open(tlog, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
                        comPort.rawlogfile =
                            new BufferedStream(
                                File.Open(rlog, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None));
                        log.Info($"creating logfile {dt}.tlog");
                    }
                }
                catch (Exception exp2)
                {
                    log.Error(exp2);
                    CustomMessageBox.Show(Strings.Failclog);
                } // soft fail

                // reset connect time - for timeout functions
                Connecttime = DateTime.Now;

                // do the connect
                comPort.Open(false, skipconnectcheck, showui);

                if (!comPort.BaseStream.IsOpen)
                {
                    log.Info("comport is closed. existing connect");
                    try
                    {
                        //_connectionControl.IsConnected(false);
                        //UpdateConnectIcon();
                        comPort.Close();
                    }
                    catch
                    {
                    }

                    return;
                }
                /*
                // check for newer firmware
                if (showui)
                    Task.Run(() =>
                    {
                        try
                        {
                            string[] fields1 = comPort.MAV.VersionString.Split(' ');

                            var softwares = APFirmware.GetReleaseNewest(APFirmware.RELEASE_TYPES.OFFICIAL);

                            foreach (var item in softwares)
                            {
                                // check primare firmware type. ie arudplane, arducopter
                                if (fields1[0].ToLower().Contains(item.VehicleType.ToLower()))
                                {
                                    Version ver1 = VersionDetection.GetVersion(comPort.MAV.VersionString);
                                    Version ver2 = item.MavFirmwareVersion;

                                    if (ver2 > ver1)
                                    {
                                        /*Common.MessageShowAgain(Strings.NewFirmware + "-" + item.VehicleType + " " + ver2,
                                            Strings.NewFirmwareA + item.VehicleType + " " + ver2 + Strings.Pleaseup +
                                            "[link;https://discuss.ardupilot.org/tags/stable-release;Release Notes]");
                break;
                                    }

                                    // check the first hit only
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Error(ex);
                        }
                    });

                this.BeginInvokeIfRequired(() =>
                {
                    _connectionControl.UpdateSysIDS();

                    FlightData.CheckBatteryShow();

                    // save the baudrate for this port
                    Settings.Instance[_connectionControl.CMB_serialport.Text.Replace(" ","_") + "_BAUD"] =
                        _connectionControl.CMB_baudrate.Text;

                    this.Text = titlebar + " " + comPort.MAV.VersionString;

                    // refresh config window if needed
                    if (MyView.current != null && showui)
                    {
                        if (MyView.current.Name == "HWConfig")
                            MyView.ShowScreen("HWConfig");
                        if (MyView.current.Name == "SWConfig")
                            MyView.ShowScreen("SWConfig");
                    }

                    // load wps on connect option.
                    if (Settings.Instance.GetBoolean("loadwpsonconnect") == true && showui)
                    {
                        // only do it if we are connected.
                        if (comPort.BaseStream.IsOpen)
                        {
                            MenuFlightPlanner_Click(null, null);
                            FlightPlanner.BUT_read_Click(null, null);
                        }
                    }

                    // get any rallypoints
                    if (MainV2.comPort.MAV.param.ContainsKey("RALLY_TOTAL") &&
                        int.Parse(MainV2.comPort.MAV.param["RALLY_TOTAL"].ToString()) > 0 && showui)
                    {
                        try
                        {
                            FlightPlanner.getRallyPointsToolStripMenuItem_Click(null, null);

                            double maxdist = 0;

                            foreach (var rally in comPort.MAV.rallypoints)
                            {
                                foreach (var rally1 in comPort.MAV.rallypoints)
                                {
                                    var pnt1 = new PointLatLngAlt(rally.Value.y / 10000000.0f, rally.Value.x / 10000000.0f);
                                    var pnt2 = new PointLatLngAlt(rally1.Value.y / 10000000.0f,
                                        rally1.Value.x / 10000000.0f);

                                    var dist = pnt1.GetDistance(pnt2);

                                    maxdist = Math.Max(maxdist, dist);
                                }
                            }

                            if (comPort.MAV.param.ContainsKey("RALLY_LIMIT_KM") &&
                                (maxdist / 1000.0) > (float)comPort.MAV.param["RALLY_LIMIT_KM"])
                            {
                                CustomMessageBox.Show(Strings.Warningrallypointdistance + " " +
                                                      (maxdist / 1000.0).ToString("0.00") + " > " +
                                                      (float)comPort.MAV.param["RALLY_LIMIT_KM"]);
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Warn(ex);
                        }
                    }

                    // get any fences
                    if (MainV2.comPort.MAV.param.ContainsKey("FENCE_TOTAL") &&
                        int.Parse(MainV2.comPort.MAV.param["FENCE_TOTAL"].ToString()) > 1 &&
                        MainV2.comPort.MAV.param.ContainsKey("FENCE_ACTION") && showui)
                    {
                        try
                        {
                            FlightPlanner.GeoFencedownloadToolStripMenuItem_Click(null, null);
                        }
                        catch (Exception ex)
                        {
                            log.Warn(ex);
                        }
                    }

                    //Add HUD custom items source
                    HUD.Custom.src = MainV2.comPort.MAV.cs;

                    // set connected icon
                    this.MenuConnect.Image = displayicons.disconnect;
                });*/
            }
            catch (Exception ex)
            {
                log.Warn(ex);
                try
                {
                    //_connectionControl.IsConnected(false);
                    //UpdateConnectIcon();
                    comPort.Close();
                }
                catch (Exception ex2)
                {
                    log.Warn(ex2);
                }

                CustomMessageBox.Show($"Can not establish a connection\n\n{ex.Message}");
                return;
            }
        }
        #endregion

        #region LoadTelemetryCommand
        public ICommand LoadTelemetryCommand { get; }

        private bool CanLoadTelemetryCommandExecute(object p)
        {
            if (comPort.BaseStream.IsOpen) 
                return true;
            return false;
        }
        private void OnLoadTelemetryCommandExecuted(object p)
        {
            SerialReader();         
        }

        private async void SerialReader()
        {
            if (serialThread == true)
                return;
            serialThread = true;

            SerialThreadrunner.Reset();

            int minbytes = 10;

            int altwarningmax = 0;

            bool armedstatus = false;

            string lastmessagehigh = "";

            DateTime speechcustomtime = DateTime.Now;

            DateTime speechlowspeedtime = DateTime.Now;

            DateTime linkqualitytime = DateTime.Now;

            while (serialThread)
            {
                try
                {
                    await Task.Delay(1).ConfigureAwait(false); // was 5

                    try
                    {
                        TelemetryPacket = comPort.MAV.cs.yaw.ToString();
                        /*
                        if (ConfigTerminal.comPort is MAVLinkSerialPort)
                        {
                        }
                        else
                        {
                            if (ConfigTerminal.comPort != null && ConfigTerminal.comPort.BaseStream.IsOpen)
                                continue;
                        }*/
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex);
                    }
                    /*
                    // update connect/disconnect button and info stats
                    try
                    {
                        UpdateConnectIcon();
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex);
                    }
                    */
                    // 30 seconds interval speech options
                    if (speechEnabled() && speechEngine != null && (DateTime.Now - speechcustomtime).TotalSeconds > 30 &&
                        (comPort.logreadmode || comPort.BaseStream.IsOpen))
                    {
                        if (speechEngine.IsReady)
                        {
                            if (Settings.Instance.GetBoolean("speechcustomenabled"))
                            {
                                speechEngine.SpeakAsync(Common.speechConversion(comPort.MAV,
                                    "" + Settings.Instance["speechcustom"]));
                            }

                            speechcustomtime = DateTime.Now;
                        }

                        // speech for battery alerts
                        //speechbatteryvolt
                        float warnvolt = Settings.Instance.GetFloat("speechbatteryvolt");
                        float warnpercent = Settings.Instance.GetFloat("speechbatterypercent");

                        if (Settings.Instance.GetBoolean("speechbatteryenabled") == true &&
                            comPort.MAV.cs.battery_voltage <= warnvolt &&
                            comPort.MAV.cs.battery_voltage >= 5.0)
                        {
                            if (speechEngine.IsReady)
                            {
                                speechEngine.SpeakAsync(Common.speechConversion(comPort.MAV,
                                    "" + Settings.Instance["speechbattery"]));
                            }
                        }
                        else if (Settings.Instance.GetBoolean("speechbatteryenabled") == true &&
                                 (comPort.MAV.cs.battery_remaining) < warnpercent &&
                        comPort.MAV.cs.battery_voltage >= 5.0 &&
                                 comPort.MAV.cs.battery_remaining != 0.0)
                        {
                            if (speechEngine.IsReady)
                            {
                                speechEngine.SpeakAsync(
                                    Common.speechConversion(comPort.MAV,
                                        "" + Settings.Instance["speechbattery"]));
                            }
                        }
                    }

                    // speech for airspeed alerts
                    if (speechEnabled() && speechEngine != null && (DateTime.Now - speechlowspeedtime).TotalSeconds > 10 &&
                        (comPort.logreadmode || comPort.BaseStream.IsOpen))
                    {
                        if (Settings.Instance.GetBoolean("speechlowspeedenabled") == true &&
                            comPort.MAV.cs.armed)
                        {
                            float warngroundspeed = Settings.Instance.GetFloat("speechlowgroundspeedtrigger");
                            float warnairspeed = Settings.Instance.GetFloat("speechlowairspeedtrigger");

                            if (comPort.MAV.cs.airspeed < warnairspeed)
                            {
                                if (speechEngine.IsReady)
                                {
                                    speechEngine.SpeakAsync(
                                        Common.speechConversion(comPort.MAV,
                                            "" + Settings.Instance["speechlowairspeed"]));
                                    speechlowspeedtime = DateTime.Now;
                                }
                            }
                            else if (comPort.MAV.cs.groundspeed < warngroundspeed)
                            {
                                if (speechEngine.IsReady)
                                {
                                    speechEngine.SpeakAsync(
                                        Common.speechConversion(comPort.MAV,
                                            "" + Settings.Instance["speechlowgroundspeed"]));
                                    speechlowspeedtime = DateTime.Now;
                                }
                            }
                            else
                            {
                                speechlowspeedtime = DateTime.Now;
                            }
                        }
                    }

                    // speech altitude warning - message high warning
                    if (speechEnabled() && speechEngine != null &&
                        (comPort.logreadmode || comPort.BaseStream.IsOpen))
                    {
                        float warnalt = float.MaxValue;
                        if (Settings.Instance.ContainsKey("speechaltheight"))
                        {
                            warnalt = Settings.Instance.GetFloat("speechaltheight");
                        }

                        try
                        {
                            altwarningmax = (int)Math.Max(comPort.MAV.cs.alt, altwarningmax);

                            if (Settings.Instance.GetBoolean("speechaltenabled") == true &&
                            comPort.MAV.cs.alt != 0.00 &&
                                (comPort.MAV.cs.alt <= warnalt) && comPort.MAV.cs.armed)
                            {
                                if (altwarningmax > warnalt)
                                {
                                    if (speechEngine.IsReady)
                                        speechEngine.SpeakAsync(
                                            Common.speechConversion(comPort.MAV,
                                                "" + Settings.Instance["speechalt"]));
                                }
                            }
                        }
                        catch
                        {
                        } // silent fail


                        try
                        {
                            // say the latest high priority message
                            if (speechEngine.IsReady &&
                                lastmessagehigh != comPort.MAV.cs.messageHigh &&
                                comPort.MAV.cs.messageHigh != null)
                            {
                                if (!comPort.MAV.cs.messageHigh.StartsWith("PX4v2 "))
                                {
                                    speechEngine.SpeakAsync(comPort.MAV.cs.messageHigh);
                                    lastmessagehigh = comPort.MAV.cs.messageHigh;
                                }
                            }
                        }
                        catch
                        {
                        }
                    }

                    // not doing anything
                    if (!comPort.logreadmode && !comPort.BaseStream.IsOpen)
                    {
                        altwarningmax = 0;
                    }

                    // attenuate the link qualty over time
                    if ((DateTime.Now - comPort.MAV.lastvalidpacket).TotalSeconds >= 1)
                    {
                        if (linkqualitytime.Second != DateTime.Now.Second)
                        {
                            comPort.MAV.cs.linkqualitygcs =
                                (ushort)(comPort.MAV.cs.linkqualitygcs * 0.8f);
                            linkqualitytime = DateTime.Now;
                            /*
                            // force redraw if there are no other packets are being read
                            this.BeginInvokeIfRequired(
                                (Action)
                                delegate { GCSViews.FlightData.myhud.Invalidate(); });*/
                        }
                    }

                    // data loss warning - wait min of 3 seconds, ignore first 30 seconds of connect, repeat at 5 seconds interval
                    if ((DateTime.Now - comPort.MAV.lastvalidpacket).TotalSeconds > 3
                        && (DateTime.Now - connecttime).TotalSeconds > 30
                        && (DateTime.Now - nodatawarning).TotalSeconds > 5
                        && (comPort.logreadmode || comPort.BaseStream.IsOpen)
                        && comPort.MAV.cs.armed)
                    {
                        var msg = "WARNING No Data for " + (int)(DateTime.Now - comPort.MAV.lastvalidpacket).TotalSeconds + " Seconds";
                        comPort.MAV.cs.messageHigh = msg;
                        if (speechEnabled() && speechEngine != null)
                        {
                            if (speechEngine.IsReady)
                            {
                                speechEngine.SpeakAsync(msg);
                                nodatawarning = DateTime.Now;
                            }
                        }
                    }

                    // get home point on armed status change.
                    if (armedstatus != comPort.MAV.cs.armed && comPort.BaseStream.IsOpen)
                    {
                        armedstatus = comPort.MAV.cs.armed;
                        // status just changed to armed
                        if (comPort.MAV.cs.armed == true &&
                            comPort.MAV.apname != MAVLink.MAV_AUTOPILOT.INVALID &&
                            comPort.MAV.aptype != MAVLink.MAV_TYPE.GIMBAL)
                        {
                            System.Threading.ThreadPool.QueueUserWorkItem(state =>
                            {
                                Thread.CurrentThread.Name = "Arm State change";
                                try
                                {
                                    while (comPort.giveComport == true)
                                        Thread.Sleep(100);

                                    comPort.MAV.cs.HomeLocation = new PointLatLngAlt(comPort.getWP(0));
                                    /*if (MyView.current != null && MyView.current.Name == "FlightPlanner")
                                    {
                                        // update home if we are on flight data tab
                                        this.BeginInvokeIfRequired((Action)delegate { FlightPlanner.updateHome(); });
                                    }*/
                                }
                                catch
                                {
                                    // dont hang this loop
                                    /*this.BeginInvokeIfRequired(
                                        (Action)
                                        delegate
                                        {
                                            CustomMessageBox.Show("Failed to update home location (" +
                                                                  comPort.MAV.sysid + ")");
                                        });*/
                                }
                            });
                        }

                        if (speechEnable && speechEngine != null)
                        {
                            if (Settings.Instance.GetBoolean("speecharmenabled"))
                            {
                                string speech = armedstatus
                                    ? Settings.Instance["speecharm"]
                                    : Settings.Instance["speechdisarm"];
                                if (!string.IsNullOrEmpty(speech))
                                {
                                    speechEngine.SpeakAsync(
                                        Common.speechConversion(comPort.MAV, speech));
                                }
                            }
                        }
                    }

                    if (comPort.MAV.param.TotalReceived < comPort.MAV.param.TotalReported)
                    {
                        if (comPort.MAV.param.TotalReported > 0 && comPort.BaseStream.IsOpen)
                        {
                            /*this.BeginInvokeIfRequired(() =>
                            {
                                try
                                {
                                    instance.status1.Percent =
                                        (comPort.MAV.param.TotalReceived / (double)comPort.MAV.param.TotalReported) *
                                        100.0;
                                }
                                catch (Exception e)
                                {
                                    log.Error(e);
                                }
                            });*/
                        }
                    }

                    // send a hb every seconds from gcs to ap
                    if (heatbeatSend.Second != DateTime.Now.Second)
                    {
                        MAVLink.mavlink_heartbeat_t htb = new MAVLink.mavlink_heartbeat_t()
                        {
                            type = (byte)MAVLink.MAV_TYPE.GCS,
                            autopilot = (byte)MAVLink.MAV_AUTOPILOT.INVALID,
                            mavlink_version = 3 // MAVLink.MAVLINK_VERSION
                        };

                        // enumerate each link
                        foreach (var port in Comports.ToArray())
                        {
                            if (!port.BaseStream.IsOpen)
                                continue;

                            // poll for params at heartbeat interval - primary mav on this port only
                            if (!port.giveComport)
                            {
                                try
                                {
                                    // poll only when not armed
                                    if (!port.MAV.cs.armed && DateTime.Now > connecttime.AddSeconds(60))
                                    {
                                        port.getParamPoll();
                                        port.getParamPoll();
                                    }
                                }
                                catch
                                {
                                }
                            }

                            // there are 3 hb types we can send, mavlink1, mavlink2 signed and unsigned
                            bool sentsigned = false;
                            bool sentmavlink1 = false;
                            bool sentmavlink2 = false;

                            // enumerate each mav
                            foreach (var MAV in port.MAVlist)
                            {
                                try
                                {
                                    // poll for version if we dont have it - every mav every port
                                    if (!port.giveComport && MAV.cs.capabilities == 0 &&
                                        (DateTime.Now.Second % 20) == 0 && MAV.cs.version < new Version(0, 1))
                                        port.getVersion(MAV.sysid, MAV.compid, false);

                                    // are we talking to a mavlink2 device
                                    if (MAV.mavlinkv2)
                                    {
                                        // is signing enabled
                                        if (MAV.signing)
                                        {
                                            // check if we have already sent
                                            if (sentsigned)
                                                continue;
                                            sentsigned = true;
                                        }
                                        else
                                        {
                                            // check if we have already sent
                                            if (sentmavlink2)
                                                continue;
                                            sentmavlink2 = true;
                                        }
                                    }
                                    else
                                    {
                                        // check if we have already sent
                                        if (sentmavlink1)
                                            continue;
                                        sentmavlink1 = true;
                                    }

                                    port.sendPacket(htb, MAV.sysid, MAV.compid);
                                }
                                catch (Exception ex)
                                {
                                    log.Error(ex);
                                    // close the bad port
                                    try
                                    {
                                        port.Close();
                                    }
                                    catch
                                    {
                                    }

                                    // refresh the screen if needed
                                    if (port == comPort)
                                    {
                                        /*// refresh config window if needed
                                        if (MyView.current != null)
                                        {
                                            this.BeginInvoke((MethodInvoker)delegate ()
                                            {
                                                if (MyView.current.Name == "HWConfig")
                                                    MyView.ShowScreen("HWConfig");
                                                if (MyView.current.Name == "SWConfig")
                                                    MyView.ShowScreen("SWConfig");
                                            });
                                        }*/
                                    }
                                }
                            }
                        }

                        heatbeatSend = DateTime.Now;
                    }

                    // if not connected or busy, sleep and loop
                    if (!comPort.BaseStream.IsOpen || comPort.giveComport == true)
                    {
                        if (!comPort.BaseStream.IsOpen)
                        {
                            // check if other ports are still open
                            foreach (var port in Comports)
                            {
                                if (port.BaseStream.IsOpen)
                                {
                                    Console.WriteLine("Main comport shut, swapping to other mav");
                                    comPort = port;
                                    break;
                                }
                            }
                        }

                        await Task.Delay(100).ConfigureAwait(false);
                    }

                    // read the interfaces
                    foreach (var port in Comports.ToArray())
                    {
                        if (!port.BaseStream.IsOpen)
                        {
                            // skip primary interface
                            if (port == comPort)
                                continue;

                            // modify array and drop out
                            Comports.Remove(port);
                            port.Dispose();
                            break;
                        }

                        DateTime startread = DateTime.Now;

                        // must be open, we have bytes, we are not yielding the port,
                        // the thread is meant to be running and we only spend 1 seconds max in this read loop
                        while (port.BaseStream.IsOpen && port.BaseStream.BytesToRead > minbytes &&
                               port.giveComport == false && serialThread && startread.AddSeconds(1) > DateTime.Now)
                        {
                            try
                            {
                                await port.readPacketAsync().ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                log.Error(ex);
                            }
                        }

                        // update currentstate of sysids on the port
                        foreach (var MAV in port.MAVlist)
                        {
                            try
                            {
                                MAV.cs.UpdateCurrentSettings(null, false, port, MAV);
                            }
                            catch (Exception ex)
                            {
                                log.Error(ex);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Tracking.AddException(e);
                    log.Error("Serial Reader fail :" + e.ToString());
                    try
                    {
                        comPort.Close();
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex);
                    }
                }
            }

            Console.WriteLine("SerialReader Done");
            SerialThreadrunner.Set();
        }

        #endregion

        #endregion
        //-------------------------------------------------------------------------

        public MainWindowViewModel()
        {

            #region Команды 

            ConnectionUdpCommand = new LambdaCommand(OnConnectionUdpCommandExecuted, CanConnectionUdpCommandExecute);
            LoadTelemetryCommand = new LambdaCommand(OnLoadTelemetryCommandExecuted, CanLoadTelemetryCommandExecute);
            #endregion

            comPort.BaseStream = new SerialPort();

            Points.Add(new PointItem
            {
                Name = "Steinbake Leitdamm",
                Location = new Location(53.51217, 8.16603)
            });

            Points.Add(new PointItem
            {
                Name = "Buhne 2",
                Location = new Location(53.50926, 8.15815)
            });

            Points.Add(new PointItem
            {
                Name = "Buhne 4",
                Location = new Location(53.50468, 8.15343)
            });

            Points.Add(new PointItem
            {
                Name = "Buhne 6",
                Location = new Location(53.50092, 8.15267)
            });

            Points.Add(new PointItem
            {
                Name = "Buhne 8",
                Location = new Location(53.49871, 8.15321)
            });

            Points.Add(new PointItem
            {
                Name = "Buhne 10",
                Location = new Location(53.49350, 8.15563)
            });

            Pushpins.Add(new PointItem
            {
                Name = "WHV - Eckwarderhörne",
                Location = new Location(53.5495, 8.1877)
            });

            Pushpins.Add(new PointItem
            {
                Name = "JadeWeserPort",
                Location = new Location(53.5914, 8.14)
            });

            Pushpins.Add(new PointItem
            {
                Name = "Kurhaus Dangast",
                Location = new Location(53.447, 8.1114)
            });

            Pushpins.Add(new PointItem
            {
                Name = "Eckwarderhörne",
                Location = new Location(53.5207, 8.2323)
            });

            Polylines.Add(new PolylineItem
            {
                Locations = LocationCollection.Parse("53.5140,8.1451 53.5123,8.1506 53.5156,8.1623 53.5276,8.1757 53.5491,8.1852 53.5495,8.1877 53.5426,8.1993 53.5184,8.2219 53.5182,8.2386 53.5195,8.2387")
            });

            Polylines.Add(new PolylineItem
            {
                Locations = LocationCollection.Parse("53.5978,8.1212 53.6018,8.1494 53.5859,8.1554 53.5852,8.1531 53.5841,8.1539 53.5802,8.1392 53.5826,8.1309 53.5867,8.1317 53.5978,8.1212")
            });
        }
    }
}
