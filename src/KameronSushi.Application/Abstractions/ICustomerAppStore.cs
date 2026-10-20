using KameronSushi.Application.Customers;

namespace KameronSushi.Application.Abstractions;

public interface ICustomerAppStore
{
    Task<CustomerSession> RegisterAsync(RegisterCustomer request, CancellationToken cancellationToken);
    Task<CustomerSession?> LoginAsync(LoginCustomer request, CancellationToken cancellationToken);
    Task<CustomerProfile?> GetSessionCustomerAsync(string token, CancellationToken cancellationToken);
    Task RevokeSessionAsync(string token, CancellationToken cancellationToken);
    Task<CustomerHome> GetHomeAsync(long? customerId, CancellationToken cancellationToken);
}
