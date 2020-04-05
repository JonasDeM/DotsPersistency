// Generated File (Author: Jonas De Maeseneer)

using System;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace DotsPersistency.Containers
{

    [Serializable]
    public struct FixedArray4<T> where T : struct
    {
        private FixedArray2<T> _value0;
        // "value is never used\" It can be retrieved via the unsafe indexer.
#pragma warning disable 0414
        private FixedArray2<T> _value1;
#pragma warning restore 0414

        public int Length => _value0.Length * 2;

        public FixedArray4(T initialValue)
        {
            _value0 = new FixedArray2<T>(initialValue);
            _value1 = new FixedArray2<T>(initialValue);
        }
    
        public ref T this[int index]
        {
            get
            {
                unsafe
                {
                    Debug.Assert(index < Length);
                    return ref UnsafeUtilityEx.ArrayElementAsRef<T>(UnsafeUtility.AddressOf(ref _value0),
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

    [Serializable]
    public struct FixedArray8<T> where T : struct
    {
        private FixedArray4<T> _value0;
        // "value is never used\" It can be retrieved via the unsafe indexer.
#pragma warning disable 0414
        private FixedArray4<T> _value1;
#pragma warning restore 0414

        public int Length => _value0.Length * 2;

        public FixedArray8(T initialValue)
        {
            _value0 = new FixedArray4<T>(initialValue);
            _value1 = new FixedArray4<T>(initialValue);
        }
    
        public ref T this[int index]
        {
            get
            {
                unsafe
                {
                    Debug.Assert(index < Length);
                    return ref UnsafeUtilityEx.ArrayElementAsRef<T>(UnsafeUtility.AddressOf(ref _value0),
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

    [Serializable]
    public struct FixedArray16<T> where T : struct
    {
        private FixedArray8<T> _value0;
        // "value is never used\" It can be retrieved via the unsafe indexer.
#pragma warning disable 0414
        private FixedArray8<T> _value1;
#pragma warning restore 0414

        public int Length => _value0.Length * 2;

        public FixedArray16(T initialValue)
        {
            _value0 = new FixedArray8<T>(initialValue);
            _value1 = new FixedArray8<T>(initialValue);
        }
    
        public ref T this[int index]
        {
            get
            {
                unsafe
                {
                    Debug.Assert(index < Length);
                    return ref UnsafeUtilityEx.ArrayElementAsRef<T>(UnsafeUtility.AddressOf(ref _value0),
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

    [Serializable]
    public struct FixedArray32<T> where T : struct
    {
        private FixedArray16<T> _value0;
        // "value is never used\" It can be retrieved via the unsafe indexer.
#pragma warning disable 0414
        private FixedArray16<T> _value1;
#pragma warning restore 0414

        public int Length => _value0.Length * 2;

        public FixedArray32(T initialValue)
        {
            _value0 = new FixedArray16<T>(initialValue);
            _value1 = new FixedArray16<T>(initialValue);
        }
    
        public ref T this[int index]
        {
            get
            {
                unsafe
                {
                    Debug.Assert(index < Length);
                    return ref UnsafeUtilityEx.ArrayElementAsRef<T>(UnsafeUtility.AddressOf(ref _value0),
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

    [Serializable]
    public struct FixedArray64<T> where T : struct
    {
        private FixedArray32<T> _value0;
        // "value is never used\" It can be retrieved via the unsafe indexer.
#pragma warning disable 0414
        private FixedArray32<T> _value1;
#pragma warning restore 0414

        public int Length => _value0.Length * 2;

        public FixedArray64(T initialValue)
        {
            _value0 = new FixedArray32<T>(initialValue);
            _value1 = new FixedArray32<T>(initialValue);
        }
    
        public ref T this[int index]
        {
            get
            {
                unsafe
                {
                    Debug.Assert(index < Length);
                    return ref UnsafeUtilityEx.ArrayElementAsRef<T>(UnsafeUtility.AddressOf(ref _value0),
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

    [Serializable]
    public struct FixedArray128<T> where T : struct
    {
        private FixedArray64<T> _value0;
        // "value is never used\" It can be retrieved via the unsafe indexer.
#pragma warning disable 0414
        private FixedArray64<T> _value1;
#pragma warning restore 0414

        public int Length => _value0.Length * 2;

        public FixedArray128(T initialValue)
        {
            _value0 = new FixedArray64<T>(initialValue);
            _value1 = new FixedArray64<T>(initialValue);
        }
    
        public ref T this[int index]
        {
            get
            {
                unsafe
                {
                    Debug.Assert(index < Length);
                    return ref UnsafeUtilityEx.ArrayElementAsRef<T>(UnsafeUtility.AddressOf(ref _value0),
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

    [Serializable]
    public struct FixedArray256<T> where T : struct
    {
        private FixedArray128<T> _value0;
        // "value is never used\" It can be retrieved via the unsafe indexer.
#pragma warning disable 0414
        private FixedArray128<T> _value1;
#pragma warning restore 0414

        public int Length => _value0.Length * 2;

        public FixedArray256(T initialValue)
        {
            _value0 = new FixedArray128<T>(initialValue);
            _value1 = new FixedArray128<T>(initialValue);
        }
    
        public ref T this[int index]
        {
            get
            {
                unsafe
                {
                    Debug.Assert(index < Length);
                    return ref UnsafeUtilityEx.ArrayElementAsRef<T>(UnsafeUtility.AddressOf(ref _value0),
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

    [Serializable]
    public struct FixedArray512<T> where T : struct
    {
        private FixedArray256<T> _value0;
        // "value is never used\" It can be retrieved via the unsafe indexer.
#pragma warning disable 0414
        private FixedArray256<T> _value1;
#pragma warning restore 0414

        public int Length => _value0.Length * 2;

        public FixedArray512(T initialValue)
        {
            _value0 = new FixedArray256<T>(initialValue);
            _value1 = new FixedArray256<T>(initialValue);
        }
    
        public ref T this[int index]
        {
            get
            {
                unsafe
                {
                    Debug.Assert(index < Length);
                    return ref UnsafeUtilityEx.ArrayElementAsRef<T>(UnsafeUtility.AddressOf(ref _value0),
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
