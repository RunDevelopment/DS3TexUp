using System;
using System.IO;

namespace DS3TexUpUI
{
    internal static class Data
    {
        public readonly static string ApplicationDir = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "data");
        private readonly static string LocalDir = Path.GetFullPath("data");

        private readonly static Lazy<bool> hasLocal = new Lazy<bool>(() => Directory.Exists(LocalDir));
        public static bool HasLocal => hasLocal.Value;

        public static string Dir(Source source = Source.Application)
        {
            return source == Source.Local && HasLocal ? LocalDir : ApplicationDir;
        }
        public static string File(string name, Source source = Source.Application)
        {
            return Path.Join(Dir(source), name.Replace("/", "\\"));
        }

        public enum Source
        {
            Application,
            Local
        }
    }
}
