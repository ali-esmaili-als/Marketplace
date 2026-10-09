using Marketplace.Domain.Notifications;
namespace Marketplace.Application.Abstractions;
public interface INotificationRepository
{
 Task<List<Notification>> GetForUserAsync(long userId,int take,CancellationToken ct=default);
 Task<Notification?> GetForUserAsync(long userId,long id,CancellationToken ct=default);
 Task<int> GetUnreadCountAsync(long userId,CancellationToken ct=default);
 Task<long?> GetUserIdForSellerAsync(long sellerId,CancellationToken ct=default);
 Task<int> MarkAllReadAsync(long userId,CancellationToken ct=default);
 void Add(Notification notification);
}
public interface INotificationSender{Task SendAsync(Notification notification,CancellationToken ct=default);}