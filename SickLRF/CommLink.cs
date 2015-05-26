//-----------------------------------------------------------------------
//  This file is part of Microsoft Robotics Developer Studio Code Samples.
//
//  Copyright (C) Microsoft Corporation.  All rights reserved.
//
//  $File: CommLink.cs $ $Revision: 10 $
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Xml;

using Microsoft.Ccr.Core;
using Microsoft.Dss.Core;
using Microsoft.Dss.Core.Attributes;
using Microsoft.Dss.ServiceModel.Dssp;
using Microsoft.Dss.ServiceModel.DsspServiceBase;
using Microsoft.Dss.Services.ConsoleOutput;

namespace Microsoft.Robotics.Services.Sensors.SickLRF
{
    internal class LRFCommLinkPort : PortSet<LinkMeasurement, LinkPowerOn, LinkReset, LinkConfirm, LinkStatus, Exception>
    {
    }

    internal class LinkMeasurement
    {
        public DateTime TimeStamp;
        public Units Units;
        public int[] Ranges;
        public int ScanIndex;
        public int TelegramIndex;
    }

    /// <summary>
    /// Units of measure
    /// </summary>
    [DataContract]
    public enum Units
    {
        /// <summary>
        /// Centimeters
        /// </summary>
        Centimeters,
        /// <summary>
        /// Millimeters
        /// </summary>
        Millimeters
    }

    internal class LinkPowerOn
    {
        public LinkPowerOn()
        {
        }

        public LinkPowerOn(string description)
        {
            Description = description;
        }

        public string Description;
    }

    internal class LinkReset
    {
    }

    internal class LinkConfirm
    {
    }

    internal class CommLink : CcrServiceBase, IDisposable
    {
        LRFCommLinkPort _internalPort;
        SerialIOManager _serial;
        string _parent;
        ConsoleOutputPort _console;
        string _description;
        string _portName;
        int _rate;

        public new DispatcherQueue TaskQueue
        {
            get { return base.TaskQueue; }
        }

        public CommLink(DispatcherQueue dispatcherQueue, string port, LRFCommLinkPort internalPort)
            : base(dispatcherQueue)
        {
            _internalPort = internalPort;
            _portName = port;

            _serial = new SerialIOManager(dispatcherQueue, _portName);
            Activate<ITask>(
                Arbiter.Receive<Packet>(true, _serial.Responses, PacketHandler),
                Arbiter.Receive<Exception>(true, _serial.Responses, ExceptionHandler)
            );
        }

        public string Parent
        {
            get { return _parent; }
            set
            {
                _parent = value;
                _serial.Parent = value;
            }
        }

        public ConsoleOutputPort Console
        {
            get { return _console; }
            set
            {
                _console = value;
                _serial.Console = value;
            }
        }

        SuccessFailurePort Send(Packet packet)
        {
            SerialIOManager.Send send = new SerialIOManager.Send();
            send.Packet = packet;

            _serial.OperationsPort.Post(send);

            return send.ResponsePort;
        }

        public SuccessFailurePort Initialize()
        {
            if (_serial.BaudRate != 9600)
            {
                _rate = 9600;
            }
            return Send(Packet.InitializeAndReset);
        }

        /// <summary>
        /// Sets the Baud rate used to communicate with the LRF.
        /// Acceptable values are 38400, 19200 and 9600
        /// </summary>
        public SuccessFailurePort SetDataRate(int rate)
        {
            Packet packet;
            switch (rate)
            {
                case 38400:
                    packet = Packet.MonitoringMode(0x40);
                    break;

                case 19200:
                    packet = Packet.MonitoringMode(0x41);
                    break;

                case 9600:
                    packet = Packet.MonitoringMode(0x42);
                    break;

                default:
                    SuccessFailurePort port = new SuccessFailurePort();
                    port.Post(new ArgumentException("Baud Rate (only 9600, 19200 and 38400 supported)"));
                    return port;
            }
            _rate = rate;
            return Send(packet);
        }

        /// <summary>
        /// Gets the Baud rate used to communicate with the LRF.
        /// Acceptable values are 38400, 19200 and 9600
        /// </summary>
        public int BaudRate
        {
            get { return _serial.BaudRate; }
        }

        public string Description
        {
            get { return _description; }
        }

