using ICENet.Core.Data;

namespace ICENet.Traffic
{
    public abstract class Packet
    {
        public abstract int Id { get; }

        public abstract bool IsReliable { get; }

        public void Serialize(ref Data data)
        {
            data.Write(Id);

            Write(ref data);
        }

        protected virtual void Write(ref Data data) { }

        public void Deserialize(ref Data data)
        {
            Read(ref data);
        }

        protected virtual void Read(ref Data data) { }
    }
}
