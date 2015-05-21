#region usings
using System;
using System.IO;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;

using VVVV.Core.Logging;
using Sick;
#endregion usings

namespace VVVV.Nodes
{
	#region PluginInfo
	[PluginInfo(Name = "Sick", Category = "Devices", Help = "Basic template with one value in/out", Tags = "")]
	#endregion PluginInfo
	public class DevicesSickNode : IPluginEvaluate, IDisposable, IPartImportsSatisfiedNotification
	{
		#region fields & pins
		[Input("Scan", IsSingle = true, IsToggle = true)]
		public IDiffSpread<bool> DoScan;

		[Input("Get Status", IsSingle = true, IsBang = true)]
		public IDiffSpread<bool> GetStatus;

		[Output("Output")]
		public ISpread<Stream> FOutput;

		[Output("Valid")]
		public ISpread<bool> Valid;

		[Output("Checksums")]
		public ISpread<int> Checksums;
		#endregion fields & pins

		
		SickDevice Scanner;
		
		public void OnImportsSatisfied() {
			Scanner = new SickDevice();
			
			DoScan.Changed += delegate(IDiffSpread<bool> doScan) {
				Scanner.ScanContinuous = doScan[0];
			};
		}
		
		public void Dispose()  {
			if (Scanner != null) Scanner.Dispose();
		}
		
		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{
			FOutput.SliceCount = 4;
			Valid.SliceCount = 1;
			Checksums.SliceCount = 2;
			Packet p =  Scanner.MakeTestPacket();
			FOutput[0] = p;
			FOutput[1] = p.Checksum;
			FOutput[2] = Sick.Packet.StartContinuousOutput;
			FOutput[3] = Sick.Packet.StopContinuousOutput;
			Valid[0] = p.IsValid;
			Checksums[0] = p.Checksums[0];
			Checksums[1] = (byte) p.Command;
			
		}
	}
}
