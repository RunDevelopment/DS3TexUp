using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace DS3TexUpUI
{
    public class OverwriteMap
    {
        private readonly Dictionary<string, HashSet<string>> _overwrites = new Dictionary<string, HashSet<string>>();

        public IReadOnlyCollection<string> this[string map]
        {
            get
            {
                if (_overwrites.TryGetValue(map, out var set))
                    return set;
                else
                    return new List<string>();
            }
        }

        public void Add(string map, string file)
        {
            if (_overwrites.TryGetValue(map, out var list))
            {
                list.Add(file);
            }
            else
            {
                _overwrites.Add(map, new HashSet<string> { file });
            }
        }

        public void Save(string filename)
        {
            var s = new StringBuilder();

            foreach (var pair in _overwrites)
            {
                var list = pair.Value.ToList();
                list.Sort();

                foreach (var file in list)
                {
                    s.Append(pair.Key).Append("/").Append(file).Append("\n");
                }
            }

            File.WriteAllText(filename, s.ToString(), Encoding.UTF8);
        }

        public static OverwriteMap Load(string filename)
        {
            var map = new OverwriteMap();

            foreach (var line in File.ReadAllText(filename, Encoding.UTF8).Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                var slash = line.IndexOf('/');
                if (slash == -1)
                    continue;

                map.Add(line.Substring(0, slash), line.Substring(slash + 1));
            }

            return map;
        }
    }

    public readonly struct OverwriteDiff<T>
    {
        public List<T> Restore { get; }
        public List<T> Overwrite { get; }

        public OverwriteDiff(IEnumerable<T> old_, IEnumerable<T> new_)
        {
            Overwrite = new_.ToList();

            var oldSet = new HashSet<T>(old_);
            oldSet.ExceptWith(Overwrite);

            Restore = oldSet.ToList();
        }
    }
}
