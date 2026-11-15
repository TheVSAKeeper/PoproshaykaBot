using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace PoproshaykaBot.Core.Platforms.Auth;

public readonly record struct PkceCodeChallenge(string Verifier, string Challenge)
{
    public static PkceCodeChallenge Create()
    {
        var verifier = CreateRandomToken(32);
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new(verifier, challenge);
    }

    public static string CreateRandomToken(int byteLength = 24)
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(byteLength));
    }
}
