//-----------------------------------------------------------------------
//  This file is part of Microsoft Robotics Developer Studio Code Samples.
//
//  Copyright (C) Microsoft Corporation.  All rights reserved.
//
//  $File: Packet.cs $ $Revision: 7 $
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.IO.Ports;
using System.Diagnostics;
using VVVV.Core.Logging;

namespace Muthesius.SickLRF
{
    internal class Packet
    {
        public byte[] _data;

        #region Constructors

        public Packet(List<byte> data)
        {
            _data = data.ToArray();
        }

        Packet(byte stx, byte address, byte command)
        {
            _data = new byte[7];

            _data[0] = stx;
            _data[1] = address;
            Write(2, 1);
            _data[4] = command;
            Write(5, CalculatedChecksum);
        }

        Packet(byte stx, byte address, byte[] command)
        {
            _data = new byte[6 + command.Length];
            _data[0] = stx;
            _data[1] = address;
            Write(2, (ushort)command.Length);
            command.CopyTo(_data, 4);
            Write(4 + command.Length, CalculatedChecksum);
        }

        #endregion

        #region Component accessors
        public byte STX
        {
            get { return _data[0]; }
        }
        public byte Address
        {
            get { return _data[1]; }
        }
        public ushort Length
        {
            get { return ReadUshort(2); }
        }
        public byte Command
        {
            get { return _data[4]; }
        }
        public byte Response
        {
            get { return _data[4]; }
        }
        public byte[] Data
        {
            get
            {
                byte[] data = new byte[Length];

                Array.Copy(_data, 4, data, 0, Length);

                return data;
            }
        }
        public ushort Checksum
        {
            get { return ReadUshort(4 + Length); }
        }
        public ushort CalculatedChecksum
        {
            get { return CreateCRC(_data, 0, 4 + Length); }
        }

        public bool GoodChecksum
        {
            get
            {
                return CalculatedChecksum == Checksum;
            }
        }
        #endregion

        #region static Constructors

        public static Packet InitializeAndReset
        {
            get
            {
                return new Packet(0x02, 0x00, 0x10);
            }
        }

        public static Packet MonitoringMode(byte mode)
        {
            return new Packet(0x02, 0x00, new byte[] { 0x20, mode });
        }

        public static Packet RequestMeasured(byte mode)
        {
            return new Packet(0x02, 0x00, new byte[] {0x30, mode});
        }

        public static Packet Status
        {
            get
            {
                return new Packet(0x02, 0x00, 0x31);
            }
        }

        #endregion

        #region IO
        public void Send(SerialPort port)
        {
            port.Write(_data, 0, _data.Length);
        }

