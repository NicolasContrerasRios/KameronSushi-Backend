using KameronSushi.Application.Admin;

namespace KameronSushi.Application.Abstractions;

public interface IAdminStore
{
    Task<IReadOnlyList<AdminCategory>> GetCategoriesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminProduct>> GetProductsAsync(CancellationToken cancellationToken);
    Task<AdminProduct> CreateProductAsync(SaveAdminProduct product, CancellationToken cancellationToken);
    Task<AdminProduct?> UpdateProductAsync(long productId, SaveAdminProduct product, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminRewardProduct>> GetRewardsAsync(CancellationToken cancellationToken);
    Task<AdminRewardProduct> SaveRewardAsync(SaveAdminRewardProduct reward, CancellationToken cancellationToken);
    Task<bool> DisableRewardAsync(long rewardProductId, CancellationToken cancellationToken);
}
