namespace Marketplace.Application.Complaints.Ports;

public interface IComplaintService
{
    Task<long> OpenAsync(long orderId, long customerId, string subject, string description, CancellationToken cancellationToken = default);
}