using System;
using System.IO;
using System.Linq;
namespace Lea.Utils {
	
	class RingStream : Stream {
		byte[] buffer;
		long head, tail, pos, length;
		long count;
		public RingStream(int buffSize) : base() {
			SetLength(0);
			buffer = new byte[buffSize];
			length = 0;
			pos = 0;
			head = 0;
			tail = 0;
		}
		
		long bufferSize;
		public int BufferSize {
			set {
				if (value != bufferSize) {
					bufferSize = value;
					Flush();
				}
			}
			get {
				return (int) bufferSize;
			}
		}
		
		public override bool CanSeek { get { return true; } }
		public override bool CanRead { get { return true; } }
		public override bool CanWrite { get { return true; } }
		public override long Length { get { return count; } }
		public override long Position {
			get { return pos; }
			set { pos = Math.Max(0,Math.Min(length,value)); }
		}
		
		public override void Flush() {
			head = tail = count = 0;
			buffer = new byte[bufferSize];
		}
		
		public override long Seek(long p, SeekOrigin o) {
			switch(o) {
				case SeekOrigin.Begin:
				Position = p;
				break;
				case SeekOrigin.Current:
				Position += p;
				break;
				case SeekOrigin.End:
				Position = length + p;
				break;
			}
			return Position;
		}
		
		public override void SetLength(long l) {
			long diffSize = count - l;
			if (l <= 0) {
				tail = head;
				return;
			}
			l = Math.Min(l, bufferSize);
		}
		
		public int CopyFrom(Stream s) {
//			int c = 0;
//			for (int data = s.ReadByte(); data != -1; data = s.ReadByte()) {
//				WriteByte((byte) data);
//				c++;
//			}
//			return c;
			byte[] tmp = new byte[s.Length];
			s.Read(tmp,0, tmp.Length);
			foreach(byte b in tmp) {
				WriteByte(b);
			}
			return tmp.Length;
		}
		
		public override int Read(byte[] data, int offset, int c) {
			int l;
			if (tail <= head) {
				l = (int) Math.Min(c, head - tail + 1);
				Buffer.BlockCopy(buffer,(int)tail,data,offset,l);
			} else {
				int tailChunk = buffer.Length - (int)tail;
				l = Math.Min(c, tailChunk);
				Buffer.BlockCopy(buffer,(int)tail,data,offset,l);
				if (c > tailChunk) {
					l = Math.Min((int)head + 1, c - l);
					Buffer.BlockCopy(buffer,0,data,offset + tailChunk + 1,l);
					l = tailChunk ;
				}
			}
			return l;
		}
		
		public override void Write(byte[] data, int offset, int count) {
			
		}
		
		public override void WriteByte(byte b) {
			if (count < buffer.Length) count += 1;
			if (count > 1) {
				long nextHead = head + 1;
				if (nextHead >= bufferSize) nextHead = 0;
				long nextTail = nextHead <= tail ? (tail + 1) % bufferSize : tail;
				tail = nextTail;
				head = nextHead;
			}
			buffer[head] = b;
		}
		
		public override string ToString() {
			string values = string.Join(",", buffer.Select(x => x.ToString("X")).ToArray());
			return String.Format(@"Ringbuffer:
				Head: {0},
				Tail: {1},
				length: {2}
				values: {3}", head, tail, count, values);
		}
	}
	
}