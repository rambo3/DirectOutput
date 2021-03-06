﻿using DirectOutput.Cab.Schedules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;


namespace DirectOutput.Cab.Out.LW
{
    /// <summary>
    /// The LedWiz is a easy to use outputcontroller with 32 outputs which all support 49 <a target="_blank" href="https://en.wikipedia.org/wiki/Pulse-width_modulation">pwm</a> levels with a PWM frequency of approx. 50hz. The LedWiz is able to drive leds and smaller loads directly, but will require some kind of booster for power hungery gadgets like big contactors or motors.
    ///
    /// \image html LedWizboard.jpg
    /// 
    /// The DirectOutput framework does fully support the LedWiz and can control up to 16 LedWiz units. 
    /// 
    /// The framework can automatically detect connected LedWiz units and configure them for use with the framework. 
    /// 
    /// The LedWiz is made by <a href="http://groovygamegear.com/">GroovyGameGear</a> and can by ordered directly on GroovyGamegears website, but also from some other vendors.
    /// 
    /// This unit was the first output controller which was widely used in the virtual pinball community and was the unit for which the legacy vbscript solution was developed. The DirectOutput framework replaces the vbscript solution, but can reuse the ini files which were used for the configuration of the tables. Please read \ref ledcontrolfiles for more information.
    /// 
    /// The current implementation of the LedWiz driver uses a separate thread for every ledwiz connected to the system to ensure max. performance.
    /// 
    /// \image html LedWizLogo.jpg
    /// 
    /// </summary>
    public class LedWiz : OutputControllerBase, IOutputController, IDisposable
    {
        #region Number


        private object NumberUpdateLocker = new object();
        private int _Number = -1;

        /// <summary>
        /// Gets or sets the number of the LedWiz.<br />
        /// The number of the LedWiz must be unique.<br />
        /// Setting changes the Name property, if it is blank or if the Name coresponds to LedWiz {Number}.
        /// </summary>
        /// <value>
        /// The unique number of the LedWiz (Range 1-16).
        /// </value>
        /// <exception cref="System.Exception">
        /// LedWiz Numbers must be between 1-16. The supplied number {0} is out of range.
        /// </exception>
        public int Number
        {
            get { return _Number; }
            set
            {
                if (!value.IsBetween(1, 16))
                {
                    throw new Exception("LedWiz Numbers must be between 1-16. The supplied number {0} is out of range.".Build(value));
                }
                lock (NumberUpdateLocker)
                {
                    if (_Number != value)
                    {

                        if (Name.IsNullOrWhiteSpace() || Name == "LedWiz {0:00}".Build(_Number))
                        {
                            Name = "LedWiz {0:00}".Build(value);
                        }

                        _Number = value;

                    }
                }
            }
        }

        #endregion


        private int _MinCommandIntervalMs = 1;
        private bool MinCommandIntervalMsSet = false;
        /// <summary>
        /// Gets or sets the mininimal interval between command in miliseconds (Default: 1ms).
        /// Depending on the mainboard, usb hardware on the board, usb drivers and other factors the LedWiz does sometime tend to loose or misunderstand commands received if the are sent in to short intervals.
        /// The settings allows to increase the default minmal interval between commands from 1ms to a higher value. Higher values will make problems less likely, but decreases the number of possible updates of the ledwiz outputs in a given time frame.
        /// It is recommended to use the default interval of 1 ms and only to increase this interval if problems occur (Toys which are sometimes not reacting, random knocks of replay knocker or solenoids).
        /// </summary>
        /// <value>
        /// The mininimal interval between command in miliseconds (Default: 1ms).
        /// Depending on the mainboard, usb hardware on the board, usb drivers and other factors the LedWiz does sometime tend to loose or misunderstand commands received if the are sent in to short intervals.
        /// The settings allows to increase the default minmal interval between commands from 1ms to a higher value. Higher values will make problems less likely, but decreases the number of possible updates of the ledwiz outputs in a given time frame.
        /// It is recommended to use the default interval of 1 ms and only to increase this interval if problems occur (Toys which are sometimes not reacting, random knocks of replay knocker or solenoids).
        /// </value>
        public int MinCommandIntervalMs
        {
            get { return _MinCommandIntervalMs; }
            set
            {
                _MinCommandIntervalMs = value.Limit(0, 1000);
                MinCommandIntervalMsSet = true;
            }
        }




