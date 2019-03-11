// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.Common
{
    using System.IO;
    using Newtonsoft.Json;

    /// <summary>
    /// Serialization and desrialization utility for Json objects
    /// </summary>
    public static class SerializationUtility
    {
        public static string Serialize<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj);
        }

        public static T Deserialize<T>(string serializedExecutorData)
        {
            return JsonConvert.DeserializeObject<T>(serializedExecutorData);
        }

        public static void Serialize<T>(string fileName, T obj)
        {
            File.WriteAllText(fileName, Serialize(obj));
        }

        public static T DeserializeFromFile<T>(string fileName)
        {
            return Deserialize<T>(File.ReadAllText(fileName));
        }
    }
}