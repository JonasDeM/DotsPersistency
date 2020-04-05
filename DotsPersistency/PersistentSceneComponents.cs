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
}