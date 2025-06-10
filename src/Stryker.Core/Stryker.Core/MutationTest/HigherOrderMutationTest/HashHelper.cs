using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Stryker.Core.MutationTest
{
    public static class HashHelper
    {
        public static string ComputeDirectoryHash(string directoryPath)
        {
            var sb = new StringBuilder();
            foreach (var file in Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories))
            {
                sb.Append(ComputeFileHash(file));
            }
            return ComputeStringHash(sb.ToString());
        }

        public static string ComputeFileHash(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }

        public static string ComputeStringHash(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}
