using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.TwoCheckout.Components
{
    [ViewComponent(Name = "PaymentTwoCheckout")]
    public class PaymentTwoCheckoutViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            return View("~/Plugins/Payments.TwoCheckout/Views/PaymentInfo.cshtml");
        }
    }
}
