using System;
using Svelto.Common;
using Svelto.ECS.Internal;

namespace Svelto.ECS.Serialization
{
    public class SerializableEntityBuilder<T> : EntityBuilder<T>, ISerializableEntityBuilder
        where T : unmanaged, IEntityStruct
    {
        public static readonly uint SIZE = UnsafeUtils.SizeOf<T>();

        internal T _lastSerialisedValue; // TODO: I have to think this through
        bool _valueSerialized;

        static SerializableEntityBuilder()
        {}

        public SerializableEntityBuilder()
        {
            _serializers = new ISerializer<T>[(int) SerializationType.Length];
            for (int i = 0; i < (int) SerializationType.Length; i++)
            {
                _serializers[i] = new DefaultSerializer<T>();
            }
        }

        public SerializableEntityBuilder(params ValueTuple<SerializationType, ISerializer<T>>[] serializers)
        {
            _serializers = new ISerializer<T>[(int) SerializationType.Length];
            for (int i = 0; i < serializers.Length; i++)
            {
                ref (SerializationType, ISerializer<T>) s = ref serializers[i];
                _serializers[(int) s.Item1] = s.Item2;
            }

            // Just in case the above are the same type
            for (int i = 0; i < (int) SerializationType.Length; i++)
            {
                if (_serializers[i] == null) _serializers[i] = new DontSerialize<T>();
            }
        }

        public void Serialize(uint entityID, ITypeSafeDictionary dictionary,
            ISerializationData serializationData, SerializationType serializationType)
        {
            using (_pp.Sample("SerializeEntityStruct"))
            {
                ISerializer<T> serializer = _serializers[(int)serializationType];

                var safeDictionary = (TypeSafeDictionary<T>) dictionary;
                if (safeDictionary.TryFindIndex(entityID, out uint index) == false)
                {
                    throw new ECSException("Entity Serialization failed");
                }

                T[] values = safeDictionary.GetValuesArray(out _);
                ref T val = ref values[index];

                serializationData.dataPos = (uint) serializationData.data.Count;

                serializationData.data.ExpandBy(serializer.size);
                using (_pp.Sample("serializer.Serialize"))
                    serializer.Serialize(val, serializationData);
            }
        }

        public void Deserialize(uint entityID, ITypeSafeDictionary dictionary,
            ISerializationData serializationData, SerializationType serializationType)
        {
            using (_pp.Sample("Deserialize"))
            {
                ISerializer<T> serializer = _serializers[(int) serializationType];

                // Handle the case when an entity struct is gone
                var safeDictionary = (TypeSafeDictionary<T>) dictionary;
                if (safeDictionary.TryFindIndex(entityID, out uint index) == false)
                {
                    throw new ECSException("Entity Deserialization failed");
                }

                T[] values = safeDictionary.GetValuesArray(out _);
                ref T val = ref values[index];

                using (_pp.Sample("serializer.Deserialize"))
                    serializer.Deserialize(ref val, serializationData);
            }
        }

        public void Deserialize(ISerializationData serializationData
            , in EntityStructInitializer initializer, SerializationType serializationType)
        {
            using (_pp.Sample("Deserialize initializer"))
            {
                ISerializer<T> serializer = _serializers[(int) serializationType];

                _lastSerialisedValue = initializer.Get<T>();

                using (_pp.Sample("serializer.Deserialize"))
                    _valueSerialized = serializer.Deserialize(ref _lastSerialisedValue, serializationData);
            }
        }

        public void Set(ref EntityStructInitializer initializer)
        {
            if (_valueSerialized)
                initializer.Init(_lastSerialisedValue);
        }

        readonly ISerializer<T>[] _serializers;

        PlatformProfiler _pp = new PlatformProfiler();
    }
}