#region usings
using System;
using System.IO;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;

using VVVV.Nodes.Devices;

using VVVV.Core.Logging;
using Sick;

#endregion usings

namespace VVVV.Nodes.Devices
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
		
		[Input("Data In", IsSingle = true, AutoValidate = false)]
		public IDiffSpread<Stream> Input;
		
		[Input("Port Name", EnumName = "Rs232Node.ComPort")]
		public ISpread<EnumEntry> ComPortIn;
		
		
		[Output("Device Handle")]
		public ISpread<SickDevice> Scanners;
		
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
			
			GetStatus.Changed += delegate(IDiffSpread<bool> getStatus) {
				if (GetStatus[0]) Scanner.GetStatus();
			};
		}
		
		public void Dispose()  {
			if (Scanner != null) Scanner.Dispose();
		}
		
		public void Evaluate(int SpreadMax)
		{
			FOutput.SliceCount = 4;
			Valid.SliceCount = 1;
			Checksums.SliceCount = 3;
			Packet p =  Scanner.MakeTestPacket();
			FOutput[0] = p;
			FOutput[1] = p.Checksum;
			FOutput[2] = Scanner.Transmit;
			Valid[0] = p.IsValid;
			Checksums[0] = p.Checksums[0];
			Checksums[1] = Input.SliceCount;
			
			if (Input.SliceCount> 0){
				Checksums[2] = (int) Input[0].Length;
				Scanner.ReadFrom(Input[0]);
			}
			Scanner.Flush();
		}
	}
	
	
	[PluginInfo(Name = "SickDecoder", Category = "Devices", Help = "Basic template with one value in/out", Tags = "")]
	public class DevicesSickDecoderNode : IPluginEvaluate, IDisposable
	{
		
		[Input("Device Handle")]
		public ISpread<SickDevice> Scanners;
		
		[Input("Data In", IsSingle = true, AutoValidate = false)]
		public IDiffSpread<Stream> Input;

		[Output("Raw Out")]
		public ISpread<Stream> Raw;
		
		public void Dispose() {}
		
		public void Evaluate(int SpreadMax)
		{
			SickDevice Scanner = Scanners[0];
			Raw.SliceCount = 1;
			if (Input.SliceCount> 0){
				Scanner.ReadFrom(Input[0]);
				Raw[0] = Scanner.Receive;
			}
			
		}
		
	}
}
