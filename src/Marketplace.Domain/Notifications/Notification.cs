using Marketplace.Domain.Common;
namespace Marketplace.Domain.Notifications;
public enum NotificationChannel:byte{InApp=1,Sms=2,Email=3}
public enum NotificationStatus:byte{Pending=1,Sent=2,Failed=3,Read=4}
public sealed class Notification:AggregateRoot<long>
{
 private Notification(){}
 public long UserId{get;private set;} public NotificationChannel Channel{get;private set;} public NotificationStatus Status{get;private set;} public string Title{get;private set;}=null!; public string Body{get;private set;}=null!; public string? ReferenceType{get;private set;} public long? ReferenceId{get;private set;} public DateTime CreatedAtUtc{get;private set;} public DateTime? SentAtUtc{get;private set;} public DateTime? ReadAtUtc{get;private set;}
 public static Notification Create(long id,long userId,NotificationChannel channel,string title,string body,string? referenceType=null,long? referenceId=null){if(id<=0||userId<=0||string.IsNullOrWhiteSpace(title)||string.IsNullOrWhiteSpace(body))throw new DomainException("Invalid notification.");return new Notification{Id=id,UserId=userId,Channel=channel,Status=NotificationStatus.Pending,Title=title.Trim(),Body=body.Trim(),ReferenceType=referenceType?.Trim(),ReferenceId=referenceId,CreatedAtUtc=DateTime.UtcNow};}
 public void MarkSent(){if(Status!=NotificationStatus.Pending)throw new DomainException("Notification is not pending.");Status=NotificationStatus.Sent;SentAtUtc=DateTime.UtcNow;}
 public void MarkRead(){if(Status==NotificationStatus.Read)return;Status=NotificationStatus.Read;ReadAtUtc=DateTime.UtcNow;}
 public void Fail(){if(Status==NotificationStatus.Sent||Status==NotificationStatus.Read)throw new DomainException("Sent notification cannot fail.");Status=NotificationStatus.Failed;}
}