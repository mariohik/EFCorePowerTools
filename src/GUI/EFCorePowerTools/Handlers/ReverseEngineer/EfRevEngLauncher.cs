﻿using EFCorePowerTools.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ReverseEngineer20.ReverseEngineer
{
    public class EfRevEngLauncher
    {
        private readonly ReverseEngineerCommandOptions options;
        private readonly bool useEFCore5;

        public EfRevEngLauncher(ReverseEngineerCommandOptions options, bool useEFCore5)
        {
            this.options = options;
            this.useEFCore5 = useEFCore5;
        }

        public List<TableInformationModel> GetDacpacTables(string dacpacPath)
        {
            var arguments = "\"" + dacpacPath + "\"";
            return GetTablesInternal(arguments);
        }

        public List<TableInformationModel> GetTables(string connectionString, DatabaseType databaseType)
        {
            var arguments = ((int)databaseType).ToString() + " \"" + connectionString + "\"";
            return GetTablesInternal(arguments);
        }

        private List<TableInformationModel> GetTablesInternal(string arguments)
        {
            if (!IsDotnetInstalled())
            {
                throw new Exception($"Reverse engineer error: Unable to launch 'dotnet' version 3 or newer. Do you have it installed?");
            }

            var launchPath = DropNetCoreFiles();

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(launchPath ?? throw new InvalidOperationException(), GetExeName()),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var standardOutput = RunProcess(startInfo);

            return BuildTableResult(standardOutput);
        }

        public ReverseEngineerResult GetOutput()
        {
            var path = Path.GetTempFileName() + "json";
            File.WriteAllText(path, options.Write());

            var launchPath = Path.Combine(Path.GetTempPath(), "efreveng");

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(launchPath ?? throw new InvalidOperationException(), GetExeName()),
                Arguments = "\"" + path + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var standardOutput = RunProcess(startInfo);

            return BuildResult(standardOutput);
        }

        private string GetExeName()
        {
            return useEFCore5 ? "efreveng50.exe" : "efreveng.exe";
        }

        private bool IsDotnetInstalled()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var result = RunProcess(startInfo);

            return result.StartsWith("3.", StringComparison.OrdinalIgnoreCase)
                || result.StartsWith("5.", StringComparison.OrdinalIgnoreCase);
        }

        private static string RunProcess(ProcessStartInfo startInfo)
        {
            var standardOutput = new StringBuilder();
            using (var process = Process.Start(startInfo))
            {
                while (process != null && !process.HasExited)
                {
                    standardOutput.Append(process.StandardOutput.ReadToEnd());
                }
                if (process != null) standardOutput.Append(process.StandardOutput.ReadToEnd());
            }

            return standardOutput.ToString();
        }

        private ReverseEngineerResult BuildResult(string output)
        {
            if (output.StartsWith("Result:" + Environment.NewLine))
            {
                var result = output.Split(new[] { "Result:" + Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                TryRead(result[0], out ReverseEngineerResult deserialized);

                return deserialized;
            }

            if (output.StartsWith("Error:" + Environment.NewLine))
            {
                var result = output.Split(new[] { "Error:" + Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                throw new Exception("Reverse engineer error: " + Environment.NewLine + result[0]);
            }

            throw new Exception($"Reverse engineer error: Unable to launch external process: {output}");
        }

        private List<TableInformationModel> BuildTableResult(string output)
        {
            if (output.StartsWith("Result:" + Environment.NewLine))
            {
                var result = output.Split(new[] { "Result:" + Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                TryRead(result[0], out List<TableInformationModel> deserialized);

                return deserialized;
            }

            if (output.StartsWith("Error:" + Environment.NewLine))
            {
                var result = output.Split(new[] { "Error:" + Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                throw new Exception("Table list error: " + Environment.NewLine + result[0]);
            }

            throw new Exception($"Table list error: Unable to launch external process: {output}");
        }

        private static bool TryRead<T>(string options, out T deserialized) where T : class, new()
        {
            try
            {
                var ms = new MemoryStream(Encoding.UTF8.GetBytes(options));
                var ser = new DataContractJsonSerializer(typeof(T));
                deserialized = ser.ReadObject(ms) as T;
                ms.Close();
                return true;
            }
            catch
            {
                deserialized = null;
                return false;
            }
        }

        private string DropNetCoreFiles()
        {
            var toDir = Path.Combine(Path.GetTempPath(), "efreveng");
            var fromDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            Debug.Assert(fromDir != null, nameof(fromDir) + " != null");
            Debug.Assert(toDir != null, nameof(toDir) + " != null");

            if (Directory.Exists(toDir))
            {
                Directory.Delete(toDir, true);
            }

            Directory.CreateDirectory(toDir);

            ZipFile.ExtractToDirectory(Path.Combine(fromDir, "efreveng.exe.zip"), toDir);

            if (useEFCore5)
            {
                using (var archive = ZipFile.Open(Path.Combine(fromDir, "efreveng50.exe.zip"), ZipArchiveMode.Read))
                {
                    archive.ExtractToDirectory(toDir, true);
                }
            }

            return toDir;
        }
    }
}