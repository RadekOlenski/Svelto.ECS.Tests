﻿using System;
using Svelto.Common;
using Svelto.ECS.Internal;
using Svelto.ECS.Serialization;

namespace Svelto.ECS
{
    //todo: this should not be at framework level
    public enum SerializationType
    {
        Network,
        Storage,

        Length
    }

    public partial class EnginesRoot
    {
        sealed class EntitySerialization : IEntitySerialization
        {
            public void SerializeEntity(EGID egid, ISerializationData serializationData,
                SerializationType serializationType)
            {
                using (_pp.Sample("SerializeEntity"))
                {
                    var entitiesDb = _enginesRoot._entitiesDB;

                    //needs to retrieve the meta data associated with the entity
                    ref var serializableEntityStruct = ref entitiesDb.QueryEntity<SerializableEntityStruct>(egid);
                    uint descriptorHash = serializableEntityStruct.descriptorHash;

                    SerializationDescriptorMap serializationDescriptorMap = _enginesRoot.serializationDescriptorMap;
                    var entityDescriptor = serializationDescriptorMap.GetDescriptorFromHash(descriptorHash);
                    var entityStructsToSerialise = entityDescriptor.entitiesToSerialize;

                    var header =
                        new SerializableEntityHeader(descriptorHash, egid, (byte) entityStructsToSerialise.Count);
                    header.Copy(serializationData);

                    for (int index = 0; index < entityStructsToSerialise.Count; index++)
                    {
                        var entityBuilder = entityStructsToSerialise[index];

                        serializationData.BeginNextEntityStruct();
                        SerializeEntityStruct(egid, entityBuilder, serializationData, serializationType);
                    }
                }
            }

            public EntityStructInitializer DeserializeNewEntity(EGID egid, ISerializationData serializationData,
                SerializationType serializationType)
            {
                using (_pp.Sample("DeserializeNewEntity"))
                {
                    //todo: SerializableEntityHeader may be needed to be customizable
                    var serializableEntityHeader = new SerializableEntityHeader(serializationData);

                    uint descriptorHash = serializableEntityHeader.descriptorHash;
                    SerializationDescriptorMap serializationDescriptorMap = _enginesRoot.serializationDescriptorMap;
                    var factory = serializationDescriptorMap.GetSerializationFactory(descriptorHash);
                    var entityDescriptor = serializationDescriptorMap.GetDescriptorFromHash(descriptorHash);

                    //default factory
                    if (factory == null)
                    {
                        var initializer = _enginesRoot.BuildEntity(egid, entityDescriptor.entitiesToBuild);

                        DeserializeEntityStructs(serializationData, entityDescriptor, initializer, serializationType);

                        entityDescriptor.FillInitializer(ref initializer);

                        return initializer;
                    }

                    //custom factory
                    return factory.BuildDeserializedEntity(egid, serializationData, entityDescriptor, serializationType,
                        this);
                }
            }

            public void DeserializeEntity(ISerializationData serializationData, SerializationType serializationType)
            {
                using (_pp.Sample("DeserializeEntity"))
                {
                    var serializableEntityHeader = new SerializableEntityHeader(serializationData);

                    EGID egid = serializableEntityHeader.egid;

                    DeserializeEntityInternal(serializationData, egid, serializableEntityHeader, serializationType);
                }
            }

            public void DeserializeEntity(EGID egid, ISerializationData serializationData,
                SerializationType serializationType)
            {
                using (_pp.Sample("DeserializeEntity egid"))
                {
                    var serializableEntityHeader = new SerializableEntityHeader(serializationData);

                    DeserializeEntityInternal(serializationData, egid, serializableEntityHeader, serializationType);
                }
            }

            public void DeserializeEntityStructs(ISerializationData serializationData,
                ISerializableEntityDescriptor entityDescriptor,
                in EntityStructInitializer initializer, SerializationType serializationType)
            {
                using (_pp.Sample("DeserializeEntityStructs"))
                {
                    for (int index = 0; index < entityDescriptor.entitiesToSerialize.Count; ++index)
                    {
                        var serializableEntityBuilder = entityDescriptor.entitiesToSerialize[index];

                        serializationData.BeginNextEntityStruct();
                        serializableEntityBuilder.Deserialize(serializationData, initializer, serializationType);
                    }
                }
            }

