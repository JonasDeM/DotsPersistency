using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace DotsPersistency
{
    public struct PersistentDataContainer : IDisposable
    {
        private NativeArray<byte> _data;
        private NativeArray<PersistencyArchetypeDataLayout> _persistenceArchetypesDataLayouts;
        internal NativeArray<PersistencyArchetypeDataLayout> PersistencyArchetypeDataLayouts => _persistenceArchetypesDataLayouts;
            
        public SceneSection SceneSection => _sceneSection;
        private SceneSection _sceneSection;

        public int Tick
        {
            get => _tick;
            set => _tick = value;
        }
        private int _tick;

        public int Count => _persistenceArchetypesDataLayouts.Length;

        public PersistentDataContainer(SceneSection sceneSection, NativeArray<PersistencyArchetypeDataLayout> persistenceArchetypesDataLayouts, Allocator allocator)
        {
            _sceneSection = sceneSection;
            _tick = 0;
            _persistenceArchetypesDataLayouts = persistenceArchetypesDataLayouts;

            int size = 0;
            foreach (var persistenceArchetype in persistenceArchetypesDataLayouts)
            {
                size += persistenceArchetype.Amount * persistenceArchetype.SizePerEntity;
            }
            _data = new NativeArray<byte>(size, allocator);
        }
            
        public PersistencyArchetypeDataLayout GetPersistenceArchetypeDataLayoutAtIndex(int index)
        {
            return _persistenceArchetypesDataLayouts[index];
        }
        
        public NativeArray<byte> GetSubArrayAtIndex(int index)
        {
            var archetype = _persistenceArchetypesDataLayouts[index];
            return _data.GetSubArray(archetype.Offset, archetype.Amount * archetype.SizePerEntity);
        }

        public NativeArray<byte> GetRawData()
        {
            return _data;
        }

        public PersistentDataContainer GetCopy()
        {
            PersistentDataContainer copy = this;
            copy._data = new NativeArray<byte>(_data, Allocator.Persistent);
            return copy;
        }

        public void Dispose()
        {
            if (_data.IsCreated)
            {
                _data.Dispose();
            }
        }
    }

}