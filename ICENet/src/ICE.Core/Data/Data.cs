using System.Collections.Generic;
using System.Text;
using System;

namespace ICENet.Core.Data
{
    public partial struct Data
    {
        private List<byte> _buffer;

        private int _readPosition;

        public int Length => _buffer.Count;

        public Data(int readPosition = 0)
        {
            _buffer = new List<byte>();
            _readPosition = readPosition;
        }

        public void LoadBytes(byte[] bytes) => _buffer.AddRange(bytes);

        public int Write(byte[] bytes)
        {
            _buffer.AddRange(bytes);
            return _buffer.Count;
        }

        public void Write(byte value)
        {
            _buffer.Add(value);
        }

        public void Write(short value)
        {
            var bytes = BitConverter.GetBytes(value);
            _buffer.AddRange(bytes);
        }

        public void Write(ushort value)
        {
            byte[] bytes = new byte[2];
            bytes[0] = (byte)(value & 0xFF);
            bytes[1] = (byte)((value >> 8) & 0xFF);
            _buffer.AddRange(bytes);
        }

        public void Write(int value)
        {
            var bytes = BitConverter.GetBytes(value);
            _buffer.AddRange(bytes);
        }

        public void Write(long value)
        {
            var bytes = BitConverter.GetBytes(value);
            _buffer.AddRange(bytes);
        }

        public void Write(float value)
        {
            var bytes = BitConverter.GetBytes(value);
            _buffer.AddRange(bytes);
        }

        public void Write(double value)
        {
            var bytes = BitConverter.GetBytes(value);
            _buffer.AddRange(bytes);
        }

        public void Write(decimal value)
        {
            var bits = decimal.GetBits(value);

            foreach (var bit in bits)
            {
                var bytes = BitConverter.GetBytes(bit);
                _buffer.AddRange(bytes);
            }
        }

        public void Write(char value)
        {
            var bytes = BitConverter.GetBytes(value);
            _buffer.AddRange(bytes);
        }

        public void Write(bool value)
        {
            var bytes = BitConverter.GetBytes(value);
            _buffer.AddRange(bytes);
        }

        public void Write(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);

            var stringSize = bytes.Length;
            Write(stringSize);

            _buffer.AddRange(bytes);
        }

        public byte[] ReadBytes(int length)
        {
            if (_readPosition + length > _buffer.Count)
                throw new IndexOutOfRangeException("Read position is out of range");

            byte[] bytes = _buffer.GetRange(_readPosition, length).ToArray();
            _readPosition += length;
            return bytes;
        }

        public byte ReadByte()
        {
            if (_readPosition + sizeof(byte) > _buffer.Count)
                throw new IndexOutOfRangeException("Read position is out of range");

            byte[] bytes = _buffer.GetRange(_readPosition, sizeof(byte)).ToArray();
            _readPosition += sizeof(byte);
            return bytes[0];
        }

        public short ReadInt16()
        {
            if (_readPosition + sizeof(short) > _buffer.Count)
                throw new IndexOutOfRangeException("Read position is out of range");

            byte[] bytes = _buffer.GetRange(_readPosition, sizeof(short)).ToArray();
            _readPosition += sizeof(short);
            return BitConverter.ToInt16(bytes, 0);
        }

        public ushort ReadUInt16()
        {
            if (_readPosition + sizeof(ushort) > _buffer.Count)
                throw new IndexOutOfRangeException("Read position is out of range");

            byte[] bytes = _buffer.GetRange(_readPosition, sizeof(ushort)).ToArray();
            _readPosition += sizeof(ushort);

            ushort value = (ushort)(bytes[0] | (bytes[1] << 8));
            return value;
        }

        public int ReadInt32()
        {
            if (_readPosition + sizeof(int) > _buffer.Count)
                throw new IndexOutOfRangeException("Read position is out of range");

            byte[] bytes = _buffer.GetRange(_readPosition, sizeof(int)).ToArray();
            _readPosition += sizeof(int);
            return BitConverter.ToInt32(bytes, 0);
        }

        public long ReadInt64()
        {
            if (_readPosition + sizeof(long) > _buffer.Count)
                throw new IndexOutOfRangeException("Read position is out of range");

            byte[] bytes = _buffer.GetRange(_readPosition, sizeof(long)).ToArray();
            _readPosition += sizeof(long);
            return BitConverter.ToInt64(bytes, 0);
        }

        public float ReadSingle()
        {
            if (_readPosition + sizeof(float) > _buffer.Count)
                throw new IndexOutOfRangeException("Read position is out of range");

            byte[] bytes = _buffer.GetRange(_readPosition, sizeof(float)).ToArray();
            _readPosition += sizeof(float);
            return BitConverter.ToSingle(bytes, 0);
        }

        public double ReadDouble()
        {
            if (_readPosition + sizeof(double) > _buffer.Count)
                throw new IndexOutOfRangeException("Read position is out of range");

            byte[] bytes = _buffer.GetRange(_readPosition, sizeof(double)).ToArray();
            _readPosition += sizeof(double);
            return BitConverter.ToDouble(bytes, 0);
        }

        public decimal ReadDecimal()
        {
            if (_readPosition + sizeof(decimal) > _buffer.Count)
                throw new IndexOutOfRangeException("Read position is out of range");

            int[] bits = new int[4];
            for (int i = 0; i < 4; i++)
            {
                byte[] bytes = _buffer.GetRange(_readPosition, sizeof(int)).ToArray();
                _readPosition += sizeof(int);
                bits[i] = BitConverter.ToInt32(bytes, 0);
            }

            return new decimal(bits);
        }

        public char ReadChar()
        {
            if (_readPosition + sizeof(char) > _buffer.Count)
                throw new IndexOutOfRangeException("Read position is out of range.");

            byte[] bytes = _buffer.GetRange(_readPosition, sizeof(char)).ToArray();
            _readPosition += sizeof(char);
            return BitConverter.ToChar(bytes, 0);
        }

        public bool ReadBoolean()
        {
            if (_readPosition + sizeof(bool) > _buffer.Count)
                throw new IndexOutOfRangeException("Read position is out of range.");

            byte[] bytes = _buffer.GetRange(_readPosition, sizeof(bool)).ToArray();
            _readPosition += sizeof(bool);
            return BitConverter.ToBoolean(bytes, 0);
        }

        public string ReadString()
        {
            int stringSize = ReadInt32();

            if (_readPosition + stringSize > _buffer.Count)
                throw new IndexOutOfRangeException("Read position is out of range.");

            byte[] bytes = _buffer.GetRange(_readPosition, stringSize).ToArray();
            _readPosition += stringSize;
            return Encoding.UTF8.GetString(bytes);
        }

        public byte[] ToArray() => _buffer.ToArray();
    }
}
