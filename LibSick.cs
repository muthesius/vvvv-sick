using System;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;

namespace Sick {
	
	public class SickDevice: IDisposable {
		public Stream Transmit, Receive;
		public SickDevice() {
			Transmit = new MemoryStream();
			Receive = new MemoryStream();
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
		
		void Send(Command cmd, byte[] data) {
			// Build the packet
		}
		
		public bool ScanContinuous {
			set {
				Packet.StartContinuousOutput.CopyTo(Transmit);
			}
		}
		
		public void CheckPacket(Stream data) {
			
		}
		
		public Packet MakeTestPacket() {
			var s = new Packet();
			byte[] data = {0x02, 0x00, 0x01, 0x00, 0x31, 0x15, 0x12};
			s.Write(data,0,data.Length);
			return s;
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
			base.Write(data,offset,length-2);
			ushort given_crc = (ushort)(((ushort)data[data.Length-1])<<8 | (ushort)data[data.Length-2]);
			byte[] buffer = new byte[data.Length-2];
			Buffer.BlockCopy(data,0,buffer,0,buffer.Length);
			ushort current_crc = Crc16.Sum(buffer);
			chksm.Seek(0,SeekOrigin.Begin);
			chksm.Write(data,data.Length-2,2);
			Checksums[0] = given_crc;
			Checksums[1] = current_crc;
			IsValid = current_crc == given_crc;
			base.Write(data,data.Length-2,2);
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
	}
	
	
	public enum Command {
		Status = 0x31
	}
	
	static class Crc16
	{
		const ushort polynomial = 0x8005;
		
		public static ushort Sum(byte[] CommData) {
			ushort uCrc16 = 0;
			byte[] abData = new byte[2];
			long uLen = CommData.Length-0;
			//uCrc16 = 0; abData[0] = 0x00;
			for (int i=0; i<CommData.Length; i++) {
				abData[1] = abData[0];
				abData[0] = CommData[i];
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
			return(uCrc16);
		}
	}
}
