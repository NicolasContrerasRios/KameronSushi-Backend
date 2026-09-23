using KameronSushi.Application.Admin;

namespace KameronSushi.Application.Abstractions;

public interface IAdminStore
{
    Task<IReadOnlyList<AdminCategory>> GetCategoriesAsync(CancellationToken cancellationToken);
    Task<AdminCategory> SaveCategoryAsync(long? categoryId, SaveAdminCategory category, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminProduct>> GetProductsAsync(CancellationToken cancellationToken);
    Task<AdminProduct> CreateProductAsync(SaveAdminProduct product, CancellationToken cancellationToken);
    Task<AdminProduct?> UpdateProductAsync(long productId, SaveAdminProduct product, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminWrapper>> GetWrappersAsync(CancellationToken cancellationToken);
    Task<AdminWrapper> SaveWrapperAsync(long? wrapperId, SaveAdminWrapper wrapper, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminSauce>> GetSaucesAsync(CancellationToken cancellationToken);
    Task<AdminSauce> SaveSauceAsync(long? sauceId, SaveAdminSauce sauce, CancellationToken cancellationToken);
    Task<AdminProductConfiguration?> GetProductConfigurationAsync(long productId, CancellationToken cancellationToken);
    Task<AdminProductConfiguration?> SaveProductConfigurationAsync(long productId, SaveAdminProductConfiguration configuration, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminRewardProduct>> GetRewardsAsync(CancellationToken cancellationToken);
    Task<AdminRewardProduct> SaveRewardAsync(SaveAdminRewardProduct reward, CancellationToken cancellationToken);
    Task<bool> DisableRewardAsync(long rewardProductId, CancellationToken cancellationToken);
}