        public static Packet Read(SerialPort port)
        {
            try
            {
                List<byte> data = new List<byte>();

                byte b = 0;

                while (b != 0x02)
                {
                    if (port.BytesToRead == 0)
                    {
                        return null;
                    }
                    b = (byte)port.ReadByte();
                }
                // STX
                data.Add(b);
                // Address
                data.Add((byte)port.ReadByte());
                // Low Length
                data.Add((byte)port.ReadByte());
                // High Length
                data.Add((byte)port.ReadByte());

                ushort length = MakeUshort(data[2], data[3]);

                for (int i = 0; i < length; i++)
                {
                    data.Add((byte)port.ReadByte());
                }

                // Low Checksum
                data.Add((byte)port.ReadByte());
                // High Checksum
                data.Add((byte)port.ReadByte());

                return new Packet(data);
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Utility Functions

        public ushort ReadUshort(int index)
        {
            return (ushort)(_data[index] | (_data[index + 1] << 8));
        }

        void Write(int index, ushort value)
        {
            _data[index] = (byte)(value & 0xFF);
            _data[index + 1] = (byte)((value >> 8) & 0xFF);
        }

        public static ushort MakeUshort(byte LowByte, byte HighByte)
        {
            return (ushort)(LowByte | (HighByte << 8));
        }
        #endregion

        #region CRC Function
        const ushort CRC16_GEN_POL = 0x8005;
        static ushort CreateCRC(byte[] CommData, int start, int end)
        {
            ushort crc;
            byte low, high;

            crc = 0;
            low = 0;

            for (int index = start; index < end; index++)
            {
                high = low;
                low = CommData[index];

                if ((crc & 0x8000) == 0x8000)
                {
                    crc = (ushort)((crc & 0x7FFF) << 1);
                    crc ^= CRC16_GEN_POL;
                }
                else
                {
                    crc <<= 1;
                }
                crc ^= MakeUshort(low, high);
            }
            return crc;
        }
        #endregion

    }

    internal class PacketBuilder
    {
        List<byte> _data = new List<byte>();
        Queue<Packet> _packets = new Queue<Packet>();

        enum State
        {
            STX,
            Address,
            LowLength,
            HighLength,
            Body,
            LowChecksum,
            HighChecksum
        }

        State _state;
        int _length;
        int _dropped;
        int _total;
        bool _missedFirst;
        int _badCount;
        string _parent;
        ILogger Logger;

        public PacketBuilder(ILogger logger)
        {
        	Logger = logger;
            _dropped = 0;
            _total = 0;
            _state = State.STX;
            _missedFirst = false;
            _badCount = 0;
        }

        public void Add(byte[] buffer, int length)
        {
            int dropped = 0;

            for (int index = 0; index < length; index++)
            {
                byte b = buffer[index];

                switch (_state)
                {
                    case State.STX:
                        if (b == 0x02)
                        {
                            _state = State.Address;
                            _data.Add(b);
                            if (_missedFirst)
                            {
                                LogInfo("SickLRF: packet sync problem, found new STX");
                                _missedFirst = false;
                            }
                        }
                        else
                        {
                            if (!_missedFirst)
                            {
                                LogInfo("SickLRF: packet sync problem (no STX), resyncing");
                                _missedFirst = true;
                            }
                            dropped++;
                        }
                        break;

                    case State.Address:
                        _state = State.LowLength;
                        _data.Add(b);
                        break;

                    case State.LowLength:
                        _state = State.HighLength;
                        _data.Add(b);
                        _length = b;
                        break;

                    case State.HighLength:
                        _data.Add(b);
                        _length |= b << 8;
                        if (_length >= 1024)
                        {
                            LogInfo("SickLRF Packet length too big, {0} bytes",
                                _length);

                            dropped += _data.Count;
                            _badCount++;

                            _state = State.STX;
                            _data.Clear();
                        }
                        else if (_length > 0)
                        {
                            _state = State.Body;
                        }
                        else
                        {
                            _state = State.LowChecksum;
                        }
                        break;

                    case State.Body:
                        _data.Add(b);
                        _length--;
                        if (_length == 0)
                        {
                            _state = State.LowChecksum;
                        }
                        break;

                    case State.LowChecksum:
                        _data.Add(b);
                        _state = State.HighChecksum;
                        break;

                    case State.HighChecksum:
                        _data.Add(b);
                        Packet p = new Packet(_data);

                        if (p.GoodChecksum)
                        {
                            _packets.Enqueue(p);
                            _badCount = 0;
                        }
                        else
                        {
                            LogInfo("SickLRF Bad Checksum: packet {0}, calc {1}",
                                p.Checksum,
                                p.CalculatedChecksum);
                            dropped += _data.Count;
                            _badCount++;
                        }

                        _data.Clear();
                        _state = State.STX;
                        break;
                }
            }
            _total += length;
            _dropped += dropped;

            if (_total > 0x10000)
            {
                _dropped = _dropped * 4 / 5;
                _total = _total * 4 / 5;
            }

            if (dropped > 0)
            {
                LogInfo("SickLRF Noise: {0}:{1}", dropped, length);
            }
        }

        public bool HasPacket
        {
            get
            {
                return _packets.Count > 0;
            }
        }

        public int BadPackets
        {
            get
            {
                return _badCount;
            }
        }

        public Packet RemovePacket()
        {
            return _packets.Dequeue();
        }

        public int Noise
        {
            get { return 100 * _dropped / _total; }
        }

        public string Parent
        {
            get { return _parent; }
            set { _parent = value; }
        }

        void LogInfo(string format, params object[] args)
        {
            string msg = string.Format(format, args);

            Logger.Log(LogType.Debug,msg);
        }
    }
}
