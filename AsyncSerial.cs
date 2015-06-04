using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

using VVVV.Core.Logging;

namespace Muthesius.SickLRF {
	
	internal class SickDevice : ASerialPort {
		public readonly PacketBuilder Packets;
		
		
		bool initialBaudrateSet = false;
		public SickDevice() : base() {
			Packets = new PacketBuilder();
			ReadPort = ReadPackets;
			ReadBufferSize = 4096;
			base.ReceivedBytesThreshold = 2;
		}
		
		public delegate void Packetnotifier(Packet p);
		public event Packetnotifier PacketReceived;
		
		protected void ReadPackets() {
			byte[] buff = new byte[1024];
			int readBytes = BaseStream.Read(buff,0,buff.Length);
			if (readBytes > 0) {
				lock(Packets) {
					Packets.Add(buff,readBytes);
					while (Packets.HasPacket) {
						Packet p = Packets.RemovePacket();
						p.TimeStamp = DateTime.Now;
						if(PacketReceived != null && p != null) {
							PacketReceived(p);
						}
					}
				}
			}
		}
		
		int _baudrate = 9600;
		public new int Baudrate {
			set {
				bool was = Enable;
				base.BaudRate = value;
				Enable = was;
				initialBaudrateSet = true;
			}
			get {
				return base.BaudRate;
			}
		}
		public new string PortName {
			set {
				bool was = Enable;
				base.PortName = value;
				Enable = was;
			}
			get {
				return base.PortName;
			}
		}
		
		#region Properties
		public int BadPackets {
			get { lock(Packets) return Packets.BadPackets; }
		}
		public int Noise {
			get { lock(Packets) return Packets.Noise; }
		}
		#endregion Properties
		
		public ILogger Logger;
		public void LogInfo(string format, params object[] args)
		{
			if(Logger == null) return;
			string msg = string.Format(format, args);
			Logger.Log(LogType.Debug,msg);
		}
		
	}
	
	#region Async Serial
	internal class ASerialPort : SerialPort {
		
		// TODO Wrap the ReadPort Action with a lock to prevent
		protected Action ReadPort;
		
		Thread ReadThread;
		bool IsReading;
		object ReadFlag = new object();
		
		public delegate void DataNotifier(byte[] data);
		public new event DataNotifier DataReceived;
		
		public delegate void ErrorNotifier(Exception e);
		public event ErrorNotifier DidError;
		
		// keep contructor simple to handle baudrate change through properties.
		public ASerialPort() : base() {
			ReadThread = new Thread(new ThreadStart(Reader));
			ReadThread.IsBackground = true;
			ReadTimeout = Timeout.Infinite; // set an infinite timeout on read
			WriteTimeout = 100; // only wait for 10 ms to send the data
			ReadBufferSize = 1024; // use a small Readbuffer size
			ReadPort = ReadPortData;
		}
		
		public new void Dispose() {
			Enable = false;
			if (ReadThread.IsAlive) {
				ReadThread.Abort();
			}
			base.Dispose();
		}
		
		// Just a simple error send helper to chek if the vent has listeners
		protected void SendError(Exception err) {
			if (DidError != null) DidError(err);
		}
		
		public bool Enable {
			set {
				if(value) {
					try {
						Open();
						IsReading = true;
						StartReader();
					} catch (Exception e) {
						string msg = e.Message;
					}
				} else {
					// Stop Reading
					// if is has been started before just suspend the thread
					try {
						StopReader();
					} catch(Exception e) {
						SendError(e);
					} finally {
						IsReading = false;
						Close();
					}
				}
			}
			get {
				return IsReading;
			}
		}
		
		protected void StartReader() {
			if (!ReadThread.IsAlive) ReadThread.Start();
			else Monitor.Exit(ReadFlag);
		}
		
		protected void StopReader() {
			if (ReadThread.IsAlive) PauseReader();
		}
		
		void PauseReader() {
			Monitor.Enter(ReadFlag);
		}
		
		void Reader() {
			while(true) {
				try {
					if(Monitor.IsEntered(ReadFlag) || ReadPort == null) Thread.Sleep(200);
					else ReadPort();
				} catch(ThreadAbortException tae) {
					// Stop reading
				} catch(InvalidOperationException _) {
					// do not catch IO invalid operation errors, just wait a bit
					Thread.Sleep(200);
				} catch(System.IO.IOException _) {
					Thread.Sleep(200);
				} catch(Exception e) {
					// deal with something else, e.g. read timeout
					SendError(e);
				} finally {
					// provide a finally clause that the abort exception is caught
				}
			}
		}
		
		protected virtual void ReadPortData() {
			byte[] buff = new byte[1024];
			int readBytes = BaseStream.Read(buff,0,buff.Length);
			
			if (DataReceived != null) {
				byte[] tmp = new byte[readBytes];
				Buffer.BlockCopy(buff,0,tmp,0,readBytes);
				DataReceived(tmp);
			}
		}
	}
	#endregion Async Serial
}