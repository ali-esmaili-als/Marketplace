using Marketplace.Domain.Catalog;
namespace Marketplace.Application.Abstractions;
public interface ICategoryRepository
{
 Task<Category?> GetAsync(long id,CancellationToken ct=default);
 Task<bool> SlugExistsAsync(long? parentId,string slug,long? exceptId=null,CancellationToken ct=default);
 Task<List<Category>> GetChildrenAsync(long? parentId,CancellationToken ct=default);
 void Add(Category category);
}