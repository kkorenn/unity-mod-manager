using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using dnlib.DotNet;

namespace UnityModManagerNet.ConsoleInstaller
{
    public class Utils
    {
        private static bool? isMacPlatform;

        static Utils()
        {

        }

        public static Version ParseVersion(string str)
        {
            var array = str.Split('.');
            if (array.Length >= 4)
            {
                var regex = new Regex(@"\D");
                return new Version(int.Parse(regex.Replace(array[0], "")), int.Parse(regex.Replace(array[1], "")), int.Parse(regex.Replace(array[2], "")), int.Parse(regex.Replace(array[3], "")));
            }
            else if (array.Length >= 3)
            {
                var regex = new Regex(@"\D");
                return new Version(int.Parse(regex.Replace(array[0], "")), int.Parse(regex.Replace(array[1], "")), int.Parse(regex.Replace(array[2], "")));
            }
            else if (array.Length >= 2)
            {
                var regex = new Regex(@"\D");
                return new Version(int.Parse(regex.Replace(array[0], "")), int.Parse(regex.Replace(array[1], "")));
            }
            else if (array.Length >= 1)
            {
                var regex = new Regex(@"\D");
                return new Version(int.Parse(regex.Replace(array[0], "")), 0);
            }

            Log.Print($"Error parsing version '{str}'.");
            return new Version();
        }

        public static bool IsDirectoryWritable(string dirpath)
        {
            try
            {
                if (Directory.Exists(dirpath))
                    using (FileStream fs = File.Create(Path.Combine(dirpath, Path.GetRandomFileName()), 1, FileOptions.DeleteOnClose))
                    {; }
                return true;
            }
            catch
            {
                Log.Print($"Directory '{dirpath}' does not have write permission.");
                return false;
            }
        }

        public static bool IsFileWritable(string filepath)
        {
            try
            {
                if (File.Exists(filepath))
                    using (FileStream fs = File.OpenWrite(filepath))
                    {; }
                return true;
            }
            catch
            {
                Log.Print($"File '{filepath}' does not have write permission.");
                return false;
            }
        }

        public static bool RemoveReadOnly(string filepath)
        {
            try
            {
                if (File.Exists(filepath))
                {
                    var fi = new FileInfo(filepath);
                    fi.Attributes &= ~System.IO.FileAttributes.ReadOnly;
                }
                return true;
            }
            catch (Exception e)
            {
                Log.Print(e.ToString());
            }
            return false;
        }

        public static bool TryParseEntryPoint(string str, out string assembly)
        {
            assembly = string.Empty;
            return TryParseEntryPoint(str, out assembly, out _, out _, out _);
        }

        public static bool TryParseEntryPoint(string str, out string assembly, out string @class, out string method, out string insertionPlace)
        {
            assembly = string.Empty;
            @class = string.Empty;
            method = string.Empty;
            insertionPlace = string.Empty;

            var regex = new Regex(@"(?:(?<=\[)(?'assembly'.+(?>\.dll))(?=\]))|(?:(?'class'[\w|\.]+)(?=\.))|(?:(?<=\.)(?'func'\w+))|(?:(?<=\:)(?'mod'\w+))", RegexOptions.IgnoreCase);
            var matches = regex.Matches(str);
            var groupNames = regex.GetGroupNames();

            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    foreach (var group in groupNames)
                    {
                        if (match.Groups[group].Success)
                        {
                            switch (group)
                            {
                                case "assembly":
                                    assembly = match.Groups[group].Value;
                                    break;
                                case "class":
                                    @class = match.Groups[group].Value;
                                    break;
                                case "func":
                                    method = match.Groups[group].Value;
                                    if (method == "ctor")
                                        method = ".ctor";
                                    else if (method == "cctor")
                                        method = ".cctor";
                                    break;
                                case "mod":
                                    insertionPlace = match.Groups[group].Value.ToLower();
                                    break;
                            }
                        }
                    }
                }
                //Log.Print(assembly);
                //Log.Print(@class);
                //Log.Print(method);
                //Log.Print(insertionPlace);
            }

            var hasError = false;

            if (string.IsNullOrEmpty(assembly))
            {
                hasError = true;
                Log.Print("Assembly name not found.");
            }

            if (string.IsNullOrEmpty(@class))
            {
                hasError = true;
                Log.Print("Class name not found.");
            }

