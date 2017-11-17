using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;
using Microsoft.AspNetCore.Builder;

namespace Nop.Plugin.Payments.TwoCheckout
{
    public partial class RouteProvider : IRouteProvider
    {
        #region Methods

        public void RegisterRoutes(IRouteBuilder routeBuilder)
        {
            //IPNHandler
            routeBuilder.MapRoute("Plugin.Payments.TwoCheckout.IPNHandler",
                 "Plugins/PaymentTwoCheckout/IPNHandler",
                 new { controller = "PaymentTwoCheckout", action = "IPNHandler" });
        }

        #endregion

        #region Properties

        public int Priority
        {
            get
            {
                return 0;
            }
        }

        #endregion
    }
}
