using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

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
            int i = 0;
            foreach (var chunk in files.Chunks(16))
            {
                token.SubmitProgress(i / (double)files.Length);
                RunProcess(chunk);
                i += 16;
            }

            token.SubmitProgress(1);
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
