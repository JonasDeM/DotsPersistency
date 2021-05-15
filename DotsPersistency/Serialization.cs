// Author: Jonas De Maeseneer

using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Entities.Serialization;
using Unity.IO.LowLevel.Unsafe;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace DotsPersistency
{
    public static class Serialization
    {
        public static string CalculateStreamingAssetsPath(Hash128 containerIdentifier)
        {
            StringBuilder sb = new StringBuilder(128);
            sb.Append(Application.streamingAssetsPath);
            sb.Append("/SavedSceneStates/");
            sb.Append(containerIdentifier.ToString());
            sb.Append(".sav");
            return sb.ToString();
        }

        // index -1 means 'use current index'
        public static void WriteContainerToDisk(this PersistentSceneSystem persistentSceneSystem, Hash128 containerIdentifier, bool initial = false, int index = -1)
        {
            Debug.Assert(initial == (index!=-1), "Don't fill in an index if you want the initial container!");
            
            // Path
            string path = CalculateStreamingAssetsPath(containerIdentifier);
            string folderPath = Path.GetDirectoryName(path);
            Debug.Assert(!string.IsNullOrEmpty(folderPath));
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            // Container
            PersistentDataStorage dataStorage = persistentSceneSystem.PersistentDataStorage;
            PersistentDataContainer container;
            if (index == -1)
            {
                container = dataStorage.GetReadContainerForCurrentIndex(containerIdentifier);
            }
            else if (initial)
            {
                container = dataStorage.GetInitialStateReadContainer(containerIdentifier);
            }
            else
            {
                int oldIndex = dataStorage.NonWrappedIndex;
                dataStorage.ToIndex(index);
                container = dataStorage.GetReadContainerForCurrentIndex(containerIdentifier);
                dataStorage.ToIndex(oldIndex);
            }
            
            // Workaround for bug where AsyncReadManager.Read keeps a handle to the last 11 reads
            for (int i = 0; i < 12; i++)
            {
                string bugWorkaroundFilePath = Application.streamingAssetsPath + $"/UnityAsyncReadBugWorkaround{i.ToString()}.txt";
                unsafe
                {
                    var readCmd = new ReadCommand
                    {
                        Size = 0, Offset = 0, Buffer = null
                    };
                    var readHandle = AsyncReadManager.Read(bugWorkaroundFilePath, &readCmd, 1);
                    readHandle.JobHandle.Complete();
                    readHandle.Dispose();
                }
            }
            
            // Write To Disk
            using (var fileStream = new StreamBinaryWriter(path))
            {
                fileStream.Write(container.CalculateEntityCapacity());
                NativeArray<byte> rawData = container.GetRawData();
                fileStream.Write(rawData.Length);
                fileStream.WriteArray(rawData);
            }
        }

        public static bool DoesContainerExistOnDisk(this PersistentSceneSystem persistentSceneSystem, Hash128 containerIdentifier)
        {
            return File.Exists(CalculateStreamingAssetsPath(containerIdentifier));
        }

        // index -1 means 'use current index'
        public static int ReadContainerFromDisk(this PersistentSceneSystem persistentSceneSystem, Hash128 containerIdentifier, int index = -1)
        {
            // Path
            string path = CalculateStreamingAssetsPath(containerIdentifier);
            Debug.Assert(File.Exists(path));
            
            // Read From Disk
            using (var fileStream = new StreamBinaryReader(path))
            {
                PersistentDataStorage dataStorage = persistentSceneSystem.PersistentDataStorage;
                int amountEntitiesInFile = fileStream.ReadInt();
                if (!dataStorage.IsInitialized(containerIdentifier))
                {
                    // Read before the scene has been loaded once this session
                    int amountBytesOfRawData = fileStream.ReadInt();
                    NativeArray<byte> rawData = new NativeArray<byte>(amountBytesOfRawData, Allocator.Persistent);
                    fileStream.ReadArray(rawData, rawData.Length);
                    dataStorage.AddUninitializedSceneContainer(containerIdentifier, rawData);
                }
                else
                {
                    int currentEntityCapacity = dataStorage.GetEntityCapacity(containerIdentifier, out bool isPool);

                    if (amountEntitiesInFile != currentEntityCapacity)
                    {
                        if (isPool)
                        {
                            persistentSceneSystem.InstantChangePoolCapacity(containerIdentifier, amountEntitiesInFile);
                        }
                        else
                        {
                            throw new InvalidDataException($"The data in \"{path}\" is invalid. (.sav data only stays valid as long as the subscene doesn't change)");
                        }
                    }
                
                    // Container (Important that it happens after potential capacity change!)
                    PersistentDataContainer container;
                    if (index == -1)
                    {
                        container = dataStorage.GetWriteContainerForCurrentIndex(containerIdentifier);
                    }
                    else
                    {
                        int oldIndex = dataStorage.NonWrappedIndex;
                        dataStorage.ToIndex(index);
                        container = dataStorage.GetWriteContainerForCurrentIndex(containerIdentifier);
                        dataStorage.ToIndex(oldIndex);
                    }

                    // Read Data From Disk
                    NativeArray<byte> rawData = container.GetRawData();
                
                    int amountBytesOfRawData = fileStream.ReadInt();
                    if (amountBytesOfRawData != rawData.Length)
                    {
                        // Extra integrity Check
                        throw new InvalidDataException($"The data in \"{path}\" is invalid. (maybe component data structs changed?)");
                    }
                
                    fileStream.ReadArray(rawData, rawData.Length);
                }
                return amountEntitiesInFile;
            }
        }
    }
}
