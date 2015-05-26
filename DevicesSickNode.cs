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
			Scanners.SliceCount = 1;
			Scanners[0] = Scanner;
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
			
			//Scanner.Flush();
		}
	}
	
	
	[PluginInfo(Name = "SickDecoder", Category = "Devices", Help = "Basic template with one value in/out", Tags = "")]
	public class DevicesSickDecoderNode : IPluginEvaluate, IDisposable
	{
		
		[Input("Device Handle")]
		public ISpread<SickDevice> Scanners;
		
		[Input("Data In", IsSingle = true)]
		public ISpread<Stream> Input;
		
		[Input("Reset", IsSingle = true, IsBang = true)]
		public IDiffSpread<bool> Reset;
		
		[Output("Raw Out")]
		public ISpread<Stream> Raw;
		
		[Output("Debug")]
		public ISpread<int> Debug;
		
		public void Dispose() {
		}
		
		public void Evaluate(int SpreadMax)
		{
			Debug.SliceCount = 0;
			SickDevice Scanner = Scanners[0];
			if (Reset.IsChanged && Reset[0]) Scanner.Reset();
			
			Raw.SliceCount = 2;
			
			if (Input.SliceCount> 0){
				Scanner.ReadFrom(Input[0]);
			}
			Debug.Add((int)Scanner.RxBuffer.Count);
			if (Raw[0] == null) Raw[0] = new MemoryStream(Scanner.RxBuffer.Count);
			Raw[0].SetLength(Scanner.RxBuffer.Count);
			Raw[0].Position = 0;
			Raw[0].Write(Scanner.RxBuffer.ToArray(),0, Scanner.RxBuffer.Count);
			
			if (Scanner.currentPacket != null) {
				Debug.Add((int)Scanner.currentPacket.Length);
			}
			Raw[1] = Scanner.currentPacket;			
		}
		
	}
}
