using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading.Tasks;

using VVVV.PluginInterfaces.V2;
using VVVV.Core;
using VVVV.Core.Logging;
using System.ComponentModel.Composition;

using Lea.Utils;

namespace VVVV.Nodes {
	
	[PluginInfo(Name = "RingStream", Category = "Raw", Help = "Implements a Ringbuffer as a Stream", Tags = "")]
	public class RingStreamNode : IPluginEvaluate, IDisposable, IPartImportsSatisfiedNotification
	{
		
		[Input("Input")]
		public IDiffSpread<Stream> Input;
		
		[Input("Buffer Size")]
		public IDiffSpread<int> BufferSizes;
		
		[Output("Output")]
		public ISpread<Stream> Output;
		
		[Output("SpreadMax")]
		public ISpread<int> FSpreadMax;
		
		[Output("Debug")]
		public ISpread<string> Debug;
		
//		[Import()]
//		ILogger Logger;
//		
		public void OnImportsSatisfied() {
			Output.SliceCount = 0;
			Output.ResizeAndDispose(0,(int i) => new RingStream(BufferSizes[i]));
		}
		
		public void Dispose() {
			foreach(RingStream rs in Output) rs.Dispose();
		}
		
		public void Evaluate(int SpreadMax) {
			Output.ResizeAndDispose(Input.SliceCount,(int i) => new RingStream(BufferSizes[i]));
			FSpreadMax.SliceCount = Output.SliceCount;
			Debug.SliceCount = 0;
			for(int i=0; i<Output.SliceCount; i++) {
				var buff = Output[i] as RingStream;
				if (BufferSizes.IsChanged) {
					buff.BufferSize = BufferSizes[i];
				}
				if(Input.IsChanged) {
					var inStream = Input[i];
					buff.CopyFrom(inStream);
				}
				Output[i] = buff;
				FSpreadMax[i] = Output[i].GetHashCode();
				Debug.Add(buff.ToString());
			}
		}
	}
}