        void PacketHandler(Packet packet)
        {
            DateTime timeStamp = DateTime.Now;

            if (packet == null || !packet.GoodChecksum)
            {
                return;
            }
            switch (packet.Response)
            {
                case 0x91:
                    LogInfo("Reset");
                    OnReset(packet);
                    break;
                case 0x90:
                    LogInfo("Power On");
                    OnPowerOn(packet);
                    break;
                case 0xA0:
                    LogInfo("Confirm");
                    OnConfirm(packet);
                    break;
                case 0xB0:
                    OnMeasurement(packet, timeStamp);
                    break;
                case 0xB1:
                    LogInfo("Status");
                    OnStatus(packet);
                    break;
                default:
                    LogInfo("Unknown Packet: {0}", packet.Response);
                    break;
            }
        }

        void LogInfo(string format, params object[] args)
        {
            string msg = string.Format(format, args);

            DsspServiceBase.Log(TraceLevel.Info,
                                TraceLevel.Info,
                                new XmlQualifiedName("CommLink", Contract.Identifier),
                                _parent,
                                msg,
                                null,
                                _console);
        }

        void ExceptionHandler(Exception e)
        {
            _internalPort.Post(e);
        }

        private void OnStatus(Packet p)
        {
            _internalPort.Post(new LinkStatus(p.Data));
        }

        private void OnReset(Packet p)
        {
            SetRate();

            _internalPort.Post(new LinkReset());
        }

        private void OnConfirm(Packet p)
        {
            _internalPort.Post(new LinkConfirm());
        }

        public SuccessFailurePort SetRate()
        {
            SuccessFailurePort port;

            if (_rate != 0)
            {
                SerialIOManager.SetRate setRate = new SerialIOManager.SetRate(_rate);

                _serial.OperationsPort.Post(setRate);

                port = setRate.ResponsePort;
            }
            else
            {
                port = new SuccessFailurePort();
                port.Post(new Exception("Rate not set"));
            }

            return port;
        }


        private void OnMeasurement(Packet p, DateTime TimeStamp)
        {
            byte[] data = p.Data;
            LinkMeasurement lsd = new LinkMeasurement();
            lsd.TimeStamp = TimeStamp;

            ushort lengthAndFlags = Packet.MakeUshort(data[1], data[2]);
            int length = lengthAndFlags & 0x3FF;

            switch (lengthAndFlags >> 14)
            {
                case 0:
                    lsd.Units = Units.Centimeters;
                    break;
                case 1:
                    lsd.Units = Units.Millimeters;
                    break;
                default:
                    return;
            }

            lsd.Ranges = new int[length];

            int offset = 3;
            for (int i = 0; i < length; i++, offset += 2)
            {
                ushort range = Packet.MakeUshort(data[offset], data[offset + 1]);
                if (range > 0x1FF7)
                {
                    range = 0x2000;
                }
                lsd.Ranges[i] = range;
            }


            if (offset < p.Length - 1)
            {
                lsd.ScanIndex = data[offset++];
            }
            else
            {
                lsd.ScanIndex = -1;
            }
            if (offset < p.Length - 1)
            {
                lsd.TelegramIndex = data[offset++];
            }
            else
            {
                lsd.TelegramIndex = -1;
            }

            _internalPort.Post(lsd);
        }



        private void OnPowerOn(Packet p)
        {
            _description = "";
            byte[] data = p.Data;
            int length = data.Length;

            for (int i = 1; i < length - 1; i++)
            {
                _description = _description + ((char)data[i]);
            }

            _internalPort.Post(new LinkPowerOn(_description));
        }

        #region IDisposable Members

        public void Dispose()
        {
            Close();
        }

        #endregion

        public SuccessFailurePort Open()
        {
            SerialIOManager.Open open = new SerialIOManager.Open();

            _serial.OperationsPort.Post(open);

            return open.ResponsePort;
        }

        public SuccessFailurePort Close()
        {
            SerialIOManager.Close close = new SerialIOManager.Close();

            _serial.OperationsPort.Post(close);

            return close.ResponsePort;
        }

        public SuccessFailurePort SetContinuous()
        {
            return Send(Packet.MonitoringMode(0x24));
        }

        public SuccessFailurePort StopContinuous()
        {
            return Send(Packet.MonitoringMode(0x25));
        }

        public SuccessFailurePort MeasureOnce()
        {
            return Send(Packet.RequestMeasured(0x01));
        }