        #region IOutputcontroller implementation
        /// <summary>
        /// Updates all outputs of the LedWiz output controller.<br/>
        /// Signals the workerthread that all pending updates for the ledwiz should be sent to the physical LedWiz unit.
        /// </summary>
        public override void Update()
        {
            LedWizUnits[Number].TriggerLedWizUpdaterThread();
        }


        /// <summary>
        /// Initializes the LedWiz object.<br />
        /// This method does also start the workerthread which does the actual update work when Update() is called.<br />
        /// This method should only be called once. Subsequent calls have no effect.
        /// </summary>
        /// <param name="Cabinet">The Cabinet object which is using the LedWiz instance.</param>
        public override void Init(Cabinet Cabinet)
        {
            Log.Debug("Initializing LedWiz Nr. {0:00}".Build(Number));
            AddOutputs();
            if (!MinCommandIntervalMsSet && Cabinet.Owner.ConfigurationSettings.ContainsKey("LedWizDefaultMinCommandIntervalMs") && Cabinet.Owner.ConfigurationSettings["LedWizDefaultMinCommandIntervalMs"] is int)
            {
                MinCommandIntervalMs = (int)Cabinet.Owner.ConfigurationSettings["LedWizDefaultMinCommandIntervalMs"];
            }

            LedWizUnits[Number].Init(Cabinet, MinCommandIntervalMs);

            Log.Write("LedWiz Nr. {0:00} initialized and updater thread initialized.".Build(Number));

        }

        /// <summary>
        /// Finishes the LedWiz object.<br/>
        /// Finish does also terminate the workerthread for updates.
        /// </summary>
        public override void Finish()
        {
            Log.Debug("Finishing LedWiz Nr. {0:00}".Build(Number));
            LedWizUnits[Number].Finish();
            LedWizUnits[Number].ShutdownLighting();
            Log.Write("LedWiz Nr. {0:00} finished and updater thread stopped.".Build(Number));

        }
        #endregion



        #region Outputs



        /// <summary>
        /// Adds the outputs for a LedWiz.<br/>
        /// A LedWiz has 32 outputs numbered from 1 to 32. This method adds LedWizOutput objects for all outputs to the list.
        /// </summary>
        private void AddOutputs()
        {
            for (int i = 1; i <= 32; i++)
            {
                if (!Outputs.Any(x => ((LedWizOutput)x).LedWizOutputNumber == i))
                {
                    Outputs.Add(new LedWizOutput(i) { Name = "{0}.{1:00}".Build(Name, i) });
                }
            }
        }


        /// <summary>
        /// This method is called whenever the value of a output in the Outputs property changes its value.<br />
        /// Updates the internal arrays holding the states of the LedWiz outputs.
        /// </summary>
        /// <param name="Output">The output which has changed.</param>
        /// <exception cref="System.Exception">
        /// The OutputValueChanged event handler for LedWiz {0:00} has been called by a sender which is not a LedWizOutput.<br/>
        /// or<br/>
        /// LedWiz output numbers must be in the range of 1-32. The supplied output number {0} is out of range.
        /// </exception>
        protected override void OnOutputValueChanged(IOutput Output)
        {
            if (!(Output is LedWizOutput))
            {
                throw new Exception("The OutputValueChanged event handler for LedWiz {0:00} has been called by a sender which is not a LedWizOutput.".Build(Number));
            }
            LedWizOutput LWO = (LedWizOutput)Output;

            if (!LWO.LedWizOutputNumber.IsBetween(1, 32))
            {
                throw new Exception("LedWiz output numbers must be in the range of 1-32. The supplied output number {0} is out of range.".Build(LWO.LedWizOutputNumber));
            }

            LedWizUnit S = LedWizUnits[this.Number];
            //S.UpdateValue(LWO);

            //uses ledwizoutput instead of standard output, need to mirror ledwizoutputnumber
            //note, compensate for id [1-16] not being zero-based
            IOutput recalculatedOutput = ScheduledSettings.Instance.getnewrecalculatedOutput(Output, 1, Number-1);
            LWO.Value = recalculatedOutput.Value;

            S.UpdateValue(LWO);
        }



