using System;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Collections.Generic;
//using VVVV.Utils.Streams;

namespace Sick {
	
	public class SickDevice: IDisposable {
		public MemoryStream Transmit;
		public RingBuffer<byte> RxBuffer;
		public SickDevice() {
			Transmit = new MemoryStream();
			RxBuffer = new RingBuffer<byte>(16);
		}
		
		public void Reset() {
			RxBuffer.Clear();
			state = 0;
			if (currentPacket != null) {
				currentPacket.Dispose();
				currentPacket = null;
			}
		}
		
		public void Dispose() {
			// Maybe send some reset signal to the device (mode, Baudrate, etc)
		}
		
		void Send(Packet p) {
			// Build the packet
			Transmit.Seek(0,SeekOrigin.Begin);
			p.WriteTo(Transmit);
			Transmit.Flush();
		}
		
		public void GetStatus() {
			Send(Packet.GetStatus);
		}
		
		public bool ScanContinuous {
			set {
				var p = value ?	Packet.StartContinuousOutput : Packet.StopContinuousOutput;
				Send(p);
			}
		}
		
		public void CheckPacket(Stream input) {
			
		}
		
		public Packet MakeTestPacket() {
			var s = new Packet();
			byte[] data = {0x02, 0x00, 0x01, 0x00, 0x31, 0x15, 0x12};
			s.Write(data,0,data.Length);
			return s;
		}
		
		public void WriteTo(ref Stream s) {
			Transmit.WriteTo(s);
			Transmit.Flush();
		}
		
		public void ReadFrom(Stream s) {
			if (s.Length == 0) return;
			s.Position = 0;
			Int32 data;
			while ((data = s.ReadByte()) >= 0) {
				RxBuffer.Enqueue((byte) data);
			}
			FindPaket();
		}
		
		
		public void Flush() {
			Transmit.Flush();
		}
		
		
		public int state = 0;
		Stack<byte> paketPositions = new Stack<byte>();
		public Packet currentPacket = null;
		void FindPaket() {
			byte[] buff = RxBuffer.ToArray();
			if (buff.Length == 0) return;
			if(state == 0) {
				int i = buff.Length;
				while( --i > 0) {
					if (buff[i] == Packet.START_CODE) {
						state = 1;
						currentPacket = new Packet();
						currentPacket.Write(buff,i,buff.Length-i);
						RxBuffer.Clear();
//						continue;
					} else {
//						RxBuffer.Dequeue();
					}
				}
			}
			if (state == 1) {
				currentPacket.Append(buff);
				RxBuffer.Clear();
				if (currentPacket.Length > 128) {
					state = 0;
					currentPacket.Dispose();
					currentPacket = null;
				}
				
			}
			
		}
		
	}
	
	#region packet stuff
	public class Packet: MemoryStream {
		public const byte START_CODE = 0x02;
		public Packet(): base(1024) { // init with 1KB
			base.SetLength(7); // 7 is the minimum Length
		}
		
		public Command Command {
			get {
				return (Command) base.GetBuffer()[4];
			}
			set {
				base.Seek(4, SeekOrigin.Begin);
				base.WriteByte((byte) value);
			}
		}
		
		Stream chksm = new MemoryStream(2);
		public Stream Checksum {
			get { return chksm; }
		}
		public bool IsValid = false;
		public ushort[] Checksums = new ushort[2];
		
		public void Append(byte[] data) {
			base.Write(data,0,data.Length);
		}
		
		public override void Write(byte[] data, int offset, int length) {
			base.Write(data,offset,length);
			//			ushort given_crc = (ushort)(((ushort)data[data.Length-1])<<8 | (ushort)data[data.Length-2]);
			//			byte[] buffer = new byte[data.Length-2];
			//			Buffer.BlockCopy(data,0,buffer,0,buffer.Length);
			//			ushort current_crc = Crc16.Sum(this,0);
			//			chksm.Seek(0,SeekOrigin.Begin);
			//			chksm.Write(data,data.Length-2,2);
			//			Checksums[0] = given_crc;
			//			Checksums[1] = current_crc;
			//			IsValid = current_crc == given_crc;
			//			base.Write(data,data.Length-2,2);
		}
		
		
		public static Packet StartContinuousOutput {
			get {
				var p = new Packet();
				byte[] buff = { 0x02, 0x00, 0x02, 0x00, 0x20, 0x24, 0x34, 0x08 };
				p.Write(buff, 0, buff.Length);
				return p;
			}
		}
		
		public static Packet StopContinuousOutput {
			get {
				var p = new Packet();
				byte[] buff = { 0x02, 0x00, 0x02, 0x00, 0x20, 0x25, 0x35, 0x08 };
				p.Write(buff, 0, buff.Length);
				return p;
			}
		}
		
		public static Packet GetStatus {
			get {
				var p = new Packet();
				byte[] buff = {0x02, 0x00, 0x01, 0x00, 0x31, 0x15, 0x12};
				p.Write(buff,0,buff.Length);
				return p;
			}
		}
	}
	
	public enum Command {
		Status = 0x31
	}
	
	static class Crc16
	{
		const ushort polynomial = 0x8005;
		
		public static ushort Sum(Stream s, int offset) {
			return 0;
			//			long currentPosition = s.Position;
			//			s.Seek(offset, SeekOrigin.Begin);
			//			ushort uCrc16 = 0;
			//			byte[] abData = new byte[2];
			//			long uLen = s.Length - 2;
			//			for (int i=offset; i<uLen; i++) {
				//				abData[1] = abData[0];
			//				s.Read(abData,i,1);
			//				if((uCrc16 & 0x8000) > 0)
			//				{
				//					uCrc16 = (ushort) ((uCrc16 & 0x7fff) << 1);
			//					uCrc16 ^= polynomial;
			//				}
			//				else {
				//					uCrc16 <<= 1;
			//				}
			//				uCrc16 ^= (ushort) ((ushort) (abData[0]) | ((ushort)(abData[1]) << 8));
			//			}
			//			s.Seek(currentPosition, SeekOrigin.Begin);
			//			return(uCrc16);
		}
	}
	#endregion packet stuff
	
	
	public class RingBuffer<T> : Queue<T> {
		uint buffSize;
		public RingBuffer(uint size) : base(1024) {
			buffSize = size;
		}
		public new void Enqueue(T item) {
			if (Count >= buffSize) {
				Dequeue();
			}
			base.Enqueue(item);
		}
	}
}
