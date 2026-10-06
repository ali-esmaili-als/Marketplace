namespace Marketplace.Application.Complaints.Ports;

public interface IComplaintService
{
    Task<long> OpenAsync(long orderId, long customerId, string subject, string description, CancellationToken cancellationToken = default);
    Task ResolveForCustomerAsync(long complaintId, CancellationToken cancellationToken = default);
    Task ResolveForSellerAsync(long complaintId, CancellationToken cancellationToken = default);
    Task CloseAsync(long complaintId, CancellationToken cancellationToken = default);
}