using System;

namespace Xcom.Convert
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string dataDir = ArgValue(args, "--data") ?? "../RawData/Resources/UFO";
            string outDir = ArgValue(args, "--out") ?? "../Assets/GameData";
            var written = ConvertJob.Run(dataDir, outDir);
            Console.WriteLine($"Wrote {written.Count} files to {outDir}");
            return 0;
        }

        private static string? ArgValue(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }
    }
}
