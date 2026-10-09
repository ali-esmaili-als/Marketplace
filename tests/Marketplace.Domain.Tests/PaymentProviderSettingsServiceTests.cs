using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Payments;
using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class PaymentProviderSettingsServiceTests
{
    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"secret\"")]
    public async Task Configure_RejectsInvalidOrNonObjectConfiguration(string configuration)
    {
        var admin = new FakePaymentProviderSettingsAdmin();
        var service = new PaymentProviderSettingsService(admin);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.ConfigureAsync(PaymentProviderCode.TestBank, true, true, 0, configuration));

        Assert.False(admin.ConfigureCalled);
    }

    [Fact]
    public async Task Configure_AcceptsJsonObjectAndNormalizesEmptyConfiguration()
    {
        var admin = new FakePaymentProviderSettingsAdmin();
        var service = new PaymentProviderSettingsService(admin);

        await service.ConfigureAsync(PaymentProviderCode.TestBank, true, true, 0, "  ");

        Assert.True(admin.ConfigureCalled);
        Assert.Equal("{}", admin.Configuration);
    }

    [Fact]
    public async Task Configure_PassesValidJsonObjectToAdmin()
    {
        var admin = new FakePaymentProviderSettingsAdmin();
        var service = new PaymentProviderSettingsService(admin);

        await service.ConfigureAsync(PaymentProviderCode.TestBank, true, true, 0, "{\"merchantId\":\"test\"}");

        Assert.True(admin.ConfigureCalled);
        Assert.Equal("{\"merchantId\":\"test\"}", admin.Configuration);
    }

    private sealed class FakePaymentProviderSettingsAdmin : IPaymentProviderSettingsAdmin
    {
        public bool ConfigureCalled { get; private set; }
        public string? Configuration { get; private set; }

        public Task<IReadOnlyList<PaymentProviderSettingDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PaymentProviderSettingDto>>(Array.Empty<PaymentProviderSettingDto>());

        public Task ConfigureAsync(PaymentProviderCode provider, bool isEnabled, bool isVisible, int sortOrder, string configurationJson, CancellationToken ct = default, long? adminUserId = null)
        {
            ConfigureCalled = true;
            Configuration = configurationJson;
            return Task.CompletedTask;
        }
    }
}
