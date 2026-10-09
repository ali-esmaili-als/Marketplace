using System;
using Marketplace.Domain.Delivery;
using Xunit;
namespace Marketplace.Domain.Tests;
public sealed class DeliveryCodeTests
{
 [Fact] public void Valid_code_can_be_used_once(){var c=DeliveryCode.Create(1,2,"123456",DateTime.UtcNow.AddMinutes(10));Assert.True(c.Verify("123456",DateTime.UtcNow));Assert.False(c.Verify("123456",DateTime.UtcNow));}
 [Fact] public void Wrong_code_does_not_unlock(){var c=DeliveryCode.Create(1,2,"123456",DateTime.UtcNow.AddMinutes(10));Assert.False(c.Verify("654321",DateTime.UtcNow));}
 [Fact] public void Expired_code_is_rejected(){var c=DeliveryCode.Create(1,2,"123456",DateTime.UtcNow.AddMilliseconds(1));Assert.False(c.Verify("123456",DateTime.UtcNow.AddSeconds(2)));}

 [Fact] public void Five_failed_attempts_lock_the_code_even_when_correct_code_is_submitted(){var c=DeliveryCode.Create(2,3,"123456",DateTime.UtcNow.AddMinutes(10));var now=DateTime.UtcNow;for(var i=0;i<5;i++)Assert.False(c.Verify("000000",now));Assert.Equal(5,c.FailedAttempts);Assert.False(c.Verify("123456",now));Assert.Equal(5,c.FailedAttempts);}
 [Fact] public void Successful_verification_trims_code_and_prevents_reuse(){var c=DeliveryCode.Create(3,4," 123456 ",DateTime.UtcNow.AddMinutes(10));var now=DateTime.UtcNow;Assert.True(c.Verify(" 123456 ",now));Assert.Equal(now,c.UsedAtUtc);Assert.False(c.Verify("123456",now.AddSeconds(1)));}
 [Fact] public void Invalid_creation_inputs_are_rejected(){Assert.Throws<Marketplace.Domain.Common.DomainException>(()=>DeliveryCode.Create(0,1,"123456",DateTime.UtcNow.AddMinutes(1)));Assert.Throws<Marketplace.Domain.Common.DomainException>(()=>DeliveryCode.Create(1,1," ",DateTime.UtcNow.AddMinutes(1)));Assert.Throws<Marketplace.Domain.Common.DomainException>(()=>DeliveryCode.Create(1,1,"123456",DateTime.UtcNow.AddMinutes(-1)));}
}
