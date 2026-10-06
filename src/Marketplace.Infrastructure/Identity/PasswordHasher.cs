using System.Security.Cryptography;
namespace Marketplace.Infrastructure.Identity;
internal static class PasswordHasher
{
 private const int SaltSize=16,KeySize=32,Iterations=100_000;
 public static string Hash(string password){if(string.IsNullOrWhiteSpace(password))throw new ArgumentException("Password is required.",nameof(password));var salt=RandomNumberGenerator.GetBytes(SaltSize);var key=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,KeySize);return "PBKDF2-SHA256$"+Iterations+"$"+Convert.ToBase64String(salt)+"$"+Convert.ToBase64String(key);}
 public static bool Verify(string password,string encoded){try{var p=encoded.Split('$');if(p.Length!=4||p[0]!="PBKDF2-SHA256")return false;var iterations=int.Parse(p[1]);var salt=Convert.FromBase64String(p[2]);var expected=Convert.FromBase64String(p[3]);var actual=Rfc2898DeriveBytes.Pbkdf2(password,salt,iterations,HashAlgorithmName.SHA256,expected.Length);return CryptographicOperations.FixedTimeEquals(actual,expected);}catch{return false;}}
}