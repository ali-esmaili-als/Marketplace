using Marketplace.Domain.Catalog;
using Marketplace.Domain.Inventory;
namespace Marketplace.Application.Abstractions;
public interface ICatalogManagementRepository
{
 Task<Product?> GetProductAsync(long id,CancellationToken ct=default);
 Task<ProductVariant?> GetVariantAsync(long id,CancellationToken ct=default);
 Task<bool> ProductBelongsToStoreAsync(long productId,long storeId,CancellationToken ct=default);
 Task<bool> VariantBelongsToStoreAsync(long variantId,long storeId,CancellationToken ct=default);
 Task<bool> CategoryIsActiveAsync(long categoryId,CancellationToken ct=default);
 Task<bool> WarrantyBelongsToStoreAsync(long warrantyId,long storeId,CancellationToken ct=default);
 Task<ProductAttribute?> GetAttributeAsync(long id,CancellationToken ct=default);
 Task<ProductAttributeValue?> GetAttributeValueAsync(long id,CancellationToken ct=default);
 Task<bool> AttributeBelongsToStoreAsync(long attributeId,long storeId,CancellationToken ct=default);
 Task<bool> AttributeValueBelongsToStoreAsync(long valueId,long storeId,CancellationToken ct=default);
 Task<InventoryItem?> GetInventoryAsync(long variantId,CancellationToken ct=default);
 Task<List<InventoryStockMovement>> GetStockMovementsAsync(long variantId,int take,CancellationToken ct=default);
 void AddStockMovement(InventoryStockMovement movement);
 Task<List<Product>> GetProductsAsync(long storeId,CancellationToken ct=default);
 Task<List<ProductVariant>> GetVariantsAsync(long productId,CancellationToken ct=default);
 Task<List<Warranty>> GetWarrantiesAsync(long storeId,CancellationToken ct=default);
 void AddProduct(Product x); void AddAttribute(ProductAttribute x); void AddAttributeValue(ProductAttributeValue x); void AddProductAttributeAssignment(ProductAttributeAssignment x); void AddVariantAttributeValue(VariantAttributeValue x); void AddVariant(ProductVariant x); void AddInventory(InventoryItem x); void AddWarranty(Warranty x); void AddProductWarranty(ProductWarranty x);
}