using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace DS3TexUpUI
{
    public class TransparencyIndex
    {
        private Dictionary<string, TransparencyKind> _data;

        public TransparencyIndex()
        {
            _data = new Dictionary<string, TransparencyKind>();
        }

        public static Action<SubProgressToken> Create(Workspace w, string dest)
        {
            return token =>
            {
                var index = new TransparencyIndex();

                var files = Directory.GetFiles(w.ExtractDir, "*.dds", SearchOption.AllDirectories);
                token.SubmitStatus($"Indexing {files.Length} files");

                token.ForAllParallel(files, f =>
                {
                    try
                    {
                        var kind = DDSImage.Load(f).GetTransparency();
                        var id = TransparencyIndex.GetFileId(f);
                        lock (index)
                        {
                            index.Set(id, kind);
                        }
                    }
                    catch (System.Exception)
                    {
                        // ignore
                    }
                });

                token.SubmitStatus($"Saving index");
                index.Save(dest);
            };
        }

        public static TransparencyIndex Load(string file)
        {
            using var s = File.OpenText(file);

            var index = new TransparencyIndex();

            string line;
            while (!string.IsNullOrEmpty(line = s.ReadLine()))
            {
                var value = (TransparencyKind)(line[0] - '0');
                var key = line.Substring(2).TrimEnd();
                index._data[key] = value;
            }

            return index;
        }

        public void Save(string file)
        {
            using var f = File.OpenWrite(file);
            using var s = new StreamWriter(f, Encoding.UTF8);

            foreach (var entry in _data)
            {
                s.Write((int)entry.Value);
                s.Write(' ');
                s.Write(entry.Key);
                s.Write('\n');
            }
        }

        public TransparencyKind Get(string id, TransparencyKind defaultValue = TransparencyKind.Full)
        {
            if (_data.TryGetValue(id, out var result))
            {
                return result;
            }
            return defaultValue;
        }

        public void Set(string id, TransparencyKind value)
        {
            _data[id] = value;
        }

        public static string GetFileId(string file)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var index = name.IndexOf('-');
            if (index != -1) name = name.Substring(0, index);

            var dir = Path.GetFileName(Path.GetDirectoryName(file));

            return $"{dir}/{name}";
        }
    }
}
