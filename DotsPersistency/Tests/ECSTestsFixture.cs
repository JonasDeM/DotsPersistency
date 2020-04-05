using System.Linq;
using NUnit.Framework;
using Unity.Entities;

namespace DotsPersistency.Tests
{
    public struct EcsTestData : IComponentData
    {
        public int value;
        
        public EcsTestData(int value)
        {
            this.value = value;
        }
    }
    public struct EcsTestFloatData2 : IComponentData
    {
        public float Value0;
        public float Value1;
    }
    public struct EcsTestData5 : IComponentData
    {
        public EcsTestData5(int value)
        {
            value0 = value;
            value1 = value;
            value2 = value;
            value3 = value;
            value4 = value;
        }
        
        public int value0;
        public int value1;
        public int value2;
        public int value3;
        public int value4;
    }
    
    public abstract class EcsTestsFixture
    {
        protected World m_PreviousWorld;
        protected World World;
        protected EntityManager m_Manager;
        protected EntityManager.EntityManagerDebug m_ManagerDebug;

        protected int StressTestEntityCount = 1000;

        [SetUp]
        public virtual void Setup()
        {
            m_PreviousWorld = World.DefaultGameObjectInjectionWorld;
            World = World.DefaultGameObjectInjectionWorld = new World("Test World");
            m_Manager = World.EntityManager;
            m_ManagerDebug = new EntityManager.EntityManagerDebug(m_Manager);
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (m_Manager != null && m_Manager.IsCreated)
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
                m_Manager = null;
            }
        }
        
        class EntityForEachSystem : ComponentSystem
        {
            protected override void OnUpdate() {  }

            public EntityQueryBuilder GetQueryBuilder()
            {
                return Entities;
            }
        }
        protected EntityQueryBuilder Entities => World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<EntityForEachSystem>().GetQueryBuilder();
    }
}