            if (string.IsNullOrEmpty(method))
            {
                hasError = true;
                Log.Print("Method name not found.");
            }

            if (hasError)
            {
                Log.Print($"Error parsing EntryPoint '{str}'.");
                return false;
            }

            return true;
        }

        public static bool TryGetEntryPoint(ModuleDefMD assemblyDef, string str, out MethodDef foundMethod, out string insertionPlace, bool createConstructor = false)
        {
            foundMethod = null;

            if (!TryParseEntryPoint(str, out var assembly, out var className, out var methodName, out insertionPlace))
            {
                return false;
            }

            var targetClass = assemblyDef.Types.FirstOrDefault(x => x.FullName == className);
            if (targetClass == null)
            {
                Log.Print($"Class '{className}' not found.");
                return false;
            }

            foundMethod = targetClass.Methods.FirstOrDefault(x => x.Name == methodName);
            if (foundMethod == null)
            {
                if (createConstructor && methodName == ".cctor")
                {
                    //var m = new MethodDefUser(".cctor", assemblyDef.CorLibTypes.Void, MethodAttributes.Private | MethodAttributes.RTSpecialName | MethodAttributes.SpecialName | MethodAttributes.Static);
                    var typeDef = ModuleDefMD.Load(typeof(Utils).Module).Types.FirstOrDefault(x => x.FullName == typeof(Utils).FullName);
                    var method = typeDef.Methods.FirstOrDefault(x => x.Name == ".cctor");
                    if (method != null)
                    {
                        typeDef.Methods.Remove(method);
                        targetClass.Methods.Add(method);
                        foundMethod = method;
                        
                        return true;
                    }
                }
                Log.Print($"Method '{methodName}' not found.");
                return false;
            }

            return true;
        }

        public static string ResolveOSXFileUrl(string url)
        {
            Process p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = "osascript";
            p.StartInfo.Arguments = $"-e \"get posix path of posix file \\\"{url}\\\"\"";
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output.TrimEnd();
        }

        public static bool IsPlatform(OSPlatform platform)
        {
            if (platform == OSPlatform.Windows)
            {
                return !IsUnixPlatform();
            }

            if (platform == OSPlatform.OSX)
            {
                return IsMacPlatform();
            }

            if (platform == OSPlatform.Linux)
            {
                return IsLinuxPlatform();
            }

            return false;
        }

        public static bool IsWindowsPlatform()
        {
            return !IsUnixPlatform();
        }

        public static bool IsUnixPlatform()
        {
            int p = (int)Environment.OSVersion.Platform;
            return (p == 4) || (p == 6) || (p == 128);
        }

        public static bool IsMacPlatform()
        {
            if (isMacPlatform.HasValue)
            {
                return isMacPlatform.Value;
            }

            int p = (int)Environment.OSVersion.Platform;
            var result = (p == 6);

            if (!result && (p == 4 || p == 128))
            {
                result = IsDarwinKernel();
            }

            isMacPlatform = result;
            return result;
        }

        public static bool IsLinuxPlatform()
        {
            int p = (int)Environment.OSVersion.Platform;
            return ((p == 4) || (p == 128)) && !IsMacPlatform();
        }

        private static bool IsDarwinKernel()
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.FileName = "uname";
                    process.StartInfo.Arguments = "-s";
                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    return output.Trim().Equals("Darwin", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool MakeBackup(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Copy(path, $"{path}.backup_", true);
                }
            }
            catch (Exception e)
            {
                Log.Print(e.Message);
                return false;
            }

            return true;
        }

        public static bool MakeBackup(List<string> arr)
        {
            try
            {
                foreach (var path in arr)
                {
                    if (File.Exists(path))
                    {
                        File.Copy(path, $"{path}.backup_", true);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Print(e.Message);
                return false;
            }

            return true;
        }

        public static bool RestoreBackup(string path)
        {
            try
            {
                var backup = $"{path}.backup_";
                if (File.Exists(backup))
                {
                    File.Copy(backup, path, true);
                }
            }
            catch (Exception e)
            {
                Log.Print(e.Message);
                return false;
            }

            return true;
        }

        public static bool RestoreBackup(List<string> arr)
        {
            try
            {
                foreach (var path in arr)
                {
                    var backup = $"{path}.backup_";
                    if (File.Exists(backup))
                    {
                        File.Copy(backup, path, true);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Print(e.Message);
                return false;
            }

            return true;
        }

        public static bool DeleteBackup(string path)
        {
            try
            {
                var backup = $"{path}.backup_";
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }
            }
            catch (Exception e)
            {
                Log.Print(e.Message);
                return false;
            }

            return true;
        }

        public static bool DeleteBackup(List<string> arr)
        {
            try
            {
                foreach (var path in arr)
                {
                    var backup = $"{path}.backup_";
                    if (File.Exists(backup))
                    {
                        File.Delete(backup);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Print(e.Message);
                return false;
            }

            return true;
        }

        public static string NormalizeGamePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var normalized = path.Trim().Trim('"', '\'');
            var root = Path.GetPathRoot(normalized);
            if (normalized.Length > 1 && !string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return normalized;
        }

        public static bool IsMacAppBundle(string path)
        {
            var normalized = NormalizeGamePath(path);
            return !string.IsNullOrEmpty(normalized)
                && normalized.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(normalized);
        }

        public static string ResolveMacGamePath(string path, params string[] appNameCandidates)
        {
            var normalizedPath = NormalizeGamePath(path);
            if (!IsMacPlatform() || string.IsNullOrEmpty(normalizedPath) || !Directory.Exists(normalizedPath))
            {
                return normalizedPath;
            }

            if (IsMacAppBundle(normalizedPath))
            {
                return normalizedPath;
            }

            var appBundles = Directory.GetDirectories(normalizedPath, "*.app", SearchOption.TopDirectoryOnly);
            if (appBundles.Length == 0)
            {
                return normalizedPath;
            }

            var appNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in appNameCandidates ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(NormalizeGamePath(item));
                if (!string.IsNullOrEmpty(name))
                {
                    appNames.Add(name);
                }
            }

            foreach (var appBundle in appBundles)
            {
                if (appNames.Contains(Path.GetFileNameWithoutExtension(appBundle)))
                {
                    return appBundle;
                }
            }

            return appBundles.Length == 1 ? appBundles[0] : normalizedPath;
        }

        public static string FindGameFolder(params string[] names)
        {
            if (names == null || names.Length == 0)
            {
                return null;
            }

            var candidates = names
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => Path.GetFileNameWithoutExtension(NormalizeGamePath(x)))
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (candidates.Length == 0)
            {
                return null;
            }

            string[] disks = new string[] { @"C:\", @"D:\", @"E:\", @"F:\" };
            string[] roots = new string[] { "Games", "Program files", "Program files (x86)", "" };
            string[] folders = new string[] { @"Steam\SteamApps\common", @"GoG Galaxy\Games", "" };
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                disks = new string[] { Environment.GetEnvironmentVariable("HOME") };
                roots = new string[] { "Library/Application Support", ".steam" };
                folders = new string[] { "Steam/SteamApps/common", "steam/steamapps/common", "Steam/steamapps/common" };
            }
            foreach (var disk in disks)
            {
                foreach (var root in roots)
                {
                    foreach (var folder in folders)
                    {
                        foreach (var candidate in candidates)
                        {
                            var path = Path.Combine(disk, root, folder, candidate);
                            if (Directory.Exists(path))
                            {
                                return ResolveMacGamePath(path, candidates);
                            }

                            if (Utils.IsMacPlatform())
                            {
                                var appPath = Path.Combine(disk, root, folder, $"{candidate}.app");
                                if (Directory.Exists(appPath))
                                {
                                    return appPath;
                                }
                            }
                        }
                    }
                }
            }
            return null;
        }

        public static string FindManagedFolder(string path)
        {
            path = NormalizeGamePath(path);
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return null;
            }

            if (Utils.IsMacPlatform())
            {
                var appPath = ResolveMacGamePath(path);
                if (IsMacAppBundle(appPath))
                {
                    var dirs = new[]
                    {
                        Path.Combine(appPath, "Contents", "Resources", "Data", "Managed"),
                        Path.Combine(appPath, "Contents", "Data", "Managed")
                    };

                    foreach (var dir in dirs)
                    {
                        if (Directory.Exists(dir))
                        {
                            return dir;
                        }
                    }
                }
            }

            foreach (var di in new DirectoryInfo(path).GetDirectories())
            {
                if ((di.Attributes & System.IO.FileAttributes.ReparsePoint) != 0)
                    continue;

                var dir = di.FullName;
                if (dir.EndsWith("Managed"))
                {
                    if (File.Exists(Path.Combine(dir, "Assembly-CSharp.dll")) || File.Exists(Path.Combine(dir, "UnityEngine.dll")))
                    {
                        return dir;
                    }
                }
                var result = FindManagedFolder(dir);
                if (!string.IsNullOrEmpty(result))
                    return result;
            }

            return null;
        }

        public static bool IsDirty(ModuleDefMD assembly)
        {
            return assembly.Types.FirstOrDefault(x => x.FullName == typeof(Marks.IsDirty).FullName || x.Name == typeof(UnityModManager).Name) != null;
        }

        public static void MakeDirty(ModuleDefMD assembly)
        {
            var moduleDef = ModuleDefMD.Load(typeof(Marks.IsDirty).Module);
            var typeDef = moduleDef.Types.FirstOrDefault(x => x.FullName == typeof(Marks.IsDirty).FullName);
            moduleDef.Types.Remove(typeDef);
            assembly.Types.Add(typeDef);
        }

        public enum MachineType : ushort
        {
            IMAGE_FILE_MACHINE_UNKNOWN = 0x0,
            IMAGE_FILE_MACHINE_AM33 = 0x1d3,
            IMAGE_FILE_MACHINE_AMD64 = 0x8664,
            IMAGE_FILE_MACHINE_ARM = 0x1c0,
            IMAGE_FILE_MACHINE_EBC = 0xebc,
            IMAGE_FILE_MACHINE_I386 = 0x14c,
            IMAGE_FILE_MACHINE_IA64 = 0x200,
            IMAGE_FILE_MACHINE_M32R = 0x9041,
            IMAGE_FILE_MACHINE_MIPS16 = 0x266,
            IMAGE_FILE_MACHINE_MIPSFPU = 0x366,
            IMAGE_FILE_MACHINE_MIPSFPU16 = 0x466,
            IMAGE_FILE_MACHINE_POWERPC = 0x1f0,
            IMAGE_FILE_MACHINE_POWERPCFP = 0x1f1,
            IMAGE_FILE_MACHINE_R4000 = 0x166,
            IMAGE_FILE_MACHINE_SH3 = 0x1a2,
            IMAGE_FILE_MACHINE_SH3DSP = 0x1a3,
            IMAGE_FILE_MACHINE_SH4 = 0x1a6,
            IMAGE_FILE_MACHINE_SH5 = 0x1a8,
            IMAGE_FILE_MACHINE_THUMB = 0x1c2,
            IMAGE_FILE_MACHINE_WCEMIPSV2 = 0x169,
        }

        public static MachineType GetDllMachineType(string dllPath)
        {
            // See http://www.microsoft.com/whdc/system/platform/firmware/PECOFF.mspx
            // Offset to PE header is always at 0x3C.
            // The PE header starts with "PE\0\0" =  0x50 0x45 0x00 0x00,
            // followed by a 2-byte machine type field (see the document above for the enum).
            //
            FileStream fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read);
            BinaryReader br = new BinaryReader(fs);
            fs.Seek(0x3c, SeekOrigin.Begin);
            Int32 peOffset = br.ReadInt32();
            fs.Seek(peOffset, SeekOrigin.Begin);
            UInt32 peHead = br.ReadUInt32();

            if (peHead != 0x00004550) // "PE\0\0", little-endian
                throw new Exception("Can't find PE header");

            MachineType machineType = (MachineType)br.ReadUInt16();
            br.Close();
            fs.Close();
            return machineType;
        }

        public static bool? UnmanagedDllIs64Bit(string dllPath)
        {
            try
            {
                switch (GetDllMachineType(dllPath))
                {
                    case MachineType.IMAGE_FILE_MACHINE_AMD64:
                    case MachineType.IMAGE_FILE_MACHINE_IA64:
                        return true;
                    case MachineType.IMAGE_FILE_MACHINE_I386:
                        return false;
                    default:
                        return null;
                }
            }
            catch (Exception e)
            {
                Log.Print(e.ToString());
                Log.Print($"Unable to determine the bitness of {dllPath}");
                return null;
            }
        }

        //[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        //public static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, SymbolicLink dwFlags);

        //public enum SymbolicLink
        //{
        //    File = 0,
        //    Directory = 1
        //}

        //[DllImport("kernel32.dll")]
        //public static extern uint GetLastError();
    }
}
