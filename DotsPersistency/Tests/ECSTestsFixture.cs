// Author: Jonas De Maeseneer

using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DotsPersistency.Tests
{
    public abstract class EcsTestsFixture
    {
        protected World m_PreviousWorld;
        protected World World;
        protected EntityManager m_Manager;
        protected EntityManager.EntityManagerDebug m_ManagerDebug;

        [SetUp]
        public virtual void Setup()
        {
            m_PreviousWorld = World.DefaultGameObjectInjectionWorld;
            World = World.DefaultGameObjectInjectionWorld = new World("Test World");
            m_Manager = World.EntityManager;
            m_ManagerDebug = new EntityManager.EntityManagerDebug(m_Manager);

            World.GetOrCreateSystem<EntityForEachSystem>();
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (m_Manager != default && World.IsCreated)
            {
                // Clean up systems before calling CheckInternalConsistency because we might have filters etc
                // holding on SharedComponentData making checks fail
                while (World.Systems.Count > 0)
                {
                    World.DestroySystem(World.Systems[0]);
                }

                m_ManagerDebug.CheckInternalConsistency();

                World.Dispose();
                World = null;

                World.DefaultGameObjectInjectionWorld = m_PreviousWorld;
                m_PreviousWorld = null;
                m_Manager = default;
            }

            foreach (var rootGameObject in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Object.DestroyImmediate(rootGameObject);
            }
        }
        
        [DisableAutoCreation]
        class EntityForEachSystem : ComponentSystem
        {
            protected override void OnUpdate() {  }

            public EntityQueryBuilder GetQueryBuilder()
            {
                return Entities;
            }
        }
        protected EntityQueryBuilder Entities => World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<EntityForEachSystem>().GetQueryBuilder();
        
        protected static PersistencySettings CreateTestSettings()
        {
            PersistencySettings settings = ScriptableObject.CreateInstance<PersistencySettings>();
            settings.AddPersistableTypeInEditor(typeof(EcsTestData).FullName);
            settings.AddPersistableTypeInEditor(typeof(EcsTestFloatData2).FullName);
            settings.AddPersistableTypeInEditor(typeof(EcsTestData5).FullName);
            settings.AddPersistableTypeInEditor(typeof(DynamicBufferData1).FullName);
            settings.AddPersistableTypeInEditor(typeof(DynamicBufferData2).FullName);
            settings.AddPersistableTypeInEditor(typeof(DynamicBufferData3).FullName);
            settings.AddPersistableTypeInEditor(typeof(EmptyEcsTestData).FullName);
            // Reset it so the new types are initialized
            settings.OnDisable();
            settings.OnEnable();
            return settings;
        }
    }
    
    
    public struct EcsTestData : IComponentData
    {
        public int Value;
        
        public EcsTestData(int value)
        {
            this.Value = value;
        }
    }
    public struct EcsTestFloatData2 : IComponentData
    {
        public float Value0;
        public float Value1;
        
        public EcsTestFloatData2(float value)
        {
            this.Value0 = value;
            this.Value1 = value;
        }
    }
    public struct EcsTestData5 : IComponentData
    {
        public EcsTestData5(int value)
        {
            Value0 = value;
            Value1 = value;
            Value2 = value;
            Value3 = value;
            Value4 = value;
        }
        
        public int Value0;
        public int Value1;
        public int Value2;
        public int Value3;
        public int Value4;
    }
    
    
    [InternalBufferCapacity(2)]
    public struct DynamicBufferData1 : IBufferElementData
    {
        public int Value;
            
        public override string ToString()
        {
            return Value.ToString();
        }
    }
    
    public struct DynamicBufferData2 : IBufferElementData
    {
#pragma warning disable 649
        public float Value;
#pragma warning restore 649
            
        public override string ToString()
        {
            return Value.ToString();
        }
    }
    
    public struct DynamicBufferData3 : IBufferElementData
    {
        public byte Value;
            
        public override string ToString()
        {
            return Value.ToString();
        }
    }
    
    public struct EmptyEcsTestData : IComponentData
    {
            
    }
}