// Author: Jonas De Maeseneer

using Unity.Entities;

namespace DotsPersistency
{
    // Add this component to a scene section entity to load it & automatically be persistable
    // Remove it to unload your scene & automatically persist the latest data (unless DisableAutoPersistOnUnload is on it)
    public struct RequestPersistentSceneSectionLoaded : IComponentData
    {
        public SceneLoadFlags LoadFlags;
        
        private Stage _currentLoadingStage;
        public Stage CurrentLoadingStage
        {
            get => _currentLoadingStage;
            internal set => _currentLoadingStage = value;
        }
        
        public enum Stage : byte
        {
            InitialStage = 0,
            WaitingForSceneLoad,
            Complete
        }
    }

    // When RequestPersistentSceneSectionLoaded.Stage is Complete this gets added
    public struct PersistentSceneSectionLoadComplete : IComponentData
    {
    }

    // Use this if you don't want the EndFramePersistencySystem to persist the scene section before unload
    public struct DisableAutoPersistOnUnload : IComponentData
    {
    }
    
    // Only used to make distinction between standard loaded scene section & a persistent scene section that needs to be unloaded  
    internal struct PersistentSceneSection : IComponentData
    {
    }
}