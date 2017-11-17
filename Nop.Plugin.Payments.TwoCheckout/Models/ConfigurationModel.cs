using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Mvc.Models;

namespace Nop.Plugin.Payments.TwoCheckout.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        [NopResourceDisplayName("Plugins.Payments.2Checkout.UseSandbox")]
        public bool UseSandbox { get; set; }

        [NopResourceDisplayName("Plugins.Payments.2Checkout.AccountNumber")]
        public string AccountNumber { get; set; }

        [NopResourceDisplayName("Plugins.Payments.2Checkout.UseMd5Hashing")]
        public bool UseMd5Hashing { get; set; }

        [NopResourceDisplayName("Plugins.Payments.2Checkout.SecretWord")]
        public string SecretWord { get; set; }

        [NopResourceDisplayName("Plugins.Payments.2Checkout.AdditionalFee")]
        public decimal AdditionalFee { get; set; }

        [NopResourceDisplayName("Plugins.Payments.2Checkout.AdditionalFeePercentage")]
        public bool AdditionalFeePercentage { get; set; }
    }
}