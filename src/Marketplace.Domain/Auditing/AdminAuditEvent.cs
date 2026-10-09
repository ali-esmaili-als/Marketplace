namespace Marketplace.Domain.Auditing;
public sealed class AdminAuditEvent
{
    private AdminAuditEvent() { }
    public long Id { get; private set; }
    public long ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityKey { get; private set; } = string.Empty;
    public string DetailsJson { get; private set; } = "{}";
    public string? CorrelationId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public static AdminAuditEvent Create(long actorUserId,string action,string entityType,string entityKey,string detailsJson,string? correlationId)
    {
        if(actorUserId<=0) throw new ArgumentOutOfRangeException(nameof(actorUserId));
        if(string.IsNullOrWhiteSpace(action)||action.Length>100) throw new ArgumentException("Audit action is invalid.",nameof(action));
        if(string.IsNullOrWhiteSpace(entityType)||entityType.Length>100) throw new ArgumentException("Audit entity type is invalid.",nameof(entityType));
        if(string.IsNullOrWhiteSpace(entityKey)||entityKey.Length>200) throw new ArgumentException("Audit entity key is invalid.",nameof(entityKey));
        if(string.IsNullOrWhiteSpace(detailsJson)||detailsJson.Length>2000) throw new ArgumentException("Audit details are invalid.",nameof(detailsJson));
        if(correlationId?.Length>100) throw new ArgumentException("Audit correlation ID is too long.",nameof(correlationId));
        return new AdminAuditEvent { ActorUserId=actorUserId,Action=action,EntityType=entityType,EntityKey=entityKey,DetailsJson=detailsJson,CorrelationId=correlationId,CreatedAtUtc=DateTime.UtcNow };
    }
}