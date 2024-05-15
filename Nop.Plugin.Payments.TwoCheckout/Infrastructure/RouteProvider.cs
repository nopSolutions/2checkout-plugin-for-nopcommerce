using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;
using Nop.Web.Infrastructure;

namespace Nop.Plugin.Payments.TwoCheckout.Infrastructure
{
    /// <summary>
    /// Represents plugin route provider
    /// </summary>
    public class RouteProvider : BaseRouteProvider, IRouteProvider
    {
        /// <summary>
        /// Register routes
        /// </summary>
        /// <param name="endpointRouteBuilder">Route builder</param>
        public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
        {
            //get language pattern
            //it's not needed to use language pattern in AJAX requests and for actions returning the result directly (e.g. file to download),
            //use it only for URLs of pages that the user can go to
            var lang = GetLanguageRoutePattern();

            endpointRouteBuilder.MapControllerRoute(name: TwoCheckoutDefaults.ConfigurationRouteName,
                pattern: "Plugins/PaymentTwoCheckout/Configure",
                defaults: new { controller = "PaymentTwoCheckout", action = "Configure", area = AreaNames.Admin });

            endpointRouteBuilder.MapControllerRoute(name: TwoCheckoutDefaults.CompletedRouteName,
                pattern: $"{lang}/Plugins/PaymentTwoCheckout/Completed",
                defaults: new { controller = "PaymentTwoCheckoutPublic", action = "Completed" });

            endpointRouteBuilder.MapControllerRoute(name: TwoCheckoutDefaults.IpnRouteName,
                pattern: "Plugins/PaymentTwoCheckout/IPNHandler",
                defaults: new { controller = "PaymentTwoCheckoutIpn", action = "IPNHandler" });
        }

        /// <summary>
        /// Gets a priority of route provider
        /// </summary>
        public int Priority => 0;
    }
}