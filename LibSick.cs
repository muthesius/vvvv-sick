using System;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Collections.Generic;
using VVVV.Utils.Streams;

namespace Sick {
	
	public class SickDevice: IDisposable {
		public MemoryStream Transmit;
		public RingStream Receive;
		public SickDevice() {
			Transmit = new MemoryStream();
			Receive = new RingStream(10);
		}
		
		public void Reset() {
			Receive.SetLength(0);
		}
		
		public void Dispose() {
			// Maybe send some reset signal to the device (mode, Baudrate, etc)
			if (port.IsOpen) port.Close();
			port.Dispose();
		}
		
		SerialPort port = new SerialPort();
		public string Port {
			set {
				// reset the current source
				if (port.IsOpen) {
					// do reset here!
					port.Close();
				}
				ResetPort();
				port.PortName = value;
				//				byte[] buffer = new byte[64];
				//				Action readData;
				//				readData = delegate {
					//					port.BaseStream.BeginRead(buffer,0,buffer.Length, delegate(IAsyncResult ar) {
						//						try {
							//							int actualLength = port.BaseStream.EndRead(ar);
				//							byte[] received = new byte[actualLength];
				//							Buffer.BlockCopy(buffer, 0, received, 0, actualLength);
				//							// raiseAppSerialDataEvent(received);
				//						}
				//						catch (IOException exc) {
					//							// handleAppSerialError(exc);
				//						}
				//						var a = ar.AsyncState as Action;
				//						a();
				//					}, readData);
				//				};
				//				readData();
				Connect();
			}
			get { return port.PortName; }
		}
		
		
		void ResetPort() {
			
		}
		
		void Connect() {
			
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
			for (Int32 data = s.ReadByte(); data != -1; data=s.ReadByte()) {
				Receive.WriteByte((byte)data);
			}
		}
		
		
		public void Flush() {
			Transmit.Flush();
			Receive.Flush();
		}
		
		void FindPaket() {
			//			if ( Receive.Length < 3) return;
			
			// get a hypothetical checksum
			//			var reader = new BinaryReader(Receive);
			//			Receive.Seek(-2,SeekOrigin.End);
			//			ushort crc = reader.ReadUInt16();
			//			var length = Receive.Length;
			//			var payloadLength = length - 2;
			// find up to payloadLength crcs
			
			// match up the current buffer from the reverse
			
		}
		
	}
	
	
	public class Packet: MemoryStream {
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
		public override void Write(byte[] data, int offset, int length) {
			//			base.Write(data,offset,length-2);
			//			ushort given_crc = (ushort)(((ushort)data[data.Length-1])<<8 | (ushort)data[data.Length-2]);
			//			byte[] buffer = new byte[data.Length-2];
			//			Buffer.BlockCopy(data,0,buffer,0,buffer.Length);
			//			ushort current_crc = Crc16.Sum(buffer);
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
			long currentPosition = s.Position;
			s.Seek(offset, SeekOrigin.Begin);
			ushort uCrc16 = 0;
			byte[] abData = new byte[2];
			long uLen = s.Length - 2;
			for (int i=offset; i<uLen; i++) {
				abData[1] = abData[0];
				s.Read(abData,i,1);
				if((uCrc16 & 0x8000) > 0)
				{
					uCrc16 = (ushort) ((uCrc16 & 0x7fff) << 1);
					uCrc16 ^= polynomial;
				}
				else {
					uCrc16 <<= 1;
				}
				uCrc16 ^= (ushort) ((ushort) (abData[0]) | ((ushort)(abData[1]) << 8));
			}
			s.Seek(currentPosition, SeekOrigin.Begin);
			return(uCrc16);
		}
	}
	
	
	public class RingStream : Stream {
		int Size;
		public int head, length, tail;
		public byte[] buffer;
		
		public RingStream(int capacity) {
			Size = capacity;
			buffer = new byte[Size];
			tail = 0;
			head = 0;
			length = 0;
		}
		
		public override bool CanRead { get { return true; } }
		public override bool CanSeek { get { return true; } }
		public override bool CanWrite { get { return true; } }
		public override long Length { get { return length; } }
		
		long position = 0;
		public override long Position {
			get { return position; }
			set { position = value; }
		}
		
		public override void Flush() {
			//
		}
		public override long Seek(long p, SeekOrigin origin) {
			switch(origin) {
				default:
				case SeekOrigin.Begin:
				position = p;
				break;
				case SeekOrigin.Current:
				position += p;
				break;
				case SeekOrigin.End:
				position = Length - p;
				break;
			}
			return Position;
		}
		
		public override void SetLength(long l) {
			length = Math.Max(0, Math.Min(Size, (int) l));
			if ( Position > l) {
				Position = l;
			}
		}
		
		public override int Read(byte[] data, int offset, int l) {
			if ( head <= tail ) {
				l = Math.Min(length, l);
				Buffer.BlockCopy(buffer,head,data, offset, l);
			} else {
				l = Math.Min(length, l);
				Buffer.BlockCopy(buffer,head, data, offset, head - Size);
				//				Buffer.BlockCopy(buffer,tail,data, offset, l - Size - 1);
			}
			return l;
		}
		
		public override void WriteByte(byte data) {
			// update the length
			length = length == Size ? length : length + 1;
			// advance the tail
			if (length > 1) {
				tail = (tail+1) % Size;
				if (length < Size) {
					head = 0;
				}
				else if (tail == head) {
					head = (tail+1) % Size;
				}
				
			}
			buffer[tail] = data;
		}
		
		public override void Write(byte[] data, int offset, int l) {
			if (l <= 0 || data.Length <= 0) return;
			byte[] tmp = new byte[l];
			Buffer.BlockCopy(data, offset, tmp, 0, l);
			foreach (byte b in tmp) {
				WriteByte(b);
			}
		}
		
		public void ReadFrom(Stream s) {
			if (s.Length <= 0 || !s.CanRead) return;
			if (s.Position != 0) s.Seek(0, SeekOrigin.Begin);
			if (s.Length >= Size) {
				s.Seek(-Size, SeekOrigin.End);
				s.Read(buffer,0,Size);
				SetLength(Size);
				Position = Size;
				head = 0;
				tail = Size-1;
			}
			else {
				if (s.Length == 1) {
					WriteByte((byte)s.ReadByte());
				} else {
					byte[] tmp = new byte[s.Length];
					s.Read(tmp,0,tmp.Length);
					Write(tmp,0,tmp.Length);
				}
			}
		}
	}
}
