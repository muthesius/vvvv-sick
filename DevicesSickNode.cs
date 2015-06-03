#region usings
using System;
using System.IO;
using System.ComponentModel.Composition;
using System.Diagnostics;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;

using VVVV.Nodes.Devices;

using VVVV.Core.Logging;
using Muthesius.SickLRF;

#endregion usings

namespace VVVV.Nodes.Devices
{
	
	
	#region PluginInfo
	[PluginInfo(Name = "Sick", Category = "Devices", Help = "Basic template with one value in/out", Tags = "")]
	#endregion PluginInfo
	public class DevicesSickNode : IPluginEvaluate, IDisposable, IPartImportsSatisfiedNotification
	{
		#region fields & pins
		[Input("Input", IsSingle = true)]
		public IDiffSpread<Stream> Input;
		
		[Input("Enable", IsSingle = true, IsToggle = true)]
		public IDiffSpread<bool> Enable;
		
		[Input("HighSpeed", IsSingle = true, IsToggle = true)]
		public IDiffSpread<bool> HighSpeed;
		
		[Input("Reset", IsSingle = true, IsBang = true)]
		public IDiffSpread<bool> Reset;
		
		[Input("Port Name", DefaultString = "COM3")]
		public IDiffSpread<string> ComPortIn;
		
		[Input("Baudrate", IsSingle = true, DefaultValue = 9600)]
		public IDiffSpread<int> Baudrate;
		
		#region Output
		[Output("Output")]
		public ISpread<Stream> FOutput;
		
		[Output("Scan")]
		public ISpread<int> ScanValues;
		
		[Output("Valid")]
		public ISpread<bool> Valid;
		
		[Output("Bad Packets")]
		public ISpread<int> BadPacketCount;
		
		[Output("Noise")]
		public ISpread<int> NoiseStats;
		
		[Output("Faulty Measurement")]
		public ISpread<bool> FaultyMeasurement;
		
		[Output("Is Dirty")]
		public ISpread<bool> IsDirty;
		
		[Output("Device Info")]
		public ISpread<string> DeviceInfo;
		
		[Output("On Data", IsBang = true)]
		public ISpread<bool> SignalData;
		
		[Output("Connected", IsToggle = true)]
		public ISpread<bool> Connected;
		
		
		[Output("Debug")]
		public ISpread<string> Debug;
		
		[Output("Timings")]
		public ISpread<double> Timings;
		
		#endregion Output
		
		[ImportAttribute()]
		ILogger Logger;
		
		#endregion fields & pins
		
		
		SickDevice Scanner;
		
		bool DidReceivePacket = false;
		public void OnImportsSatisfied() {
			Scanner = new SickDevice();
//			Scanner.PortName = ComPortIn.SliComPortIn[0];
//			Scanner.BaudRate = Baudrate.SliceCount > 0 ? Baudrate[0] : 9600;
			
			ComPortIn.Changed += (names) => {
				if(names.SliceCount == 0) return;
				Scanner.PortName = names[0];
			};
			
			Scanner.DidError += (e) => Logger.Log(e);
			
			Scanner.PacketReceived += PacketHandler;
			
			// temporary write method, TODO move as Methods to SickDevice Class
			Input.Changed += (input) => {
				if (input.SliceCount == 0 || input[0].Length == 0) return;
				var s = input[0];
				s.CopyTo(Scanner.BaseStream);
				Scanner.BaseStream.Flush();
			};
		}
		
		public void Dispose()  {
			Scanner.Dispose();
		}
		
		public void Evaluate(int SpreadMax)
		{
			if (Baudrate.IsChanged) Scanner.Baudrate = Baudrate[0];
			if (Enable.IsChanged) Scanner.Enable = Enable[0];
			
			Connected[0] = Scanner.IsOpen;
			
			BadPacketCount[0] = Scanner.BadPackets;
			if (DidReceivePacket) {
				DidReceivePacket = false;
				SignalData[0] = true;
			}
			else {
				SignalData[0] = false;
			}
		}
		
		
		#region Packet Handling
		DateTime LastPacketReceived = DateTime.Now;
		
		void PacketHandler(Packet packet)
		{
			DidReceivePacket = true;
			LastPacketReceived = packet.TimeStamp;
			
			Valid[0] = packet.GoodChecksum;
			
			if (Timings.SliceCount >= 10) Timings.RemoveAt(0);
			double millis = packet.TimeStamp.ToUniversalTime().Subtract(
    				new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    			).TotalMilliseconds;
			Timings.Add(millis);
			
			if (!packet.GoodChecksum)
			{
				FaultyMeasurement[0] = false;
				IsDirty[0] = false;
				return;
			}

			FaultyMeasurement[0] = packet.FaultyMeasurement;
			IsDirty[0] = packet.IsDirty;
			
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
				OnMeasurement(packet);
				break;
				case 0xB1:
				LogInfo("Status");
				OnStatus(packet);
				break;
				default:
				LogInfo("Unknown Packet: {0}", packet.Response.ToString("X"));
				break;
			}
		}
		
		void OnReset(Packet packet) {
		}
		
		void OnPowerOn(Packet p) {
			string _description = "";
			byte[] data = p.Data;
			int length = data.Length;
			
			for (int i = 1; i < length - 1; i++)
			{
				_description = _description + ((char)data[i]);
			}
			
			DeviceInfo.SliceCount = 1;
			DeviceInfo[0] = _description;
		}
		
		void OnStatus(Packet p) {
			
		}
		
		void OnConfirm(Packet p) {
			
		}
		
		// TODO Abstract this into the SickDevice class!
		void OnMeasurement(Packet p) {
			byte[] data = p.Data;
			LinkMeasurement lsd = new LinkMeasurement();
			lsd.TimeStamp = p.TimeStamp;
			
			ScanValues.SliceCount = 0;
			
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
			//			Logger.Log(LogType.Debug,length.ToString()+ " : units : " +lsd.Units.ToString());
			int offset = 3;
			for (int i = 0; i < length; i++, offset += 2)
			{
				ushort range = Packet.MakeUshort(data[offset], data[offset + 1]);
				if (range > 0x1FF7) // if > 8191
				{
					// default max value of 0x2000
					range = 0x2000;
				}
				lsd.Ranges[i] = range;
				ScanValues.Add(range);
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
			
			
		}

		public void LogInfo(string format, params object[] args)
		{
			if(Logger == null) return;
			string msg = string.Format(format, args);
			Logger.Log(LogType.Debug,msg);
		}
		#endregion Packet Handling
	}
	
}