        ///// <summary>
        ///// Handles the OutputValueChanged event of the base class.<br/>
        ///// Updates the internal arrays holding the states of the LedWiz outputs. 
        ///// </summary>
        ///// <param name="sender">The source of the event (must be a LedWizOutput).</param>
        ///// <param name="e">The <see cref="OutputEventArgs" /> instance containing the event data.</param>
        ///// <exception cref="System.Exception">The OutputValueChanged event handler for LedWiz {0:00} has been called by a sender which is not a LedWizOutput. or LedWiz output numbers must be in the range of 1-32. The supplied output number {0} is out of range.</exception>
        //private void OutputValueChanged(object sender, OutputEventArgs e)
        //{

        //    if (!(e.Output is LedWizOutput))
        //    {
        //        throw new Exception("The OutputValueChanged event handler for LedWiz {0:00} has been called by a sender which is not a LedWizOutput.".Build(Number));
        //    }
        //    LedWizOutput LWO = (LedWizOutput)e.Output;

        //    if (!LWO.LedWizOutputNumber.IsBetween(1, 32))
        //    {
        //        throw new Exception("LedWiz output numbers must be in the range of 1-32. The supplied output number {0} is out of range.".Build(LWO.LedWizOutputNumber));
        //    }

        //    LedWizUnit S = LedWizUnits[this.Number];
        //    S.UpdateValue(LWO);
        //}
        #endregion



