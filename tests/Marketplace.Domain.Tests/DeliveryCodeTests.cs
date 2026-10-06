using Marketplace.Domain.Delivery;
using Xunit;
namespace Marketplace.Domain.Tests;
public sealed class DeliveryCodeTests
{
 [Fact] public void Valid_code_can_be_used_once(){var c=DeliveryCode.Create(1,2,"123456",DateTime.UtcNow.AddMinutes(10));Assert.True(c.Verify("123456",DateTime.UtcNow));Assert.False(c.Verify("123456",DateTime.UtcNow));}
 [Fact] public void Wrong_code_does_not_unlock(){var c=DeliveryCode.Create(1,2,"123456",DateTime.UtcNow.AddMinutes(10));Assert.False(c.Verify("654321",DateTime.UtcNow));}
 [Fact] public void Expired_code_is_rejected(){var c=DeliveryCode.Create(1,2,"123456",DateTime.UtcNow.AddMilliseconds(1));Assert.False(c.Verify("123456",DateTime.UtcNow.AddSeconds(2)));}
}