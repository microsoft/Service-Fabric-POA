// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentService
{
    using System.Linq;
    using System.IO;

    /// <summary>
    /// Utility class which contains helper functions to do operations on a directory
    /// </summary>
    internal class DirectoryUtility
    {
        /// <summary>
        /// Utility to trim a directory as per the size provided, newer files would be retained, older files would be deleted.
        /// </summary>
        /// <param name="path">Path indicating the location of the directory which is to be trimmed</param>
        /// <param name="maxAllowedDirectorySizeInBytes">Maximum folder size in bytes which is allowed for directory</param>
        internal static void TrimDirectory(string path, long maxAllowedDirectorySizeInBytes)
        {
            if (!Directory.Exists(path))
                return;

            IOrderedEnumerable<string> orderedFileList = Directory.GetFiles(path).OrderByDescending(f => new FileInfo(f).LastWriteTime);
            long maxQuota = maxAllowedDirectorySizeInBytes;
            foreach (string fileName in orderedFileList)
            {
                if (maxQuota > 0)
                {
                    FileInfo fileInfo = new FileInfo(fileName);
                    maxQuota -= fileInfo.Length;
                }
                else
                {
                    File.Delete(fileName);
                }
            }
        }
    }
}
