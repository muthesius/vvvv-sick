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
		
		[Input("Port Name", EnumName = "Rs232Node.ComPort")]
		public ISpread<EnumEntry> ComPortIn;
		
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
		
		[Output("Debug")]
		public ISpread<string> Debug;
		
		[ImportAttribute()]
		ILogger Logger;
		
		#endregion fields & pins
		
		
		PacketBuilder PacketCollector;
		
		public void OnImportsSatisfied() {
			PacketCollector = new PacketBuilder(Logger);
			
			Input.Changed += delegate(IDiffSpread<Stream> input) {
				if (input.SliceCount == 0) return;
				
				var s = input[0];
				if (s.Length == 0) return;
				
				byte[] buff = new byte[s.Length];
				s.Read(buff,0, buff.Length);
				
				Valid.SliceCount = 0;
				BadPacketCount.SliceCount = 0;
				NoiseStats.SliceCount = 0;
				IsDirty.SliceCount = 0;
				FaultyMeasurement.SliceCount = 0;
				
				FOutput.ResizeAndDispose(1,()=>Stream.Null);
				
				PacketCollector.Add(buff,buff.Length);
				BadPacketCount.Add(PacketCollector.BadPackets);
				NoiseStats.Add(PacketCollector.Noise);
				
				Packet packet = null;
				var ps = Stream.Null;
				while (PacketCollector.HasPacket) {
					ps = new MemoryStream();
					packet = PacketCollector.RemovePacket();
					ps.Write(packet._data,0,packet._data.Length);
				}
				FOutput[0] = ps;
				
				PacketHandler(packet);
			};
		}
		
		public void Dispose()  {
		}
		
		public void Evaluate(int SpreadMax)
		{
			
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
		
		void OnMeasurement(Packet p, DateTime TimeStamp) {
			byte[] data = p.Data;
			LinkMeasurement lsd = new LinkMeasurement();
			lsd.TimeStamp = TimeStamp;
			
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
		
		
		
		void PacketHandler(Packet packet)
		{
			DateTime timeStamp = DateTime.Now;
			
			Valid.Add(packet!=null);
			
			if (packet == null || !packet.GoodChecksum)
			{
				FaultyMeasurement.Add(false);
				IsDirty.Add(false);
				return;
			}
			
			FaultyMeasurement.Add(packet.IsDirty);
			IsDirty.Add(packet.IsDirty);
			
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
		
		public void LogInfo(string format, params object[] args)
		{
			string msg = string.Format(format, args);
			
			Logger.Log(LogType.Debug,msg);
		}
	}
	
}