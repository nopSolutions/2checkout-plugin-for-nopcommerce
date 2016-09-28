using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.TwoCheckout
{
    public class TwoCheckoutPaymentSettings : ISettings
    {
        public bool UseSandbox { get; set; }
        public string AccountNumber { get; set; }
        public bool UseMd5Hashing { get; set; }
        public string SecretWord { get; set; }
        public decimal AdditionalFee { get; set; }
        public bool AdditionalFeePercentage { get; set; }
    }
}
