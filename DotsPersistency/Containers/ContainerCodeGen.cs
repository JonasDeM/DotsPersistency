using System.IO;
using UnityEditor;

namespace DotsPersistency.Containers
{
    public static class ContainerCodeGen 
    {
        //[MenuItem("Code Gen/Containers/Generate Fixed Arrays")]
        public static void Generate()
        {
            const int amount = 8;
            
            using (StreamWriter streamWriter = new StreamWriter("Assets/DotsPersistency/Containers/FixedArray.gen.cs"))
            {
                streamWriter.WriteLine(@"// Generated File (Author: Jonas De Maeseneer)

using System;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace DotsPersistency.Containers");
                streamWriter.WriteLine("{");
                for (int i = 0; i < amount; i++)
                {
                    WriteFixedArray(streamWriter, 1 << i+2);
                }
            
                streamWriter.WriteLine("}");
            } 
        }

        private static void WriteFixedArray(StreamWriter streamWriter, int size)
        {
            streamWriter.WriteLine(string.Format(
@"
    [Serializable]
    public struct FixedArray{0}<T> where T : struct
    {{
        private FixedArray{1}<T> _value0;
        // ""value is never used\"" It can be retrieved via the unsafe indexer.
#pragma warning disable 0414
        private FixedArray{1}<T> _value1;
#pragma warning restore 0414

        public int Length => _value0.Length * 2;

        public FixedArray{0}(T initialValue)
        {{
            _value0 = new FixedArray{1}<T>(initialValue);
            _value1 = new FixedArray{1}<T>(initialValue);
        }}
    
        public ref T this[int index]
        {{
            get
            {{
                unsafe
                {{
                    Debug.Assert(index < Length);
                    return ref UnsafeUtilityEx.ArrayElementAsRef<T>(UnsafeUtility.AddressOf(ref _value0),
                        index);
                }}
            }}
        }}
    
        public override string ToString()
        {{
            StringBuilder sb = new StringBuilder(64);
            sb.Append(""FixedArray"");
            sb.Append(Length);
            sb.Append(""("");
            for (int i = 0; i < Length; i++)
            {{
                if (i > 0)
                {{
                    sb.Append("", "");
                }}
                sb.Append(this[i].ToString());
            }}
            sb.Append("")"");
            return sb.ToString();
        }}
    }}", arg0:size, arg1:size/2));
        }
    }
}
