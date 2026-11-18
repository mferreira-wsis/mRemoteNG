using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace mRemoteNG.MultiUser
{
    /// <summary>
    /// PBKDF2-HMAC-SHA256 password hashing for application users.
    /// </summary>
    public static class PasswordHasher
    {
        public const int DefaultIterations = 100000;
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;

        public class HashResult
        {
            public byte[] Hash { get; set; }
            public byte[] Salt { get; set; }
            public int Iterations { get; set; }
        }

        public static HashResult Hash(string password)
        {
            var salt = new byte[SaltSizeBytes];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);

            return new HashResult
            {
                Salt = salt,
                Iterations = DefaultIterations,
                Hash = Derive(password, salt, DefaultIterations)
            };
        }

        public static bool Verify(string password, byte[] expectedHash, byte[] salt, int iterations)
        {
            if (password == null || expectedHash == null || salt == null || iterations <= 0)
                return false;

            var actual = Derive(password, salt, iterations);
            return FixedTimeEquals(actual, expectedHash);
        }

        private static byte[] Derive(string password, byte[] salt, int iterations)
        {
            var generator = new Pkcs5S2ParametersGenerator(new Sha256Digest());
            generator.Init(PbeParametersGenerator.Pkcs5PasswordToUtf8Bytes(password.ToCharArray()), salt, iterations);
            var key = (KeyParameter)generator.GenerateDerivedMacParameters(HashSizeBytes * 8);
            return key.GetKey();
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
