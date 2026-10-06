using Marketplace.Application.Abstractions;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
namespace Marketplace.Application.Catalog;
public sealed class CategoryManagementService
{
 private readonly ICategoryRepository _repo;private readonly IIdGenerator _ids;private readonly IUnitOfWork _uow;
 public CategoryManagementService(ICategoryRepository repo,IIdGenerator ids,IUnitOfWork uow){_repo=repo;_ids=ids;_uow=uow;}
 public async Task<long> CreateAsync(string name,string slug,long? parentId,CancellationToken ct=default){if(await _repo.SlugExistsAsync(parentId,slug,null,ct))throw new DomainException("Category slug already exists.");var id=await _ids.NextAsync(ct);var path=id.ToString();if(parentId.HasValue){var p=await _repo.GetAsync(parentId.Value,ct)??throw new DomainException("Parent category not found.");if(!p.IsActive)throw new DomainException("Parent category is inactive.");path=p.Path+"-"+id;}var c=Category.Create(id,name,slug,path,parentId);_repo.Add(c);await _uow.SaveChangesAsync(ct);return id;}
 public async Task RenameAsync(long id,string name,string slug,CancellationToken ct=default){var c=await _repo.GetAsync(id,ct)??throw new DomainException("Category not found.");if(await _repo.SlugExistsAsync(c.ParentCategoryId,slug,id,ct))throw new DomainException("Category slug already exists.");c.Rename(name,slug);await _uow.SaveChangesAsync(ct);}
 public async Task DeactivateAsync(long id,CancellationToken ct=default){var c=await _repo.GetAsync(id,ct)??throw new DomainException("Category not found.");c.Deactivate();await _uow.SaveChangesAsync(ct);}
 public Task<List<Category>> GetChildrenAsync(long? parentId,CancellationToken ct=default)=>_repo.GetChildrenAsync(parentId,ct);
}