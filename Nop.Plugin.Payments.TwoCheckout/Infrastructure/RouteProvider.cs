using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.TwoCheckout.Infrastructure
{
    /// <summary>
    /// Represents plugin route provider
    /// </summary>
    public class RouteProvider : IRouteProvider
    {
        /// <summary>
        /// Register routes
        /// </summary>
        /// <param name="endpointRouteBuilder">Route builder</param>
        public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
        {
            endpointRouteBuilder.MapControllerRoute(TwoCheckoutDefaults.ConfigurationRouteName,
                "Plugins/PaymentTwoCheckout/Configure",
                new { controller = "PaymentTwoCheckout", action = "Configure", area = AreaNames.Admin });

            endpointRouteBuilder.MapControllerRoute(TwoCheckoutDefaults.CompletedRouteName,
                "Plugins/PaymentTwoCheckout/Completed",
                new { controller = "PaymentTwoCheckout", action = "Completed" });

            endpointRouteBuilder.MapControllerRoute(TwoCheckoutDefaults.IpnRouteName,
                "Plugins/PaymentTwoCheckout/IPNHandler",
                new { controller = "PaymentTwoCheckoutIpn", action = "IPNHandler" });
        }

        /// <summary>
        /// Gets a priority of route provider
        /// </summary>
        public int Priority => 0;
    }
}