            public void DeserializeEntityToSwap(EGID localEgid, EGID toEgid)
            {
                EntitiesDB entitiesDb = _enginesRoot._entitiesDB;
                ref var serializableEntityStruct = ref entitiesDb.QueryEntity<SerializableEntityStruct>(localEgid);

                SerializationDescriptorMap serializationDescriptorMap = _enginesRoot.serializationDescriptorMap;
                uint descriptorHash = serializableEntityStruct.descriptorHash;
                var entityDescriptor = serializationDescriptorMap.GetDescriptorFromHash(descriptorHash);

                var entitySubmitOperation = new EntitySubmitOperation(
                    EntitySubmitOperationType.Swap,
                    localEgid,
                    toEgid,
                    entityDescriptor.entitiesToBuild);

                _enginesRoot.CheckRemoveEntityID(localEgid);
                _enginesRoot.CheckAddEntityID(toEgid);

                _enginesRoot.QueueEntitySubmitOperation(entitySubmitOperation);
            }

            public void DeserializeEntityToDelete(EGID egid)
            {
                EntitiesDB entitiesDB = _enginesRoot._entitiesDB;
                ref var serializableEntityStruct = ref entitiesDB.QueryEntity<SerializableEntityStruct>(egid);
                uint descriptorHash = serializableEntityStruct.descriptorHash;

                SerializationDescriptorMap serializationDescriptorMap = _enginesRoot.serializationDescriptorMap;
                var entityDescriptor = serializationDescriptorMap.GetDescriptorFromHash(descriptorHash);

                _enginesRoot.CheckRemoveEntityID(egid);

                var entitySubmitOperation = new EntitySubmitOperation(
                    EntitySubmitOperationType.Remove,
                    egid,
                    egid,
                    entityDescriptor.entitiesToBuild);

                _enginesRoot.QueueEntitySubmitOperation(entitySubmitOperation);
            }

            public void RegisterSerializationFactory<T>(IDeserializationFactory deserializationFactory)
                where T : ISerializableEntityDescriptor, new()
            {
                SerializationDescriptorMap serializationDescriptorMap = _enginesRoot.serializationDescriptorMap;
                serializationDescriptorMap.RegisterSerializationFactory<T>(deserializationFactory);
            }

            internal EntitySerialization(EnginesRoot enginesRoot)
            {
                _enginesRoot = enginesRoot;
            }

            void SerializeEntityStruct(EGID entityGID, ISerializableEntityBuilder entityBuilder,
                ISerializationData serializationData, SerializationType serializationType)
            {
                using (_pp.Sample("SerializeEntityStruct"))
                {
                    uint groupId = entityGID.groupID;
                    Type entityType = entityBuilder.GetEntityType();
                    if (!_enginesRoot._entitiesDB.UnsafeQueryEntityDictionary(groupId, entityType,
                        out var safeDictionary))
                    {
                        throw new Exception("Entity Serialization failed");
                    }

                    entityBuilder.Serialize(entityGID.entityID, safeDictionary, serializationData, serializationType);
                }
            }

            void DeserializeEntityInternal(ISerializationData serializationData, EGID egid,
                SerializableEntityHeader serializableEntityHeader, SerializationType serializationType)
            {
                using (_pp.Sample("DeserializeEntityInternal"))
                {
                    SerializationDescriptorMap descriptorMap = _enginesRoot.serializationDescriptorMap;
                    var entityDescriptor = descriptorMap.GetDescriptorFromHash(serializableEntityHeader.descriptorHash);

                    foreach (var serializableEntityBuilder in entityDescriptor.entitiesToSerialize)
                    {
                        _enginesRoot._entitiesDB.UnsafeQueryEntityDictionary(egid.groupID,
                            serializableEntityBuilder.GetEntityType(), out var safeDictionary);

                        serializationData.BeginNextEntityStruct();
                        serializableEntityBuilder.Deserialize(egid.entityID, safeDictionary, serializationData,
                            serializationType);
                    }
                }
            }

            readonly EnginesRoot _enginesRoot;

            PlatformProfiler _pp = new PlatformProfiler();
        }

        public IEntitySerialization GenerateEntitySerializer()
        {
            return new EntitySerialization(this);
        }
    }
}