namespace Nop.Plugin.Payments.TwoCheckout
{
    /// <summary>
    /// Represents plugin constants
    /// </summary>
    public class TwoCheckoutDefaults
    {
        /// <summary>
        /// Gets a name of the view component to display payment info in public store
        /// </summary>
        public const string PAYMENT_INFO_VIEW_COMPONENT_NAME = "PaymentTwoCheckout";

        /// <summary>
        /// Gets payment method system name
        /// </summary>
        public static string SystemName => "Payments.TwoCheckout";

        /// <summary>
        /// Gets the service URL
        /// </summary>
        public static string ServiceUrl => "https://www.2checkout.com/checkout/purchase";

        /// <summary>
        /// Gets the configuration route name
        /// </summary>
        public static string ConfigurationRouteName => "Plugin.Payments.TwoCheckout.Configure";

        /// <summary>
        /// Gets the IPN handler route name
        /// </summary>
        public static string IpnRouteName => "Plugin.Payments.TwoCheckout.IPNHandler";
    }
}