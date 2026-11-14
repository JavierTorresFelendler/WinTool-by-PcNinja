using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WinTool.Core.Security;
using Xunit;

namespace WinTool.Core.Tests
{
    public class AuthenticodeVerifierTests
    {
        [Theory]
        [InlineData(true, 0u, true)]
        [InlineData(true, 0x800B0109u, false)]
        [InlineData(false, 0u, false)]
        public void Policy_success_requires_both_evaluation_and_zero_error(bool evaluated, uint error, bool expected) =>
            Assert.Equal(expected, new AuthenticodeVerifier.RootPolicyResult(evaluated, error).Succeeded);

        [Fact]
        public void True_with_nonzero_error_is_rejected_by_both_production_variants()
        {
            var flags = new List<uint>();
            Assert.False(AuthenticodeVerifier.CheckMicrosoftRoot(value =>
            {
                flags.Add(value);
                return new AuthenticodeVerifier.RootPolicyResult(true, 0x800B0109);
            }));
            Assert.Equal(new uint[] { 0, 0x20000 }, flags);
        }

        [Fact]
        public void Application_root_is_checked_only_after_the_default_variant_fails()
        {
            var flags = new List<uint>();
            Assert.True(AuthenticodeVerifier.CheckMicrosoftRoot(value =>
            {
                flags.Add(value);
                return new AuthenticodeVerifier.RootPolicyResult(true, value == 0 ? 0x800B0109u : 0u);
            }));
            Assert.Equal(new uint[] { 0, 0x20000 }, flags);
        }

        [Theory]
        [InlineData("CN=Microsoft Corporation Test, O=Other", false)]
        [InlineData("CN=Test, O=Microsoft Corporation Other", false)]
        [InlineData("CN=Test, O=Microsoft Corporation", true)]
        public void Organization_is_an_exact_attribute_match(string subject, bool expected)
        {
            using (var rsa = RSA.Create())
            {
                rsa.KeySize = 2048;
                var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using (var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)))
                    Assert.Equal(expected, AuthenticodeVerifier.HasMicrosoftOrganization(certificate));
            }
        }

        [Fact]
        public void Self_signed_certificate_is_not_a_Microsoft_root_even_with_the_exact_organization()
        {
            using (var rsa = RSA.Create())
            {
                rsa.KeySize = 2048;
                var request = new CertificateRequest("CN=Test, O=Microsoft Corporation", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using (var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)))
                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                    chain.Build(certificate); // No AIA/CRL URLs or store changes; creates an isolated untrusted test chain.
                    Assert.True(AuthenticodeVerifier.HasMicrosoftOrganization(certificate));
                    Assert.False(AuthenticodeVerifier.CheckMicrosoftRoot(flags => AuthenticodeVerifier.VerifyRoot(chain.ChainContext, flags)));
                }
            }
        }

        [Fact]
        public void Unsigned_and_tampered_files_are_refused()
        {
            using (var temp = new TempDirectory())
            {
                var unsigned = temp.File("unsigned.exe");
                File.WriteAllText(unsigned, "fixture");
                Assert.False(AuthenticodeVerifier.IsTrustedMicrosoftFile(unsigned, out _));
                var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "curl.exe");
                Assert.True(AuthenticodeVerifier.IsTrustedMicrosoftFile(source, out _));
                var tampered = temp.File("tampered.exe");
                var bytes = File.ReadAllBytes(source);
                bytes[1024] ^= 1;
                File.WriteAllBytes(tampered, bytes);
                Assert.False(AuthenticodeVerifier.IsTrustedMicrosoftFile(tampered, out _));
            }
        }
    }
}