        #region LEDWIZ Static Private Methods & Properties
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct LWZDEVICELIST
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = LWZ_MAX_DEVICES)]
            public uint[] handles;
            public int numdevices;
        }

        [DllImport("LEDWiz", CallingConvention = CallingConvention.Cdecl)]
        private extern static void LWZ_SBA(uint device, uint bank0, uint bank1, uint bank2, uint bank3, uint globalPulseSpeed);

        [DllImport("LEDWiz", CallingConvention = CallingConvention.Cdecl)]
        private extern static void LWZ_PBA(uint device, uint brightness);

        [DllImport("LEDWiz", CallingConvention = CallingConvention.Cdecl)]
        private extern static void LWZ_REGISTER(uint h, uint hwnd);

        [DllImport("LEDWiz", CallingConvention = CallingConvention.Cdecl)]
        private extern static void LWZ_SET_NOTIFY(MulticastDelegate notifyProc, uint list);

        private delegate void NotifyDelegate(int reason, uint newDevice);

        private static IntPtr MainWindow = IntPtr.Zero;

        private static LWZDEVICELIST deviceList;

        private const int LWZ_MAX_DEVICES = 16;
        private const int LWZ_ADD = 1;
        private const int LWZ_DELETE = 2;

        private enum AutoPulseMode : int
        {
            RampUpRampDown = 129,				// /\/\
            OnOff = 130,						// _|-|_|-|
            OnRampDown = 131,					// -\|-\
            RampUpDown = 132					// /-|/-
        }



        private static void Notify(int reason, uint newDevice)
        {
            //TODO: Ledwizverhalten bei add und remove im laufenden betrieb prüfen
            if (reason == LWZ_ADD)
            {
                LWZ_REGISTER(newDevice, (uint)MainWindow.ToInt32());
            }
            if (reason == LWZ_DELETE)
            {
            }
        }



        private static int NumDevices
        {
            get { return deviceList.numdevices; }
        }

        private static uint[] DeviceHandles
        {
            get { return deviceList.handles; }
        }

        private static int StartedUp = 0;
        private static object StartupLocker = new object();
        private static void StartupLedWiz()
        {
            lock (StartupLocker)
            {
                if (StartedUp == 0)
                {
                    MainWindow = IntPtr.Zero;
                    deviceList.handles = new uint[LWZ_MAX_DEVICES] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                    deviceList.numdevices = 0;
                    try
                    {
                        NotifyDelegate del = new NotifyDelegate(Notify);

                        IntPtr ptr = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(LWZDEVICELIST)));
                        Marshal.StructureToPtr(deviceList, ptr, true);
                        LWZ_SET_NOTIFY(del, (uint)ptr.ToInt32());
                        deviceList = (LWZDEVICELIST)Marshal.PtrToStructure(ptr, typeof(LWZDEVICELIST));
                        Marshal.FreeCoTaskMem(ptr);
                        Log.Debug("Ledwiz devicelist content. Handles: {0}, Num devices: {1}".Build(string.Join(", ", deviceList.handles.Select(H => H.ToString())), deviceList.numdevices));
                    }
                    catch (Exception ex)
                    {
                        Log.Exception("Could not init LedWiz", ex);
                        throw new Exception("Could not init LedWiz", ex);
                    }

                }
                StartedUp++;
            }
        }

        private static void TerminateLedWiz()
        {
            lock (StartupLocker)
            {

                if (StartedUp > 0)
                {
                    StartedUp--;
                    if (StartedUp == 0)
                    {
                        foreach (LedWizUnit S in LedWizUnits.Values)
                        {
                            S.ShutdownLighting();
                        }
                        LWZ_SET_NOTIFY((System.MulticastDelegate)null, 0);
                    }
                }
            }
        }
        #endregion


        /// <summary>
        /// Gets the numbers of the LedWiz controllers which are connected to the system.
        /// </summary>
        /// <returns></returns>
        public static List<int> GetLedwizNumbers()
        {
            List<int> L = new List<int>();

            LedWiz LW = new LedWiz();
            for (int i = 0; i < LedWiz.NumDevices; i++)
            {
                L.Add((int)LedWiz.DeviceHandles[i]);
            }
            LW.Dispose();
            return L;
        }



        /// <summary>
        /// Finalizes an instance of the <see cref="LedWiz"/> class.
        /// </summary>
        ~LedWiz()
        {

            Dispose(false);
        }

        #region Dispose



        /// <summary>
        /// Disposes the LedWiz object.
        /// </summary>
        public void Dispose()
        {

            Dispose(true);
            GC.SuppressFinalize(this); // remove this from gc finalizer list
        }
        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (!this.disposed)
            {
                try
                {
                    Log.Debug("Disposing LedWiz instance {0:00}.".Build(Number));
                }
                catch
                {
                }
                // If disposing equals true, dispose all managed
                // and unmanaged resources.
                if (disposing)
                {
                    // Dispose managed resources.

                }

                // Call the appropriate methods to clean up
                // unmanaged resources here.
                // If disposing is false,
                // only the following code is executed.

                TerminateLedWiz();


                // Note disposing has been done.
                disposed = true;

            }
        }
        private bool disposed = false;

        #endregion



        #region Constructor


        /// <summary>
        /// Initializes the <see cref="LedWiz"/> class.
        /// </summary>
        static LedWiz()
        {
            LedWizUnits = new Dictionary<int, LedWizUnit>();
            for (int i = 1; i <= 16; i++)
            {
                LedWizUnits.Add(i, new LedWizUnit(i));
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LedWiz"/> class.
        /// </summary>
        public LedWiz()
        {
            StartupLedWiz();

            Outputs = new OutputList();


        }



        /// <summary>
        /// Initializes a new instance of the <see cref="LedWiz"/> class.
        /// </summary>
        /// <param name="Number">The number of the LedWiz (1-16).</param>
        public LedWiz(int Number)
            : this()
        {
            this.Number = Number;
        }

        #endregion






        #region Internal class for LedWiz output states and update methods

        private static Dictionary<int, LedWizUnit> LedWizUnits = new Dictionary<int, LedWizUnit>();

        private class LedWizUnit
        {
          //  private Pinball Pinball;


            //Used to convert the 0-255 range of output values to LedWiz values in the range of 0-48.
            //            private static readonly byte[] ByteToLedWizValue = { 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 9, 9, 9, 9, 9, 9, 10, 10, 10, 10, 10, 11, 11, 11, 11, 11, 12, 12, 12, 12, 12, 13, 13, 13, 13, 13, 14, 14, 14, 14, 14, 14, 15, 15, 15, 15, 15, 16, 16, 16, 16, 16, 17, 17, 17, 17, 17, 18, 18, 18, 18, 18, 19, 19, 19, 19, 19, 19, 20, 20, 20, 20, 20, 21, 21, 21, 21, 21, 22, 22, 22, 22, 22, 23, 23, 23, 23, 23, 24, 24, 24, 24, 24, 24, 25, 25, 25, 25, 25, 26, 26, 26, 26, 26, 27, 27, 27, 27, 27, 28, 28, 28, 28, 28, 29, 29, 29, 29, 29, 29, 30, 30, 30, 30, 30, 31, 31, 31, 31, 31, 32, 32, 32, 32, 32, 33, 33, 33, 33, 33, 34, 34, 34, 34, 34, 34, 35, 35, 35, 35, 35, 36, 36, 36, 36, 36, 37, 37, 37, 37, 37, 38, 38, 38, 38, 38, 39, 39, 39, 39, 39, 39, 40, 40, 40, 40, 40, 41, 41, 41, 41, 41, 42, 42, 42, 42, 42, 43, 43, 43, 43, 43, 44, 44, 44, 44, 44, 44, 45, 45, 45, 45, 45, 46, 46, 46, 46, 46, 47, 47, 47, 47, 47, 48, 48, 48, 48, 48 };

            private static readonly byte[] ByteToLedWizValue = { 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 9, 9, 9, 9, 9, 10, 10, 10, 10, 10, 10, 11, 11, 11, 11, 11, 12, 12, 12, 12, 12, 13, 13, 13, 13, 13, 13, 14, 14, 14, 14, 14, 15, 15, 15, 15, 15, 16, 16, 16, 16, 16, 16, 17, 17, 17, 17, 17, 18, 18, 18, 18, 18, 19, 19, 19, 19, 19, 20, 20, 20, 20, 20, 20, 21, 21, 21, 21, 21, 22, 22, 22, 22, 22, 23, 23, 23, 23, 23, 23, 24, 24, 24, 24, 24, 25, 25, 25, 25, 25, 26, 26, 26, 26, 26, 26, 27, 27, 27, 27, 27, 28, 28, 28, 28, 28, 29, 29, 29, 29, 29, 29, 30, 30, 30, 30, 30, 31, 31, 31, 31, 31, 32, 32, 32, 32, 32, 32, 33, 33, 33, 33, 33, 34, 34, 34, 34, 34, 35, 35, 35, 35, 35, 36, 36, 36, 36, 36, 36, 37, 37, 37, 37, 37, 38, 38, 38, 38, 38, 39, 39, 39, 39, 39, 39, 40, 40, 40, 40, 40, 41, 41, 41, 41, 41, 42, 42, 42, 42, 42, 42, 43, 43, 43, 43, 43, 44, 44, 44, 44, 44, 45, 45, 45, 45, 45, 45, 46, 46, 46, 46, 46, 47, 47, 47, 47, 47, 48, 48, 48, 48, 48, 49 };
            private const int MaxUpdateFailCount = 5;


            public int Number { get; private set; }

            public byte[] NewOutputValues = new byte[32] { 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49, 49 };
            public byte[] CurrentOuputValues = new byte[32];
            public byte[] NewAfterValueSwitches = new byte[4];
            public byte[] NewBeforeValueSwitches = new byte[4];
            public byte[] CurrentAfterValueSwitches = new byte[4];
            public byte[] CurrentBeforeValueSwitches = new byte[4];
            public bool NewValueUpdateRequired = true;
            public bool NewSwitchUpdateBeforeValueUpdateRequired = true;
            public bool NewSwitchUpdateAfterValueUpdateRequired = true;

            public bool CurrentValueUpdateRequired = true;
            public bool CurrentSwitchUpdateBeforeValueUpdateRequired = true;
            public bool CurrentSwitchUpdateAfterValueUpdateRequired = true;
            public bool UpdateRequired = true;

            public object LedWizUpdateLocker = new object();
            public object ValueChangeLocker = new object();

            public Thread LedWizUpdater;
            public bool KeepLedWizUpdaterAlive = false;
            public object LedWizUpdaterThreadLocker = new object();


            public void Init(Cabinet Cabinet, int MinCommandIntervalMs)
            {
                //this.Pinball = Cabinet.Pinball;
                //if (!Pinball.TimeSpanStatistics.Contains("LedWiz {0:00} update calls".Build(Number)))
                //{
                //    UpdateTimeStatistics = new TimeSpanStatisticsItem() { Name = "LedWiz {0:00} update calls".Build(Number), GroupName = "OutputControllers - LedWiz" };
                //    Pinball.TimeSpanStatistics.Add(UpdateTimeStatistics);
                //}
                //else
                //{
                //    UpdateTimeStatistics = Pinball.TimeSpanStatistics["LedWiz {0:00} update calls".Build(Number)];
                //}
                //if (!Pinball.TimeSpanStatistics.Contains("LedWiz {0:00} PWM updates".Build(Number)))
                //{
                //    PWMUpdateTimeStatistics = new TimeSpanStatisticsItem() { Name = "LedWiz {0:00} PWM updates".Build(Number), GroupName = "OutputControllers - LedWiz" };
                //    Pinball.TimeSpanStatistics.Add(PWMUpdateTimeStatistics);
                //}
                //else
                //{
                //    PWMUpdateTimeStatistics = Pinball.TimeSpanStatistics["LedWiz {0:00} PWM updates".Build(Number)];
                //}
                //if (!Pinball.TimeSpanStatistics.Contains("LedWiz {0:00} OnOff updates".Build(Number)))
                //{
                //    OnOffUpdateTimeStatistics = new TimeSpanStatisticsItem() { Name = "LedWiz {0:00} OnOff updates".Build(Number), GroupName = "OutputControllers - LedWiz" };
                //    Pinball.TimeSpanStatistics.Add(OnOffUpdateTimeStatistics);
                //}
                //else
                //{
                //    OnOffUpdateTimeStatistics = Pinball.TimeSpanStatistics["LedWiz {0:00} OnOff updates".Build(Number)];
                //}
                this.MinCommandIntervalMs = MinCommandIntervalMs;
                StartLedWizUpdaterThread();
            }

            public void Finish()
            {

                TerminateLedWizUpdaterThread();
                ShutdownLighting();
                //this.Pinball = null;
              
            }

            public void UpdateValue(LedWizOutput LedWizOutput)
            {
                byte V = ByteToLedWizValue[LedWizOutput.Value];
                bool S = (V != 0);

                int ZeroBasedOutputNumber = LedWizOutput.LedWizOutputNumber - 1;

                int ByteNr = ZeroBasedOutputNumber >> 3;
                int BitNr = ZeroBasedOutputNumber & 7;

                byte Mask = (byte)(1 << BitNr);

                lock (ValueChangeLocker)
                {
                    if (S != ((NewAfterValueSwitches[ByteNr] & Mask) != 0))
                    {
                        //Neeed to adjust switches
                        if (S == false)
                        {
                            //Output will be turned off
                            Mask = (byte)~Mask;
                            NewAfterValueSwitches[ByteNr] &= Mask;
                            NewBeforeValueSwitches[ByteNr] &= Mask;
                            NewSwitchUpdateBeforeValueUpdateRequired = true;
                            UpdateRequired = true;
                        }
                        else
                        {
                            //Output will be turned on
                            if (V != NewOutputValues[ZeroBasedOutputNumber])
                            {
                                //Need to change value before turing on
                                NewOutputValues[ZeroBasedOutputNumber] = V;
                                NewAfterValueSwitches[ByteNr] |= Mask;
                                NewValueUpdateRequired = true;
                                NewSwitchUpdateAfterValueUpdateRequired = true;
                                UpdateRequired = true;
                            }
                            else
                            {
                                //Value is correct, only need to turn on switch
                                NewAfterValueSwitches[ByteNr] |= Mask;
                                NewBeforeValueSwitches[ByteNr] |= Mask;
                                NewSwitchUpdateBeforeValueUpdateRequired = true;
                                UpdateRequired = true;
                            }
                        }
                    }
                    else
                    {
                        if (S == true && V != NewOutputValues[ZeroBasedOutputNumber])
                        {
                            //Only need to adjust value
                            NewOutputValues[ZeroBasedOutputNumber] = V;
                            NewValueUpdateRequired = true;
                            UpdateRequired = true;
                        }

                    }
                }
            }
            public void CopyNewToCurrent()
            {
                lock (ValueChangeLocker)
                {

                    CurrentValueUpdateRequired = NewValueUpdateRequired;
                    CurrentSwitchUpdateBeforeValueUpdateRequired = NewSwitchUpdateBeforeValueUpdateRequired;
                    CurrentSwitchUpdateAfterValueUpdateRequired = NewSwitchUpdateAfterValueUpdateRequired;


                    if (NewValueUpdateRequired)
                    {
                        Array.Copy(NewOutputValues, CurrentOuputValues, NewOutputValues.Length);
                        NewValueUpdateRequired = false;
                    }
                    if (NewSwitchUpdateAfterValueUpdateRequired || NewSwitchUpdateBeforeValueUpdateRequired)
                    {
                        Array.Copy(NewAfterValueSwitches, CurrentAfterValueSwitches, NewAfterValueSwitches.Length);
                        Array.Copy(NewBeforeValueSwitches, CurrentBeforeValueSwitches, NewBeforeValueSwitches.Length);
                        Array.Copy(CurrentAfterValueSwitches, NewBeforeValueSwitches, NewAfterValueSwitches.Length);
                        NewSwitchUpdateAfterValueUpdateRequired = false;
                        NewSwitchUpdateBeforeValueUpdateRequired = false;
                    }



                }
            }

            public bool IsUpdaterThreadAlive
            {
                get
                {
                    if (LedWizUpdater != null)
                    {
                        return LedWizUpdater.IsAlive;
                    }
                    return false;
                }
            }

            public void StartLedWizUpdaterThread()
            {
                lock (LedWizUpdaterThreadLocker)
                {
                    if (!IsUpdaterThreadAlive)
                    {
                        KeepLedWizUpdaterAlive = true;
                        LedWizUpdater = new Thread(LedWizUpdaterDoIt);
                        LedWizUpdater.Name = "LedWiz {0:00} updater thread".Build(Number);
                        LedWizUpdater.Start();
                    }
                }
            }
            public void TerminateLedWizUpdaterThread()
            {
              //  lock (LedWizUpdaterThreadLocker)
              //  {
                    if (LedWizUpdater != null)
                    {
                        try
                        {
                            KeepLedWizUpdaterAlive = false;
                            TriggerLedWizUpdaterThread();
                            if (!LedWizUpdater.Join(1000))
                            {
                                LedWizUpdater.Abort();
                            }

                        }
                        catch (Exception E)
                        {
                            Log.Exception("A error occurd during termination of {0}.".Build(LedWizUpdater.Name), E);
                            throw new Exception("A error occurd during termination of {0}.".Build(LedWizUpdater.Name), E);
                        }
                        LedWizUpdater = null;
                    }
               // }
            }

            bool TriggerUpdate = false;
            public void TriggerLedWizUpdaterThread()
            {
                TriggerUpdate = true;
                lock (LedWizUpdaterThreadLocker)
                {
                    Monitor.Pulse(LedWizUpdaterThreadLocker);
                }
            }


            //TODO: Check if thread should really terminate on failed updates
            private void LedWizUpdaterDoIt()
            {
                Log.Write("Updater thread for LedWiz {0:00} started.".Build(Number));
                //Pinball.ThreadInfoList.HeartBeat("LedWiz {0:00}".Build(Number));


                int FailCnt = 0;
                while (KeepLedWizUpdaterAlive)
                {
                    try
                    {
                        if (IsPresent)
                        {
                     
                            SendLedWizUpdate();
               
                        }
                        FailCnt = 0;
                    }
                    catch (Exception E)
                    {
                        Log.Exception("A error occured when updating LedWiz Nr. {0}".Build(Number), E);
                        //Pinball.ThreadInfoList.RecordException(E);
                        FailCnt++;

                        if (FailCnt > MaxUpdateFailCount)
                        {
                            Log.Exception("More than {0} consecutive updates failed for LedWiz Nr. {1}. Updater thread will terminate.".Build(MaxUpdateFailCount, Number));
                            KeepLedWizUpdaterAlive = false;
                        }
                    }
                    //Pinball.ThreadInfoList.HeartBeat();
                    if (KeepLedWizUpdaterAlive)
                    {
                        lock (LedWizUpdaterThreadLocker)
                        {
                            while (!TriggerUpdate && KeepLedWizUpdaterAlive)
                            {
                                Monitor.Wait(LedWizUpdaterThreadLocker, 50);  // Lock is released while we’re waiting
                                //Pinball.ThreadInfoList.HeartBeat();
                            }

                        }

                    }
                    TriggerUpdate = false;
                }
                //Pinball.ThreadInfoList.ThreadTerminates();
                Log.Write("Updater thread for LedWiz {0:00} terminated.".Build(Number));
            }



            private DateTime LastUpdate = DateTime.Now;
            public int MinCommandIntervalMs = 1;
            private void UpdateDelay()
            {
                int Ms = (int)DateTime.Now.Subtract(LastUpdate).TotalMilliseconds;
                if (Ms < MinCommandIntervalMs)
                {
                    Thread.Sleep((MinCommandIntervalMs - Ms).Limit(0, MinCommandIntervalMs));
                }
                LastUpdate = DateTime.Now;
            }

            private void SendLedWizUpdate()
            {
                if (Number.IsBetween(1, 16))
                {

                    lock (LedWizUpdateLocker)
                    {
                        lock (ValueChangeLocker)
                        {
                            if (!UpdateRequired) return;


                            CopyNewToCurrent();

                            UpdateRequired = false;
                        }


                        if (CurrentValueUpdateRequired)
                        {
                            if (CurrentSwitchUpdateBeforeValueUpdateRequired)
                            {
                                UpdateDelay();
                          
                                SBA(CurrentBeforeValueSwitches[0], CurrentBeforeValueSwitches[1], CurrentBeforeValueSwitches[2], CurrentBeforeValueSwitches[3], 2);
                       
                            }

                            UpdateDelay();
                     
                            PBA(CurrentOuputValues);
                        
                        }
                        if (CurrentSwitchUpdateAfterValueUpdateRequired || (CurrentSwitchUpdateBeforeValueUpdateRequired && !NewValueUpdateRequired))
                        {
                            UpdateDelay();
                         
                            SBA(CurrentAfterValueSwitches[0], CurrentAfterValueSwitches[1], CurrentAfterValueSwitches[2], CurrentAfterValueSwitches[3], 2);
                       
                        }

                    }
                }
            }


            public void ShutdownLighting()
            {
                SBA(0, 0, 0, 0, 2);
            }


            private void SBA(uint bank1, uint bank2, uint bank3, uint bank4, uint globalPulseSpeed)
            {
                if (IsPresent)
                {
                    LWZ_SBA((uint)Number, bank1, bank2, bank3, bank4, globalPulseSpeed);
                }
            }

            private void PBA(byte[] val)
            {
                if (IsPresent)
                {
                    IntPtr ptr = Marshal.AllocCoTaskMem(val.Length);
                    Marshal.Copy(val, 0, ptr, val.Length);
                    LWZ_PBA((uint)Number, (uint)ptr.ToInt32());
                    Marshal.FreeCoTaskMem(ptr);
                }
            }

            private bool IsPresent
            {
                get
                {
                    if (!Number.IsBetween(1, 16)) return false;
                    return DeviceHandles.Any(x => x == Number);
                }
            }


            public LedWizUnit(int Number)
            {
                this.Number = Number;



            }

        }



        #endregion




    }
}
