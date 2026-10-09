using Marketplace.Application.Payments;
using Marketplace.Domain.Payments;

namespace Marketplace.Application.Abstractions;

public interface IPaymentProviderSettingsAdmin
{
    Task<IReadOnlyList<PaymentProviderSettingDto>> GetAllAsync(CancellationToken ct=default);
    Task ConfigureAsync(PaymentProviderCode provider,bool isEnabled,bool isVisible,int sortOrder,string configurationJson,CancellationToken ct=default,long? adminUserId=null);
}