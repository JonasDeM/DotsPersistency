using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public struct MyBufferData1 : IBufferElementData
{
    public int Data;
}

public class BufferData : MonoBehaviour, IConvertGameObjectToEntity
{
    public int Data = 5;

    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddBuffer<MyBufferData1>(entity);
    }
}
