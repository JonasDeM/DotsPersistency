using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

// These containers are intentionally super specific.
// They should only be used to get around the fact that NativeContainer doesn't want to hold DynamicTypeHandles.

namespace DotsPersistency.Containers
{
    [NativeContainer]
    internal struct TypeHandleArrayDisposeJobData
    {
        [NativeDisableUnsafePtrRestriction]
        public unsafe void* m_Buffer;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public AtomicSafetyHandle m_Safety;
#endif
        public Allocator m_AllocatorLabel;

        public unsafe void Dispose()
        {
            UnsafeUtility.Free(m_Buffer, m_AllocatorLabel);
        }
    }
        
    internal struct TypeHandleArrayDisposeJob : IJob
    {
        public TypeHandleArrayDisposeJobData DisposeJobData;

        public void Execute()
        {
            DisposeJobData.Dispose();
        }
    }
    
    [NativeContainer]
    public struct ComponentTypeHandleArray : IDisposable
    {
        [NativeDisableUnsafePtrRestriction] private unsafe void* m_Buffer;
        private int m_Length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
        [NativeSetClassTypeToNullOnSchedule]
        private DisposeSentinel m_DisposeSentinel;
#endif
        private Allocator m_AllocatorLabel;

        private static readonly int ElementSize = UnsafeUtility.SizeOf<DynamicComponentTypeHandle>();
        private static readonly int Alignment = UnsafeUtility.AlignOf<DynamicComponentTypeHandle>();

        public unsafe ComponentTypeHandleArray(int length, Allocator allocator)
        {
            long size = ElementSize * (long) length;
        
            if (allocator <= Allocator.None)
                throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof (allocator));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof (length), "Length must be >= 0");
            if (size > (long) int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof (length), $"Length * sizeof(DynamicComponentTypeHandle) cannot exceed {int.MaxValue.ToString()} bytes");
        
            m_Buffer = UnsafeUtility.Malloc(size, Alignment, allocator);
            m_Length = length;
            m_AllocatorLabel = allocator;
            
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 1, allocator);
#endif
        }
    
        public unsafe void Dispose()
        {
            Debug.Assert(!JobsUtility.IsExecutingJob);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!UnsafeUtility.IsValidAllocator(m_AllocatorLabel))
                throw new InvalidOperationException("The ComponentTypeHandleArray can not be disposed because it was not allocated with a valid allocator.");
#endif
            UnsafeUtility.Free(m_Buffer, m_AllocatorLabel);
            m_Buffer = (void*) null;
            m_Length = 0;
        }
        
        public unsafe JobHandle Dispose(JobHandle inputDeps)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Clear(ref m_DisposeSentinel);
#endif
            JobHandle jobHandle = new TypeHandleArrayDisposeJob()
            {
                DisposeJobData = new TypeHandleArrayDisposeJobData()
                {
                    m_Buffer = m_Buffer,
                    m_AllocatorLabel = m_AllocatorLabel,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    m_Safety = m_Safety
#endif
                }
            }.Schedule(inputDeps);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            m_Buffer = (void*) null;
            m_Length = 0;
            return jobHandle;
        }
        
        public unsafe DynamicComponentTypeHandle this[int index]
        {
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Debug.Assert(!JobsUtility.IsExecutingJob);
                if (index < 0 || index >= m_Length)
                    throw new IndexOutOfRangeException();
#endif
                UnsafeUtility.WriteArrayElement(m_Buffer, index, value);
            }
        }

        public unsafe bool GetByTypeIndex(int typeIndex, out DynamicComponentTypeHandle typeHandle)
        {
            Debug.Assert(typeIndex != 0);
            for (int i = 0; i < m_Length; i++)
            {
                DynamicComponentTypeHandle element = UnsafeUtility.ReadArrayElement<DynamicComponentTypeHandle>(m_Buffer, i);
                if (element.GetTypeIndex() == typeIndex)
                {
                    typeHandle = element;
                    return true;
                }
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            throw new ArgumentException("There was no DynamicComponentTypeHandle found with the type index you provided.", nameof(typeIndex));
#else
            typeHandle = default;
            return false;
#endif
        }
    }

    
    [NativeContainer]
    public struct BufferTypeHandleArray : IDisposable
    {
        [NativeDisableUnsafePtrRestriction] private unsafe void* m_Buffer;
        private int m_Length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
        [NativeSetClassTypeToNullOnSchedule]
        private DisposeSentinel m_DisposeSentinel;
#endif
        private Allocator m_AllocatorLabel;

        private static readonly int ElementSize = UnsafeUtility.SizeOf<DynamicBufferTypeHandle>();
        private static readonly int Alignment = UnsafeUtility.AlignOf<DynamicBufferTypeHandle>();

        public unsafe BufferTypeHandleArray(int length, Allocator allocator)
        {
            long size = ElementSize * (long) length;
        
            if (allocator <= Allocator.None)
                throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof (allocator));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof (length), "Length must be >= 0");
            if (size > (long) int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof (length), $"Length * sizeof(DynamicBufferTypeHandle) cannot exceed {int.MaxValue.ToString()} bytes");
        
            m_Buffer = UnsafeUtility.Malloc(size, Alignment, allocator);
            m_Length = length;
            m_AllocatorLabel = allocator;
            
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 1, allocator);
#endif
        }
    
        public unsafe void Dispose()
        {
            Debug.Assert(!JobsUtility.IsExecutingJob);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!UnsafeUtility.IsValidAllocator(m_AllocatorLabel))
                throw new InvalidOperationException("The BufferTypeHandleArray can not be disposed because it was not allocated with a valid allocator.");
#endif
            UnsafeUtility.Free(m_Buffer, m_AllocatorLabel);
            m_Buffer = (void*) null;
            m_Length = 0;
        }
        
        public unsafe JobHandle Dispose(JobHandle inputDeps)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Clear(ref m_DisposeSentinel);
#endif
            JobHandle jobHandle = new TypeHandleArrayDisposeJob()
            {
                DisposeJobData = new TypeHandleArrayDisposeJobData()
                {
                    m_Buffer = m_Buffer,
                    m_AllocatorLabel = m_AllocatorLabel,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    m_Safety = m_Safety
#endif
                }
            }.Schedule(inputDeps);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
#endif
            m_Buffer = (void*) null;
            m_Length = 0;
            return jobHandle;
        }
        
        public unsafe DynamicBufferTypeHandle this[int index]
        {
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Debug.Assert(!JobsUtility.IsExecutingJob);
                if (index < 0 || index >= m_Length)
                    throw new IndexOutOfRangeException();
#endif
                UnsafeUtility.WriteArrayElement(m_Buffer, index, value);
            }
        }

        public unsafe bool GetByTypeIndex(int typeIndex, out DynamicBufferTypeHandle typeHandle)
        {
            Debug.Assert(typeIndex != 0);
            for (int i = 0; i < m_Length; i++)
            {
                DynamicBufferTypeHandle element = UnsafeUtility.ReadArrayElement<DynamicBufferTypeHandle>(m_Buffer, i);
                if (element.GetTypeIndex() == typeIndex)
                {
                    typeHandle = element;
                    return true;
                }
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            throw new ArgumentException("There was no DynamicBufferTypeHandle found with the type index you provided.", nameof(typeIndex));
#else
            typeHandle = default;
            return false;
#endif
        }
    }
}