        public SuccessFailurePort RequestStatus()
        {
            return Send(Packet.Status);
        }
    }

    internal class LinkStatus
    {
        private byte[] _data;

        // 0-5
        public string SoftwareVersion { get { return MakeString(0, 7); } }
        // 7
        public byte OperatingMode { get { return _data[7]; } }
        // 8
        public byte OperatingStatus { get { return _data[8]; } }
        // 9-15
        public string ManufacturerCode { get { return MakeString(9, 8); } }
        // 17
        public byte Variant { get { return _data[17]; } }
        // 18-33
        public ushort[] Pollution { get { return MakeUshortArray(18, 8); } }
        // 34-41
        public ushort[] ReferencePollution { get { return MakeUshortArray(34, 4); } }
        // 42-57
        public ushort[] CalibratingPollution { get { return MakeUshortArray(42, 8); } }
        // 58-65
        public ushort[] CalibratingReferencePollution { get { return MakeUshortArray(58, 4); } }
        // 66-66
        public ushort MotorRevolutions { get { return MakeUshort(66); } }
        // 70-71
        public ushort ReferenceScale1Dark100 { get { return MakeUshort(70); } }
        // 74-75
        public ushort ReferenceScale2Dark100 { get { return MakeUshort(74); } }
        // 76-77
        public ushort ReferenceScale1Dark66 { get { return MakeUshort(76); } }
        // 80-81
        public ushort ReferenceScale2Dark66 { get { return MakeUshort(80); } }
        // 82-83
        public ushort SignalAmplitude { get { return MakeUshort(82); } }
        // 84-85
        public ushort CurrentAngle { get { return MakeUshort(84); } }
        // 86-87
        public ushort PeakThreshold { get { return MakeUshort(86); } }
        // 88-89
        public ushort AngleofMeasurement { get { return MakeUshort(88); } }
        // 90-91
        public ushort CalibrationSignalAmplitude { get { return MakeUshort(90); } }
        // 92-93
        public ushort TargetStopThreshold { get { return MakeUshort(92); } }
        // 94-95
        public ushort TargetPeakThreshold { get { return MakeUshort(94); } }
        // 96-97
        public ushort ActualStopThreshold { get { return MakeUshort(96); } }
        // 98-99
        public ushort ActualPeakThreshold { get { return MakeUshort(98); } }
        // 101
        public byte MeasuringMode { get { return _data[101]; } }
        // 102-103
        public ushort ReferenceTargetSingle { get { return MakeUshort(102); } }
        // 104-105
        public ushort ReferenceTargetMean { get { return MakeUshort(104); } }
        // 106-107
        public ushort ScanningAngle { get { return MakeUshort(106); } }
        // 108-109
        public ushort AngularResolution { get { return MakeUshort(108); } }
        // 110
        public byte RestartMode { get { return _data[110]; } }
        // 111
        public byte RestartTime { get { return _data[111]; } }
        // 115-116
        public ushort BaudRate { get { return MakeUshort(115); } }
        // 117
        public byte EvaluationNumber { get { return _data[117]; } }
        // 118
        public byte PermanentBaudRate { get { return _data[118]; } }
        // 119
        public byte LMSAddress { get { return _data[119]; } }
        // 120
        public byte FieldSetNumber { get { return _data[120]; } }
        // 121
        public byte CurrentMVUnit { get { return _data[121]; } }
        // 122
        public byte LaserSwitchOff { get { return _data[122]; } }
        // 123-130
        public string BootPROMVersion { get { return MakeString(123, 8); } }
        // 131-144
        public ushort[] CalibrationValues { get { return MakeUshortArray(131, 7); } }

        public LinkStatus(byte[] data)
        {
            if (data.Length < 145)
            {
                throw new ArgumentException("data");
            }
            _data = new byte[144];
            Array.Copy(data, 1, _data, 0, 144);
        }

        ushort MakeUshort(int index)
        {
            return (ushort)(_data[index] | (_data[index + 1] << 8));
        }

        string MakeString(int index, int length)
        {
            string ret = "";
            for (int i = 0; i < length; i++)
            {
                ret = ret + (char)_data[index++];
            }
            return ret;
        }

        ushort[] MakeUshortArray(int index, int length)
        {
            ushort[] array = new ushort[length];

            for (int i = 0; i < length; i++)
            {
                array[i] = MakeUshort(index);
                index += 2;
            }

            return array;
        }
    }

}
