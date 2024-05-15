using System.Threading.Tasks;
using Nop.Core.Domain.Orders;

namespace Nop.Plugin.Payments.TwoCheckout.Services
{
    /// <summary>
    /// Represents payment manager
    /// </summary>
    public interface IPaymentManager
    {
        /// <summary>
        /// Handle return request
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the order id
        /// </returns>
        Task<int?> HandleReturnRequestAsync();

        /// <summary>
        /// Create the redirect URL
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the redirect URL
        /// </returns>
        Task<string> GetRequestUrlAsync(Order order);
    }
}