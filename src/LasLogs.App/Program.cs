using System;

namespace LasLogs.App
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: LasLogs.App <input-folder> <output-folder>");
                return 2;
            }

            Console.WriteLine("Batch processing is not implemented yet (see docs/PLAN.md).");
            return 0;
        }
    }
}
