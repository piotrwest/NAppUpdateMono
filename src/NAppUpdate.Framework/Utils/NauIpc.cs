using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using NAppUpdate.Framework.Common;
using NAppUpdate.Framework.Tasks;

namespace NAppUpdate.Framework.Utils
{
    /// <summary>
    ///     Starts the cold update process by extracting the updater app from the library's resources,
    ///     passing it all the data it needs and terminating the current application
    /// </summary>
    internal static class NauIpc
    {
        [Serializable]
        internal class NauDto
        {
            public NauConfigurations Configs { get; set; }
            public IList<IUpdateTask> Tasks { get; set; }
            public List<Logger.LogItem> LogItems { get; set; }
            public string AppPath { get; set; }
            public string WorkingDirectory { get; set; }
            public bool RelaunchApplication { get; set; }
        }

        private static string GetAdditionalParamsFileName(string syncProcessName)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), syncProcessName);
        }

        internal static void WriteDtoToFile(NauDto dto, string syncProcessName)
        {
            using (var fileStream = new FileStream(GetAdditionalParamsFileName(syncProcessName), FileMode.Create))
            {
                new BinaryFormatter().Serialize(fileStream, dto);
                fileStream.Flush();
            }
        }

        public static Process LaunchProcessAndSendDto(NauDto dto, ProcessStartInfo processStartInfo,
            string syncProcessName)
        {
            Process p;
            var paramsFileName = GetAdditionalParamsFileName(syncProcessName);

            try
            {
                WriteDtoToFile(dto, syncProcessName);
            }
            catch
            {
                if (File.Exists(paramsFileName))
                    File.Delete(paramsFileName);
                return null;
            }

            if (!File.Exists(paramsFileName))
            {
                Console.WriteLine("Writing params to temporary path failed");
                return null;
            }

            try
            {
                p = ExtendendStartProcess.Start(processStartInfo);
            }
            catch (Win32Exception)
            {
                // Person denied UAC escallation
                return null;
            }

            for (int i = 0; i < 15; i++)
            {
                if (!File.Exists(paramsFileName))
                    return p;
                Thread.Sleep(1000);
            }

            File.Delete(paramsFileName);
            return null;
        }


        internal static object ReadDto(string syncProcessName)
        {
            var paramsFileName = GetAdditionalParamsFileName(syncProcessName);
            if (!File.Exists(paramsFileName))
                return null;

            object result;
            using (var fileStream = new FileStream(paramsFileName, FileMode.Open))
            {
                result = new BinaryFormatter().Deserialize(fileStream);
            }

            File.Delete(paramsFileName);

            return result;
        }

        internal static void ExtractUpdaterFromResource(string updaterPath, string hostExeName)
        {
            if (!Directory.Exists(updaterPath))
                Directory.CreateDirectory(updaterPath);

            //store the updater temporarily in the designated folder            
            using (var writer = new BinaryWriter(File.Open(Path.Combine(updaterPath, hostExeName), FileMode.Create)))
                writer.Write(Resources.updater);

            // Now copy the NAU DLL
            var assemblyLocation = typeof (NauIpc).Assembly.Location;
            File.Copy(assemblyLocation, Path.Combine(updaterPath, "NAppUpdate.Framework.dll"), true);

            // And also all other referenced DLLs (opt-in only)
            var assemblyPath = Path.GetDirectoryName(assemblyLocation) ?? string.Empty;
            if (UpdateManager.Instance.Config.DependenciesForColdUpdate == null) return;
            // TODO Maybe we can back this up with typeof(UpdateStarter).Assembly.GetReferencedAssemblies()

            foreach (var dep in UpdateManager.Instance.Config.DependenciesForColdUpdate)
            {
                var fullPath = Path.Combine(assemblyPath, dep);
                if (!File.Exists(fullPath)) continue;

                var dest = Path.Combine(updaterPath, dep);
                FileSystem.CreateDirectoryStructure(dest);
                File.Copy(fullPath, Path.Combine(updaterPath, dep), true);
            }
        }
    }
}