using Marketplace.Application.Abstractions;
using Marketplace.Domain.Notifications;
namespace Marketplace.Application.Notifications;
public sealed class NotificationService
{
 private readonly INotificationRepository _repo; private readonly IIdGenerator _ids; private readonly IUnitOfWork _uow;
 public NotificationService(INotificationRepository repo,IIdGenerator ids,IUnitOfWork uow){_repo=repo;_ids=ids;_uow=uow;}
 public async Task CreateInAppAsync(long userId,string title,string body,string? type=null,long? refId=null,CancellationToken ct=default){var n=Notification.Create(await _ids.NextAsync(ct),userId,NotificationChannel.InApp,title,body,type,refId);_repo.Add(n);n.MarkSent();await _uow.SaveChangesAsync(ct);}
 public Task<List<Notification>> GetAsync(long userId,int take,CancellationToken ct=default)=>_repo.GetForUserAsync(userId,Math.Clamp(take,1,100),ct);
 public Task<int> GetUnreadCountAsync(long userId,CancellationToken ct=default)=>_repo.GetUnreadCountAsync(userId,ct);
 public async Task<int> MarkAllReadAsync(long userId,CancellationToken ct=default){var count=await _repo.MarkAllReadAsync(userId,ct);if(count>0)await _uow.SaveChangesAsync(ct);return count;}
 public async Task MarkReadAsync(long userId,long id,CancellationToken ct=default){var n=await _repo.GetForUserAsync(userId,id,ct)??throw new Marketplace.Domain.Common.DomainException("Notification not found.");n.MarkRead();await _uow.SaveChangesAsync(ct);}
}