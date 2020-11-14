using System;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace DotsPersistency.Containers
{
    [Serializable]
    public struct FixedArray2<T>
        where T : struct
    {
        [SerializeField]
        private T _value0;
        [SerializeField]
        private T _value1;

        public int Length => 2;

        public FixedArray2(T initialValue)
        {
            _value0 = initialValue; 
            _value1 = initialValue;
        }
        
        public ref T this[int index]
        {
            get
            {
                unsafe
                {
                    Debug.Assert(index < Length);
                    return ref UnsafeUtility.ArrayElementAsRef<T>(UnsafeUtility.AddressOf(ref _value0),
                        index);
                }
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(64);
            sb.Append("FixedArray");
            sb.Append(Length);
            sb.Append("(");
            for (int i = 0; i < Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(this[i].ToString());
            }
            sb.Append(")");
            return sb.ToString();
        }
    }
}

