using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DS3TexUpUI
{
    public static class Files
    {
        public static void InvertDirectoryStructure(string source, string target)
        {
            InvertDirectoryStructure(new NoopProgressToken(), source, target);
        }
        public static void InvertDirectoryStructure(IProgressToken token, string source, string target)
        {
            var inv = GetInvertedDirectoryStructure(source);

            token.ForAll(inv, pair =>
            {
                var (from, toRelative) = pair;
                var to = Path.Join(target, toRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(from, to, true);
            });
        }
        private static List<(string from, string toRelative)> GetInvertedDirectoryStructure(string source)
        {
            var level1 = Directory.GetDirectories(source).Select(Path.GetFileName).ToArray();

            var level2 = new HashSet<string>();
            foreach (var l1 in level1)
                level2.UnionWith(Directory.GetDirectories(Path.Join(source, l1)).Select(Path.GetFileName));

            var result = new List<(string, string)>();
            foreach (var l1 in level1)
            {
                foreach (var l2 in level2)
                {
                    var d = Path.Join(source, l1, l2);
                    if (Directory.Exists(d))
                    {
                        foreach (var file in Directory.GetFiles(d))
                        {
                            result.Add((file, Path.Join(l2, l1, Path.GetFileName(file))));
                        }
                    }
                }
            }
            return result;
        }
    }
}
