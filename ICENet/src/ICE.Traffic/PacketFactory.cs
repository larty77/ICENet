using System;
using System.Reflection;
using System.Collections.Concurrent;
using System.Collections.Generic;

using ICENet.Core.Data;

namespace ICENet.Traffic
{
    internal sealed class PacketFactory
    {
        public readonly ConcurrentDictionary<int, Type> _registeredPackets;

        public PacketFactory()
        {
            _registeredPackets = new ConcurrentDictionary<int, Type>();
        }

        public bool AddOrUpdate<T>() where T : Packet, new()
        {
            _registeredPackets.AddOrUpdate(new T().Id, typeof(T), (_, existingType) => typeof(T));
            return true;
        }

        public Packet GetInstance(ref Data data) => GetInstance(data.ReadInt32());

        private Packet GetInstance(int id)
        {
            if (IsTypeRegistered(id) is false)
                throw new PacketTypeNotFoundException(id);

            return GetInstance(_registeredPackets[id]);
        }

        private Packet GetInstance(Type type) => (Packet)Activator.CreateInstance(type);

        private bool IsTypeRegistered(int typeId) => _registeredPackets.ContainsKey(typeId);
    }

    internal class PacketTypeNotFoundException : Exception
    {
        internal PacketTypeNotFoundException(MemberInfo type) : base($"Packet type[{type.Name}] was not found") { }

        internal PacketTypeNotFoundException(int typeId) : base($"Packet type with ID[{typeId}] was not found") { }
    }

    internal class PacketTypeUnexpectedException : Exception
    {
        internal PacketTypeUnexpectedException(MemberInfo unexpectedType, MemberInfo targetType) : base($"Unexpected packet type {unexpectedType}, expected {targetType}") { }
    }

    internal static class ConcurrentDictionaryExtensions
    {
        internal static bool ContainsValue<TKey, TValue>
        (this IDictionary<TKey, TValue> dict, TValue value) => dict.Values.Contains(value);
    }
}
