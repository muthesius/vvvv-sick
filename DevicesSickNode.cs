#region usings
using System;
using System.IO;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;

using VVVV.Nodes.Devices;

using VVVV.Core.Logging;
using Sick;
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
		
		[Output("Valid")]
		public ISpread<bool> Valid;
		
		[Output("Checksums")]
		public ISpread<int> Checksums;
		
		[Output("Debug")]
		public ISpread<string> Debug;
		
		[ImportAttribute()]
		ILogger Logger;
		
		#endregion fields & pins
		
		
		SickDevice Scanner;
		PacketBuilder PacketCollector;
		
		public void OnImportsSatisfied() {
			Scanner = new SickDevice();
			PacketCollector = new PacketBuilder(Logger);
			
			
			Input.Changed += delegate(IDiffSpread<Stream> input) {
				if (input.SliceCount == 0) return;
				
				var s = input[0];
				if (s.Length == 0) return;
				
				byte[] buff = new byte[s.Length];
				s.Read(buff,0, buff.Length);
				Valid.SliceCount = 0;
				Valid.SliceCount = 0;
				Checksums.SliceCount = 0;
				FOutput.SliceCount = 1;
				PacketCollector.Add(buff,buff.Length);
				
				while (PacketCollector.HasPacket) {
					Muthesius.SickLRF.Packet p = PacketCollector.RemovePacket();
					var ps = new MemoryStream();
					ps.Write(p._data,0,p._data.Length);
					
					FOutput[0] = ps;
				}
				
				Valid.Add(PacketCollector.HasPacket);
				Checksums.Add(PacketCollector.BadPackets);
				Checksums.Add(PacketCollector.Noise);
			};
		}
		
		public void Dispose()  {
			if (Scanner != null) Scanner.Dispose();
		}
		
		public void Evaluate(int SpreadMax)
		{
			
			
		}
	}
	
}