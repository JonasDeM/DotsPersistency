using System;
using Unity.Entities;

namespace DotsPersistency
{
    public struct RequestPersistentSceneLoaded : IComponentData
    {
        public SceneLoadFlags LoadFlags;
        public Stage CurrentLoadingStage;
        public enum Stage : byte
        {
            InitialStage,
            WaitingForContainer,
            WaitingForSceneLoad,
            Complete
        }
    }

    // Persisting on load always happens!
    public enum PersistingStrategy
    { 
        None = 0,
        OnUnLoad = 1,
        OnRequest = 2
    }

    public struct PersistingSceneType : ISharedComponentData
    {
        public PersistingStrategy PersistingStrategy;
    }
}