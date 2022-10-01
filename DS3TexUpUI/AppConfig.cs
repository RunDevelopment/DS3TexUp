using System;
using System.IO;
using System.Threading.Tasks;

namespace DS3TexUpUI
{
    public class AppConfig
    {
        public string YabberExe { get; set; }
        public string TexConvExe { get; set; }
        public int MaxDegreeOfParallelism { get; set; }

        public static readonly AppConfig Instance = LoadInstance();
        private static AppConfig LoadInstance()
        {
            var file = Path.Join(AppDomain.CurrentDomain.BaseDirectory, "appconfig.json");
            var config = file.LoadJsonFile<AppConfig>();
            config.Validate();
            return config;
        }

        public void Validate()
        {
            var change = "Please change 'appconfig.json' to correct the path.";

            if (!File.Exists(YabberExe))
                throw new Exception($"The path to {nameof(YabberExe)} does not exist. {change}");
            if (!File.Exists(TexConvExe))
                throw new Exception($"The path to {nameof(TexConvExe)} does not exist. {change}");

            if (MaxDegreeOfParallelism <= 0)
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount;
            }
        }

        public ParallelOptions GetParallelOptions()
        {
            var options = new ParallelOptions();
            options.MaxDegreeOfParallelism = MaxDegreeOfParallelism;
            return options;
        }
    }
}
