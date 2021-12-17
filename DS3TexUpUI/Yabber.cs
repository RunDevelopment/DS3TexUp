using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Linq;

namespace DS3TexUpUI
{
    public class Yabber
    {
        private static readonly string _yabberDir = @"C:\mods\DS3\Yabber 1.3.1";
        private static readonly string _yabberExe = _yabberDir + @"\Yabber.exe";

        public static void Run(params string[] files)
        {
            foreach (var chunk in files.Chunks(16))
            {
                RunProcess(chunk);
            }
        }
        public static void Run(IProgressToken token, params string[] files)
        {
            token.ForAll(files.Chunks(16), files.Length, chunk =>
            {
                RunProcess(chunk);
                return chunk.Length;
            });
        }
        public static void RunParallel(string[] files, int degreeOfParallelism = 0)
        {
            if (degreeOfParallelism == 0) degreeOfParallelism = (int)Math.Ceiling(Environment.ProcessorCount * 2.0 / 3.0);

            files
                .Chunks(Math.Min(1, Math.Max(files.Length / degreeOfParallelism, 16)))
                .AsParallel()
                .WithDegreeOfParallelism(degreeOfParallelism)
                .ForAll(chunk => RunProcess(chunk));
        }
        public static void RunParallel(IProgressToken token, string[] files, int degreeOfParallelism = 0)
        {
            if (degreeOfParallelism == 0) degreeOfParallelism = (int)Math.Ceiling(Environment.ProcessorCount * 2.0 / 3.0);

            token.ForAll(
                files
                    .Chunks(Math.Min(1, Math.Max(files.Length / degreeOfParallelism, 16)))
                    .AsParallel()
                    .WithDegreeOfParallelism(degreeOfParallelism),
                files.Length,
                chunk =>
                {
                    RunProcess(chunk);
                    return chunk.Length;
                }
            );
        }

        private static void RunProcess(string[] files)
        {
            var info = new ProcessStartInfo();
            info.FileName = _yabberExe;
            foreach (var file in files)
            {
                info.ArgumentList.Add(file);
            }
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.RedirectStandardInput = true;
            info.CreateNoWindow = true;

            var process = new Process();
            process.StartInfo = info;
            process.Start();

            var error = process.StandardError.ReadToEnd();
            if (error.Length > 0)
            {
                Console.WriteLine(error);
            }
        }
    }
}
