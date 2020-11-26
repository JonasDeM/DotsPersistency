using System;
using Unity.Entities;

namespace DotsPersistency
{
    public struct RequestPersistentSceneSectionLoaded : IComponentData
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

    public struct PersistentSceneSectionLoadComplete : IComponentData
    {
    }

    public struct DisableAutoPersistOnUnload : IComponentData
    {
    }
}