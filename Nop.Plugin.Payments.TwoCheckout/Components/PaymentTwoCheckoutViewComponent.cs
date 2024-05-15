using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.TwoCheckout.Components
{
    /// <summary>
    /// Represents payment info view component
    /// </summary>
    public class PaymentTwoCheckoutViewComponent : NopViewComponent
    {
        /// <summary>
        /// Invoke view component
        /// </summary>
        /// <param name="widgetZone">Widget zone name</param>
        /// <param name="additionalData">Additional data</param>
        /// <returns>View component result</returns>
        public IViewComponentResult Invoke(string widgetZone, object additionalData)
        {
            return View("~/Plugins/Payments.TwoCheckout/Views/PaymentInfo.cshtml");
        }
    }
}