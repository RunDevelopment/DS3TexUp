﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace DS3TexUpUI
{
    public class Yabber
    {
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
        public static void RunParallel(string[] files)
        {
            var degreeOfParallelism = AppConfig.Instance.MaxDegreeOfParallelism;
            var chunks = files.Chunks(Math.Min(1, Math.Max(files.Length / degreeOfParallelism, 16)));

            Parallel.ForEach(chunks, RunProcess);
        }
        public static void RunParallel(IProgressToken token, string[] files)
        {
            var degreeOfParallelism = AppConfig.Instance.MaxDegreeOfParallelism;
            var chunks = files.Chunks(Math.Min(1, Math.Max(files.Length / degreeOfParallelism, 16)));

            token.ForAllParallel(chunks, files.Length, chunk =>
            {
                RunProcess(chunk);
                return chunk.Length;
            });
        }

        private static void RunProcess(string[] files)
        {
            var info = new ProcessStartInfo();
            info.FileName = AppConfig.Instance.YabberExe;
            foreach (var file in files)
            {
                info.ArgumentList.Add(file);
            }
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.RedirectStandardInput = true;
            info.CreateNoWindow = true;

            using var process = new Process();
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
