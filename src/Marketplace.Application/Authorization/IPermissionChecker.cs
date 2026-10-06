namespace Marketplace.Application.Authorization;
public interface IPermissionChecker
{
 Task<bool> HasPermissionAsync(long userId,string permission,CancellationToken cancellationToken=default);
}