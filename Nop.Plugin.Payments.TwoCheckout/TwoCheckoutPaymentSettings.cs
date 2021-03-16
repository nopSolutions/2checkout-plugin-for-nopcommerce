using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.TwoCheckout
{
    /// <summary>
    /// Represents plugin settings
    /// </summary>
    public class TwoCheckoutPaymentSettings : ISettings
    {
        /// <summary>
        /// Gets or sets an account number
        /// </summary>
        public string AccountNumber { get; set; }

        /// <summary>
        /// Gets or sets a secret word
        /// </summary>
        public string SecretWord { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use sandbox (testing environment)
        /// </summary>
        public bool UseSandbox { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use MD5 hashing
        /// </summary>
        public bool UseMd5Hashing { get; set; }

        /// <summary>
        /// Gets or sets an additional fee
        /// </summary>
        public decimal AdditionalFee { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to "additional fee" is specified as percentage. true - percentage, false - fixed value
        /// </summary>
        public bool AdditionalFeePercentage { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to log IPN errors
        /// </summary>
        public bool LogIpnErrors { get; set; }
    }
}