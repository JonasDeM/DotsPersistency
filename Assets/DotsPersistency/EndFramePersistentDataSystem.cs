// Author: Jonas De Maeseneer

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace DotsPersistency
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public class EndFramePersistentDataSystem : PersistencyJobSystem
    {
        public PersistentDataStorage PersistentDataStorage { get; private set; }
        private EntityQuery _unloadStreamRequests;
        private EntityCommandBufferSystem _ecbSystem;

        protected override void OnCreate()
        {
            InitializeReadOnly(RuntimePersistableTypesInfo.Load());
            _ecbSystem = World.GetOrCreateSystem<BeginInitializationEntityCommandBufferSystem>();
            PersistentDataStorage = World.GetOrCreateSystem<BeginFramePersistentDataSystem>().PersistentDataStorage;

            _unloadStreamRequests = GetEntityQuery(ComponentType.Exclude<RequestPersistentSceneLoaded>()
                , ComponentType.ReadOnly<RequestSceneLoaded>(), ComponentType.ReadOnly<SceneSectionData>());
            RequireForUpdate(_unloadStreamRequests);
        }
    
        protected override JobHandle OnUpdate(JobHandle inputDependencies)
        {
            JobHandle allPersistJobs = inputDependencies;

            var sceneSectionsToUnload = _unloadStreamRequests.ToComponentDataArray<SceneSectionData>(Allocator.TempJob);
            foreach (SceneSectionData sceneSectionData in sceneSectionsToUnload)
            {
                SceneSection sceneSectionToPersist = new SceneSection()
                {
                    Section = sceneSectionData.SubSectionIndex,
                    SceneGUID = sceneSectionData.SceneGUID
                };
                
                allPersistJobs = JobHandle.CombineDependencies(
                    allPersistJobs,
                    ScheduleCopyToPersistentDataContainer(inputDependencies, sceneSectionToPersist, PersistentDataStorage.GetExistingContainer(sceneSectionToPersist)));
            }
            sceneSectionsToUnload.Dispose();
            
            // this will trigger the actual unload
            _ecbSystem.CreateCommandBuffer().RemoveComponent<RequestSceneLoaded>(_unloadStreamRequests);
            //_ecbSystem.AddJobHandleForProducer(allPersistJobs);

            return allPersistJobs;
        }
    }
}