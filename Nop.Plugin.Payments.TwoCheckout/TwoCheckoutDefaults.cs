namespace Nop.Plugin.Payments.TwoCheckout
{
    /// <summary>
    /// Represents plugin constants
    /// </summary>
    public class TwoCheckoutDefaults
    {
        /// <summary>
        /// Gets payment method system name
        /// </summary>
        public static string SystemName => "Payments.TwoCheckout";

        /// <summary>
        /// Gets the service URL
        /// </summary>
        public static (string TwoCheckout, string ConvertPlus) ServiceUrl =>
            ("https://www.2checkout.com/checkout/purchase", "https://secure.2checkout.com/checkout/buy/");

        /// <summary>
        /// Gets the configuration route name
        /// </summary>
        public static string ConfigurationRouteName => "Plugin.Payments.TwoCheckout.Configure";

        /// <summary>
        /// Gets the route name of completed endpoint
        /// </summary>
        public static string CompletedRouteName => "Plugin.Payments.TwoCheckout.Completed";

        /// <summary>
        /// Gets the IPN handler route name
        /// </summary>
        public static string IpnRouteName => "Plugin.Payments.TwoCheckout.IPNHandler";
    }
}