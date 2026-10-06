using Marketplace.Domain.Notifications;
namespace Marketplace.Application.Abstractions;
public interface INotificationRepository
{
 Task<List<Notification>> GetForUserAsync(long userId,int take,CancellationToken ct=default);
 Task<Notification?> GetForUserAsync(long userId,long id,CancellationToken ct=default);
 void Add(Notification notification);
}
public interface INotificationSender{Task SendAsync(Notification notification,CancellationToken ct=default);}