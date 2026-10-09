using System.Security.Cryptography;
using System.Text;
using Marketplace.Domain.Common;
namespace Marketplace.Domain.Delivery;
public sealed class DeliveryCode:AggregateRoot<long>
{
 private DeliveryCode(){}
 public long OrderId{get;private set;} public byte[] CodeHash{get;private set;}=Array.Empty<byte>(); public DateTime ExpiresAtUtc{get;private set;} public DateTime IssuedAtUtc{get;private set;} public DateTime? UsedAtUtc{get;private set;} public int FailedAttempts{get;private set;}
 public static DeliveryCode Create(long id,long orderId,string code,DateTime expiresAtUtc){if(id<=0||orderId<=0||string.IsNullOrWhiteSpace(code)||expiresAtUtc<=DateTime.UtcNow)throw new DomainException("Invalid delivery code.");return new DeliveryCode{Id=id,OrderId=orderId,CodeHash=Hash(code),ExpiresAtUtc=expiresAtUtc,IssuedAtUtc=DateTime.UtcNow};}
 public bool Verify(string code,DateTime nowUtc){if(UsedAtUtc.HasValue||nowUtc>ExpiresAtUtc||FailedAttempts>=5)return false;var ok=CryptographicOperations.FixedTimeEquals(CodeHash,Hash(code));if(!ok){FailedAttempts++;return false;}UsedAtUtc=nowUtc;return true;}
 private static byte[] Hash(string code)=>SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim()));
}