using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shadowsocks.Encryption;
using Shadowsocks.Encryption.Stream;
using Shadowsocks.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTest
{
    [TestClass]
    public class EncryptionTest
    {
        private void RunEncryptionRound(IEncryptor encryptor, IEncryptor decryptor)
        {
            var plain = new byte[16384];
            var cipher = new byte[plain.Length + 16];
            var plain2 = new byte[plain.Length + 16];
            Rng.RandBytes(plain);
            encryptor.Encrypt(plain, plain.Length, cipher, out var outLen);
            decryptor.Decrypt(cipher, outLen, plain2, out var outLen2);
            Assert.AreEqual(plain.Length, outLen2);
            for (var j = 0; j < plain.Length; j++)
            {
                Assert.AreEqual(plain[j], plain2[j]);
            }
            encryptor.Encrypt(plain, 1000, cipher, out outLen);
            decryptor.Decrypt(cipher, outLen, plain2, out outLen2);
            Assert.AreEqual(1000, outLen2);
            for (var j = 0; j < outLen2; j++)
            {
                Assert.AreEqual(plain[j], plain2[j]);
            }
            encryptor.Encrypt(plain, 12333, cipher, out outLen);
            decryptor.Decrypt(cipher, outLen, plain2, out outLen2);
            Assert.AreEqual(12333, outLen2);
            for (var j = 0; j < outLen2; j++)
            {
                Assert.AreEqual(plain[j], plain2[j]);
            }
        }

        [TestMethod]
        public void TestStreamOpenSSLEncryption()
        {
            var failed = false;
            // run it once before the multi-threading test to initialize global tables
            RunSingleStreamOpenSSLEncryptionThread();

            var tasks = new List<Task>();
            foreach (var cipher in StreamOpenSSLEncryptor.SupportedCiphers())
            {
                if (cipher.EndsWith(@"-cbc"))
                {
                    continue;
                }
                var t = new Task(() =>
                {
                    try
                    {
                        RunSingleStreamOpenSSLEncryptionThread(cipher);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($@"{cipher}:{e.Message}");
                        failed = true;
                        throw;
                    }
                });
                tasks.Add(t);
                t.Start();
            }

            Task.WaitAll(tasks.ToArray());
            Assert.IsFalse(failed);
        }

        private void RunSingleStreamOpenSSLEncryptionThread(string methodName = @"aes-256-cfb8", string password = @"barfoo!")
        {
            for (var i = 0; i < 100; i++)
            {
                IEncryptor encryptor = new StreamOpenSSLEncryptor(methodName, password);
                IEncryptor decryptor = new StreamOpenSSLEncryptor(methodName, password);
                RunEncryptionRound(encryptor, decryptor);
            }
        }

        [TestMethod]
        public void TestStreamSodiumEncryption()
        {
            var failed = false;
            // run it once before the multi-threading test to initialize global tables
            RunSingleStreamSodiumEncryptionThread();
            var tasks = new List<Task>();
            foreach (var cipher in StreamSodiumEncryptor.SupportedCiphers())
            {
                if (cipher.StartsWith(@"x"))
                {
                    continue;
                }
                var t = new Task(() =>
                {
                    try
                    {
                        RunSingleStreamSodiumEncryptionThread(cipher);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($@"{cipher}:{e.Message}");
                        failed = true;
                        throw;
                    }
                });
                tasks.Add(t);
                t.Start();
            }

            Task.WaitAll(tasks.ToArray());
            Assert.IsFalse(failed);
        }

        private void RunSingleStreamSodiumEncryptionThread(string methodName = @"salsa20", string password = @"barfoo!")
        {
            for (var i = 0; i < 100; i++)
            {
                IEncryptor encryptor = new StreamSodiumEncryptor(methodName, password);
                IEncryptor decryptor = new StreamSodiumEncryptor(methodName, password);
                RunEncryptionRound(encryptor, decryptor);
            }
        }

        // ============================================================
        // NEW TEST METHODS — cover all registered ciphers
        // ============================================================

        private const string TestPassword = @"testpass!";

        /// <summary>
        /// Basic round-trip helper: Encrypt a buffer of given length, then Decrypt, verify.
        /// Creates fresh encryptor/decryptor pair per call.
        /// When length is 0, only the Encrypt path is tested (Decrypt disallows 0-length input).
        /// </summary>
        private void RunBasicRoundTrip(string method, int length)
        {
            var plain = new byte[length];
            if (length > 0)
            {
                Rng.RandBytes(plain);
            }
            // Output buffer must accommodate IV (up to 32 bytes for xchacha20/xsalsa20) + ciphertext
            var cipher = new byte[length + 64];
            var plain2 = new byte[length + 64];

            var encryptor = EncryptorFactory.GetEncryptor(method, TestPassword);
            var decryptor = EncryptorFactory.GetEncryptor(method, TestPassword);

            try
            {
                encryptor.Encrypt(plain, length, cipher, out var encLen);

                if (length == 0)
                {
                    // Encrypt with 0-length input should return 0 output
                    Assert.AreEqual(0, encLen,
                        $@"[{method}] Empty Encrypt should return outlength=0");
                    // Decrypt with 0-length input is not supported by ByteCircularBuffer.Put
                    return;
                }

                decryptor.Decrypt(cipher, encLen, plain2, out var decLen);

                Assert.AreEqual(length, decLen,
                    $@"[{method}] Decrypted length {decLen} != original length {length}");
                for (var i = 0; i < length; i++)
                {
                    Assert.AreEqual(plain[i], plain2[i],
                        $@"[{method}] Byte mismatch at index {i}, length={length}");
                }
            }
            finally
            {
                encryptor.Dispose();
                decryptor.Dispose();
            }
        }

        /// <summary>
        /// Like RunEncryptionRound but with larger output buffers to accommodate
        /// ciphers with large IVs (xsalsa20: 24 bytes, xchacha20: 24 bytes).
        /// Tests 3 sizes: 16384, 1000, 12333.
        /// </summary>
        private void RunEncryptionRoundLargeIv(IEncryptor encryptor, IEncryptor decryptor)
        {
            var plain = new byte[16384];
            var cipher = new byte[plain.Length + 64];
            var plain2 = new byte[plain.Length + 64];
            Rng.RandBytes(plain);
            encryptor.Encrypt(plain, plain.Length, cipher, out var outLen);
            decryptor.Decrypt(cipher, outLen, plain2, out var outLen2);
            Assert.AreEqual(plain.Length, outLen2);
            for (var j = 0; j < plain.Length; j++)
            {
                Assert.AreEqual(plain[j], plain2[j]);
            }
            encryptor.Encrypt(plain, 1000, cipher, out outLen);
            decryptor.Decrypt(cipher, outLen, plain2, out outLen2);
            Assert.AreEqual(1000, outLen2);
            for (var j = 0; j < outLen2; j++)
            {
                Assert.AreEqual(plain[j], plain2[j]);
            }
            encryptor.Encrypt(plain, 12333, cipher, out outLen);
            decryptor.Decrypt(cipher, outLen, plain2, out outLen2);
            Assert.AreEqual(12333, outLen2);
            for (var j = 0; j < outLen2; j++)
            {
                Assert.AreEqual(plain[j], plain2[j]);
            }
        }

        /// <summary>
        /// Run multi-size round-trip (16384, 1000, 12333) with factory-created encryptors
        /// using large-IV-aware buffers.
        /// </summary>
        private void RunFactoryEncryptionRound(string method)
        {
            var encryptor = EncryptorFactory.GetEncryptor(method, TestPassword);
            var decryptor = EncryptorFactory.GetEncryptor(method, TestPassword);
            try
            {
                RunEncryptionRoundLargeIv(encryptor, decryptor);
            }
            finally
            {
                encryptor.Dispose();
                decryptor.Dispose();
            }
        }

        /// <summary>
        /// Run basic round-trip with a single Encrypt call then a single Decrypt call.
        /// Uses a moderate random-size buffer.
        /// </summary>
        private void RunSingleCallRoundTrip(string method, int length)
        {
            var plain = new byte[length];
            if (length > 0)
            {
                Rng.RandBytes(plain);
            }
            var cipher = new byte[length + 64];
            var plain2 = new byte[length + 64];

            var encryptor = EncryptorFactory.GetEncryptor(method, TestPassword);
            var decryptor = EncryptorFactory.GetEncryptor(method, TestPassword);

            try
            {
                // Single Encrypt call
                encryptor.Encrypt(plain, length, cipher, out var encLen);
                Assert.IsTrue(encLen >= length,
                    $@"[{method}] Ciphertext length {encLen} < plaintext length {length}");

                // Single Decrypt call
                decryptor.Decrypt(cipher, encLen, plain2, out var decLen);
                Assert.AreEqual(length, decLen,
                    $@"[{method}] Single-call decrypt produced {decLen} bytes, expected {length}");

                for (var i = 0; i < length; i++)
                {
                    Assert.AreEqual(plain[i], plain2[i],
                        $@"[{method}] Single-call byte mismatch at index {i}");
                }
            }
            finally
            {
                encryptor.Dispose();
                decryptor.Dispose();
            }
        }

        [TestMethod]
        public void TestAllRegisteredCiphersRoundTrip()
        {
            var failed = false;
            var allMethods = EncryptorFactory.RegisteredEncryptors.Keys.ToList();

            // Run once before multi-threading to initialize global tables
            RunFactoryEncryptionRound(allMethods[0]);

            var tasks = new List<Task>();
            foreach (var method in allMethods)
            {
                var capturedMethod = method;
                var t = new Task(() =>
                {
                    try
                    {
                        for (var i = 0; i < 100; i++)
                        {
                            RunFactoryEncryptionRound(capturedMethod);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($@"{capturedMethod}:{e.Message}");
                        failed = true;
                        throw;
                    }
                });
                tasks.Add(t);
                t.Start();
            }

            Task.WaitAll(tasks.ToArray());
            Assert.IsFalse(failed);
        }

        [TestMethod]
        public void TestEmptyBufferRoundTrip()
        {
            var failed = false;
            var allMethods = EncryptorFactory.RegisteredEncryptors.Keys.ToList();

            var tasks = new List<Task>();
            foreach (var method in allMethods)
            {
                var capturedMethod = method;
                var t = new Task(() =>
                {
                    try
                    {
                        for (var i = 0; i < 10; i++)
                        {
                            RunBasicRoundTrip(capturedMethod, 0);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($@"{capturedMethod} (empty):{e.Message}");
                        failed = true;
                        throw;
                    }
                });
                tasks.Add(t);
                t.Start();
            }

            Task.WaitAll(tasks.ToArray());
            Assert.IsFalse(failed);
        }

        [TestMethod]
        public void TestSingleByteRoundTrip()
        {
            var failed = false;
            var allMethods = EncryptorFactory.RegisteredEncryptors.Keys.ToList();

            var tasks = new List<Task>();
            foreach (var method in allMethods)
            {
                var capturedMethod = method;
                var t = new Task(() =>
                {
                    try
                    {
                        for (var i = 0; i < 100; i++)
                        {
                            RunBasicRoundTrip(capturedMethod, 1);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($@"{capturedMethod} (1-byte):{e.Message}");
                        failed = true;
                        throw;
                    }
                });
                tasks.Add(t);
                t.Start();
            }

            Task.WaitAll(tasks.ToArray());
            Assert.IsFalse(failed);
        }

        [TestMethod]
        public void TestMaxSizeBufferRoundTrip()
        {
            const int maxSize = 8192;
            var failed = false;
            var allMethods = EncryptorFactory.RegisteredEncryptors.Keys.ToList();

            var tasks = new List<Task>();
            foreach (var method in allMethods)
            {
                var capturedMethod = method;
                var t = new Task(() =>
                {
                    try
                    {
                        for (var i = 0; i < 50; i++)
                        {
                            RunBasicRoundTrip(capturedMethod, maxSize);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($@"{capturedMethod} (max-size):{e.Message}");
                        failed = true;
                        throw;
                    }
                });
                tasks.Add(t);
                t.Start();
            }

            Task.WaitAll(tasks.ToArray());
            Assert.IsFalse(failed);
        }

        [TestMethod]
        public void TestEncryptDecryptOneCall()
        {
            // Verify that a single Encrypt call followed by a single Decrypt call
            // correctly recovers original data for all registered ciphers.
            var failed = false;
            var allMethods = EncryptorFactory.RegisteredEncryptors.Keys.ToList();
            var random = new Random();

            var tasks = new List<Task>();
            foreach (var method in allMethods)
            {
                var capturedMethod = method;
                var t = new Task(() =>
                {
                    try
                    {
                        for (var i = 0; i < 50; i++)
                        {
                            // Use varying sizes to ensure robustness
                            var length = random.Next(1, 4096);
                            RunSingleCallRoundTrip(capturedMethod, length);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($@"{capturedMethod} (one-call):{e.Message}");
                        failed = true;
                        throw;
                    }
                });
                tasks.Add(t);
                t.Start();
            }

            Task.WaitAll(tasks.ToArray());
            Assert.IsFalse(failed);
        }

        [TestMethod]
        public void TestSequentialEncryptDecrypt()
        {
            // Test the streaming nature: multiple sequential Encrypt calls
            // followed by matching sequential Decrypt calls.
            var failed = false;
            var allMethods = EncryptorFactory.RegisteredEncryptors.Keys.ToList();

            var tasks = new List<Task>();
            foreach (var method in allMethods)
            {
                var capturedMethod = method;
                var t = new Task(() =>
                {
                    try
                    {
                        for (var round = 0; round < 50; round++)
                        {
                            // Generate three random-sized plaintext segments
                            var sizes = new[] { 100, 200, 300 };
                            var plainSegments = new List<byte[]>();
                            foreach (var s in sizes)
                            {
                                var seg = new byte[s];
                                Rng.RandBytes(seg);
                                plainSegments.Add(seg);
                            }

                            var encryptor = EncryptorFactory.GetEncryptor(capturedMethod, TestPassword);
                            var decryptor = EncryptorFactory.GetEncryptor(capturedMethod, TestPassword);

                            try
                            {
                                // Encrypt each segment sequentially
                                var cipherSegments = new List<byte[]>();
                                foreach (var seg in plainSegments)
                                {
                                    var cipher = new byte[seg.Length + 64];
                                    encryptor.Encrypt(seg, seg.Length, cipher, out var encLen);
                                    var result = new byte[encLen];
                                    Buffer.BlockCopy(cipher, 0, result, 0, encLen);
                                    cipherSegments.Add(result);
                                }

                                // Decrypt each segment sequentially
                                for (var s = 0; s < plainSegments.Count; s++)
                                {
                                    var plain2 = new byte[plainSegments[s].Length + 64];
                                    decryptor.Decrypt(cipherSegments[s], cipherSegments[s].Length,
                                        plain2, out var decLen);

                                    Assert.AreEqual(plainSegments[s].Length, decLen,
                                        $@"[{capturedMethod}] Sequential: segment {s} length mismatch");
                                    for (var b = 0; b < plainSegments[s].Length; b++)
                                    {
                                        Assert.AreEqual(plainSegments[s][b], plain2[b],
                                            $@"[{capturedMethod}] Sequential: segment {s} byte {b} mismatch");
                                    }
                                }
                            }
                            finally
                            {
                                encryptor.Dispose();
                                decryptor.Dispose();
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($@"{capturedMethod} (sequential):{e.Message}");
                        failed = true;
                        throw;
                    }
                });
                tasks.Add(t);
                t.Start();
            }

            Task.WaitAll(tasks.ToArray());
            Assert.IsFalse(failed);
        }

        [TestMethod]
        public void TestIvHandling()
        {
            // Verify IV and key handling for all registered ciphers.
            foreach (var kvp in EncryptorFactory.RegisteredEncryptors)
            {
                var method = kvp.Key;
                var encryptor = EncryptorFactory.GetEncryptor(method, TestPassword);
                var decryptor = EncryptorFactory.GetEncryptor(method, TestPassword);

                try
                {
                    // IV should be non-null
                    var ivEnc = encryptor.getIV();
                    Assert.IsNotNull(ivEnc, $@"[{method}] Encryptor IV is null");
                    var ivDec = decryptor.getIV();
                    Assert.IsNotNull(ivDec, $@"[{method}] Decryptor IV is null");

                    // Key should be non-null
                    var keyEnc = encryptor.getKey();
                    Assert.IsNotNull(keyEnc, $@"[{method}] Encryptor key is null");
                    var keyDec = decryptor.getKey();
                    Assert.IsNotNull(keyDec, $@"[{method}] Decryptor key is null");

                    // Key should match between encryptor and decryptor (same password)
                    Assert.AreEqual(keyEnc.Length, keyDec.Length,
                        $@"[{method}] Key length mismatch between encryptor/decryptor");
                    for (var i = 0; i < keyEnc.Length; i++)
                    {
                        Assert.AreEqual(keyEnc[i], keyDec[i],
                            $@"[{method}] Key byte mismatch at index {i}");
                    }

                    // getInfo() should return valid EncryptorInfo
                    var info = encryptor.getInfo();
                    Assert.IsNotNull(info, $@"[{method}] EncryptorInfo is null");
                    Assert.IsTrue(info.KeySize > 0, $@"[{method}] KeySize is 0");

                    // ResetEncrypt should change the IV
                    // Store current IV, reset, and verify it changed (or at least call succeeds)
                    var ivBefore = new byte[ivEnc.Length];
                    Buffer.BlockCopy(ivEnc, 0, ivBefore, 0, ivEnc.Length);

                    encryptor.ResetEncrypt();
                    var ivAfter = encryptor.getIV();
                    Assert.IsNotNull(ivAfter, $@"[{method}] IV after ResetEncrypt is null");
                    Assert.AreEqual(ivBefore.Length, ivAfter.Length,
                        $@"[{method}] IV length changed after ResetEncrypt");

                    // After ResetEncrypt, a subsequent Encrypt should still work
                    var plain = new byte[64];
                    Rng.RandBytes(plain);
                    var cipher = new byte[plain.Length + 64];
                    var plain2 = new byte[plain.Length + 64];

                    encryptor.Encrypt(plain, plain.Length, cipher, out var encLen);

                    // Create a fresh decryptor to pair with the reset encryptor
                    var decryptor2 = EncryptorFactory.GetEncryptor(method, TestPassword);
                    try
                    {
                        decryptor2.Decrypt(cipher, encLen, plain2, out var decLen);
                        Assert.AreEqual(plain.Length, decLen,
                            $@"[{method}] ResetEncrypt round-trip length mismatch");
                        for (var i = 0; i < plain.Length; i++)
                        {
                            Assert.AreEqual(plain[i], plain2[i],
                                $@"[{method}] ResetEncrypt round-trip byte mismatch at {i}");
                        }
                    }
                    finally
                    {
                        decryptor2.Dispose();
                    }
                }
                finally
                {
                    encryptor.Dispose();
                    decryptor.Dispose();
                }
            }
        }

        [TestMethod]
        public void TestMultiThreadedStress()
        {
            // Stress-test: run many concurrent encrypt/decrypt operations on all ciphers.
            var allMethods = EncryptorFactory.RegisteredEncryptors.Keys.ToList();
            var failedCount = 0;
            var random = new Random();

            Parallel.ForEach(allMethods, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                method =>
                {
                    try
                    {
                        // Each task creates its own encryptor/decryptor and runs many iterations
                        Parallel.For(0, 200, _ =>
                        {
                            var length = random.Next(1, 2048);
                            var plain = new byte[length];
                            Rng.RandBytes(plain);

                            var cipher = new byte[length + 64];
                            var plain2 = new byte[length + 64];

                            var encryptor = EncryptorFactory.GetEncryptor(method, TestPassword);
                            var decryptor = EncryptorFactory.GetEncryptor(method, TestPassword);

                            try
                            {
                                encryptor.Encrypt(plain, length, cipher, out var encLen);
                                decryptor.Decrypt(cipher, encLen, plain2, out var decLen);

                                if (decLen != length)
                                {
                                    Interlocked.Increment(ref failedCount);
                                    Console.WriteLine(
                                        $@"[{method}] Stress length mismatch: {decLen} vs {length}");
                                    return;
                                }

                                for (var i = 0; i < length; i++)
                                {
                                    if (plain[i] != plain2[i])
                                    {
                                        Interlocked.Increment(ref failedCount);
                                        Console.WriteLine(
                                            $@"[{method}] Stress byte mismatch at {i}");
                                        return;
                                    }
                                }
                            }
                            finally
                            {
                                encryptor.Dispose();
                                decryptor.Dispose();
                            }
                        });
                    }
                    catch (Exception e)
                    {
                        Interlocked.Increment(ref failedCount);
                        Console.WriteLine($@"[{method}] Stress exception: {e.Message}");
                    }
                });

            Assert.AreEqual(0, failedCount,
                $@"Multi-threaded stress test: {failedCount} failure(s)");
        }
    }
}
