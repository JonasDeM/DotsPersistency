﻿using System;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace DotsPersistency
{
    public interface IPersistentDataSerializer
    {
        void WriteContainerData(PersistentDataContainer container);
        void ReadContainerData(PersistentDataContainer container);
    }
    
    public class DefaultPersistentDataSerializer : IPersistentDataSerializer
    {
        public string GetResourcePath(Hash128 containerIdentifier)
        {
            StringBuilder sb = new StringBuilder(128);
            sb.Append(Application.streamingAssetsPath);
            sb.Append("/SavedSceneStates/");
            sb.Append(containerIdentifier.ToString());
            sb.Append(".sav");
            return sb.ToString();
        }

        public void WriteContainerData(PersistentDataContainer container)
        {
            string path = GetResourcePath(container.DataIdentifier);
            string folderPath = Path.GetDirectoryName(path);
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            
            using (var fileStream = new FileStream(path, FileMode.Create))
            {
                var dataArray = container.GetRawData().ToArray();
                fileStream.Write(dataArray, 0, dataArray.Length);
            }
        }

        public void ReadContainerData(PersistentDataContainer container)
        {
            string path = GetResourcePath(container.DataIdentifier);
            Debug.Assert(File.Exists(path));
            
            using (var fileStream = new FileStream(path, FileMode.Open))
            {
                int length = (int)fileStream.Length;
                // Read Data
                byte[] dataByteArray = new byte[length];
                fileStream.Read(dataByteArray, 0, length);
                container.GetRawData().CopyFrom(dataByteArray);
            }
        }
    }
}
